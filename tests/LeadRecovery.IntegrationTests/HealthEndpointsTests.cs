using System.Net;

using LeadRecovery.IntegrationTests.Infrastructure;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class HealthEndpointsTests(LeadRecoveryApiFixture fixture)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpointReturnsOk(string path)
    {
        using HttpClient client = fixture.Application.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
