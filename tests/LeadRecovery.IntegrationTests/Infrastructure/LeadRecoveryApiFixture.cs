using LeadRecovery.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Testcontainers.PostgreSql;

namespace LeadRecovery.IntegrationTests.Infrastructure;

public sealed class LeadRecoveryApiFixture : IAsyncLifetime
{
    private const string ExternalDatabaseVariable =
        "LEADRECOVERY_TEST_DATABASE_CONNECTION_STRING";

    private readonly PostgreSqlContainer? _database;
    private readonly string? _externalDatabaseConnectionString;
    private WebApplicationFactory<Program>? _application;
    private string? _activeDatabaseConnectionString;

    public LeadRecoveryApiFixture()
    {
        _externalDatabaseConnectionString =
            Environment.GetEnvironmentVariable(ExternalDatabaseVariable);
        if (string.IsNullOrWhiteSpace(_externalDatabaseConnectionString))
        {
            _database = CreateDatabase();
        }
    }

    public WebApplicationFactory<Program> Application =>
        _application ?? throw new InvalidOperationException("The API fixture is not initialized.");

    public string DatabaseConnectionString =>
        _activeDatabaseConnectionString ?? throw new InvalidOperationException(
            "The API fixture is not initialized.");

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString;
        if (_database is null)
        {
            connectionString = _externalDatabaseConnectionString
                ?? throw new InvalidOperationException(
                    $"{ExternalDatabaseVariable} was unexpectedly unavailable.");
        }
        else
        {
            await _database.StartAsync(cancellationToken);
            connectionString = _database.GetConnectionString();
        }

        _activeDatabaseConnectionString = connectionString;

        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", connectionString);
            builder.UseSetting("RateLimiting:LoginPermitLimit", "100");
            builder.UseSetting("AUTOMATION_GLOBAL_ENABLED", "true");
            builder.UseSetting(
                "TWILIO_AUTH_TOKEN",
                "integration-test-twilio-auth-token");
            builder.UseSetting(
                "TWILIO_WEBHOOK_BASE_URL",
                "https://webhooks.example.test");
        });

        await using AsyncServiceScope scope = _application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private static PostgreSqlContainer CreateDatabase()
    {
        const string dockerApiVersionVariable = "DOCKER_API_VERSION";
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(dockerApiVersionVariable)))
        {
            // Docker API 1.43 is supported by Docker 24+ and newer daemons.
            Environment.SetEnvironmentVariable(dockerApiVersionVariable, "1.43");
        }

        return new PostgreSqlBuilder("postgres:18.4-bookworm").Build();
    }
}
