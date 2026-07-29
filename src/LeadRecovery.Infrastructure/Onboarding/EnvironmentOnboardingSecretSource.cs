using LeadRecovery.Application.Onboarding;

namespace LeadRecovery.Infrastructure.Onboarding;

internal sealed class EnvironmentOnboardingSecretSource : IOnboardingSecretSource
{
    public string? GetSecret(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Environment.GetEnvironmentVariable(name);
    }
}
