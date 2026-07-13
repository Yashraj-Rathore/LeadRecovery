namespace LeadRecovery.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class PostgreSqlIntegrationDefinition : ICollectionFixture<LeadRecoveryApiFixture>
{
    public const string Name = "PostgreSQL integration tests";
}
