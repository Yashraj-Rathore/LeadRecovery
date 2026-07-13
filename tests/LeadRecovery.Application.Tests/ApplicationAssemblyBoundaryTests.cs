using System.Reflection;

namespace LeadRecovery.Application.Tests;

public sealed class ApplicationAssemblyBoundaryTests
{
    private static readonly string[] ForbiddenReferencePrefixes =
    [
        "Hangfire",
        "LeadRecovery.Api",
        "LeadRecovery.Infrastructure",
        "LeadRecovery.Worker",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Twilio",
    ];

    [Fact]
    public void ApplicationAssemblyDoesNotReferenceInfrastructureHostsOrAdapters()
    {
        Assembly applicationAssembly = Assembly.Load(new AssemblyName("LeadRecovery.Application"));
        string[] forbiddenReferences = applicationAssembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .Where(static reference => ForbiddenReferencePrefixes.Any(
                prefix => reference.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }
}
