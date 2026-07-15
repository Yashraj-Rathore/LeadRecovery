namespace LeadRecovery.Api.Demo;

internal sealed record DemoSeedSettings(
    string OwnerEmail,
    string OwnerPassword,
    string StaffEmail,
    string StaffPassword,
    string BetaOwnerEmail,
    string BetaOwnerPassword,
    string AlphaUrgentPhone,
    string AlphaBookingPhone,
    string BetaLeadPhone,
    string AlphaProviderPhone,
    string BetaProviderPhone)
{
    public static DemoSeedSettings FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new DemoSeedSettings(
            Require(configuration, "DemoSeed:OwnerEmail"),
            Require(configuration, "DemoSeed:OwnerPassword"),
            Require(configuration, "DemoSeed:StaffEmail"),
            Require(configuration, "DemoSeed:StaffPassword"),
            Require(configuration, "DemoSeed:BetaOwnerEmail"),
            Require(configuration, "DemoSeed:BetaOwnerPassword"),
            Require(configuration, "DemoSeed:AlphaUrgentPhone"),
            Require(configuration, "DemoSeed:AlphaBookingPhone"),
            Require(configuration, "DemoSeed:BetaLeadPhone"),
            Require(configuration, "DemoSeed:AlphaProviderPhone"),
            Require(configuration, "DemoSeed:BetaProviderPhone"));
    }

    private static string Require(IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Demo seed configuration '{key}' is required when demo seeding is enabled.");
        }

        return value;
    }
}
