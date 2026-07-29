using System.Text.Json;
using System.Text.Json.Serialization;

using LeadRecovery.Application.Onboarding;

namespace LeadRecovery.Api.Onboarding;

internal static class TenantOnboardingCommand
{
    private const int MaximumPlanBytes = 262_144;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static async Task<bool> TryRunAsync(
        string[] args,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        bool validateOnly = args.Contains("--validate-onboarding", StringComparer.OrdinalIgnoreCase);
        bool provision = args.Contains("--onboard", StringComparer.OrdinalIgnoreCase);
        if (!validateOnly && !provision)
        {
            return false;
        }

        string option = validateOnly ? "--validate-onboarding" : "--onboard";
        int optionIndex = Array.FindIndex(args, value => value.Equals(option, StringComparison.OrdinalIgnoreCase));
        if (validateOnly == provision || optionIndex < 0 || optionIndex + 1 >= args.Length)
        {
            WriteResult(new
            {
                status = "InvalidCommand",
                errors = new[] { new { field = "command", message = "Specify exactly one onboarding command and a JSON plan path." } },
            });
            Environment.ExitCode = 2;
            return true;
        }

        try
        {
            FileInfo file = new(ResolvePlanPath(args[optionIndex + 1]));
            if (!file.Exists || file.Length > MaximumPlanBytes)
            {
                throw new InvalidDataException($"The onboarding plan must exist and be no larger than {MaximumPlanBytes} bytes.");
            }

            await using FileStream stream = file.OpenRead();
            TenantOnboardingPlan? plan = await JsonSerializer.DeserializeAsync<TenantOnboardingPlan>(stream, SerializerOptions, cancellationToken);
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            TenantOnboardingUseCase useCase = scope.ServiceProvider.GetRequiredService<TenantOnboardingUseCase>();
            if (validateOnly)
            {
                TenantOnboardingValidationResult validation = TenantOnboardingUseCase.Validate(plan);
                WriteResult(new { status = validation.IsValid ? "Valid" : "ValidationFailed", errors = validation.Errors });
                Environment.ExitCode = validation.IsValid ? 0 : 2;
                return true;
            }

            TenantOnboardingResult result = await useCase.ExecuteAsync(plan, cancellationToken);
            WriteResult(new { status = result.Status.ToString(), tenantId = result.TenantId, errors = result.Errors });
            Environment.ExitCode = result.Status == TenantOnboardingStatus.Activated ? 0 : 2;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            WriteResult(new
            {
                status = "InvalidPlan",
                errors = new[] { new { field = "plan", message = exception.Message } },
            });
            Environment.ExitCode = 2;
            return true;
        }
    }

    private static void WriteResult<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, SerializerOptions));

    private static string ResolvePlanPath(string candidate)
    {
        string directPath = Path.GetFullPath(candidate);
        if (File.Exists(directPath) || Path.IsPathFullyQualified(candidate))
        {
            return directPath;
        }

        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LeadRecovery.sln")))
            {
                return Path.Combine(directory.FullName, candidate);
            }

            directory = directory.Parent;
        }

        return directPath;
    }
}
