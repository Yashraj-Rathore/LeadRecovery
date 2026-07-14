namespace LeadRecovery.Api.Demo;

internal static class DemoDataSeederExtensions
{
    public static async Task SeedDemoDataAsync(
        this IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue<bool>("DemoSeed:Enabled"))
        {
            return;
        }

        DemoSeedSettings settings = DemoSeedSettings.FromConfiguration(configuration);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        DemoDataSeeder seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        await seeder.SeedAsync(settings, cancellationToken);
    }
}
