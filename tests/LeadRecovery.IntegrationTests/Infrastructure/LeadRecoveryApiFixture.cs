using LeadRecovery.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Testcontainers.PostgreSql;

namespace LeadRecovery.IntegrationTests.Infrastructure;

public sealed class LeadRecoveryApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = CreateDatabase();
    private WebApplicationFactory<Program>? _application;

    public WebApplicationFactory<Program> Application =>
        _application ?? throw new InvalidOperationException("The API fixture is not initialized.");

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await _database.StartAsync(cancellationToken);

        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Database", _database.GetConnectionString()));

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

        await _database.DisposeAsync();
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
