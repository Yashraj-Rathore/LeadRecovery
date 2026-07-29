using System.Text.RegularExpressions;

using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed partial class OpenApiRouteConformanceTests(LeadRecoveryApiFixture fixture)
{
    [Fact]
    public void CommittedOpenApiOperationsExactlyMatchImplementedApiRoutes()
    {
        string contractPath = Path.Combine(
            AppContext.BaseDirectory,
            "Contracts",
            "openapi.yaml");
        string[] documented = ParseOpenApiOperations(contractPath);

        EndpointDataSource endpointSource =
            fixture.Application.Services.GetRequiredService<EndpointDataSource>();
        string[] implemented = endpointSource.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                HttpMethodMetadata? methods =
                    endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
                string route = endpoint.RoutePattern.RawText ?? string.Empty;
                if (methods is null ||
                    !route.StartsWith("/api/v1", StringComparison.Ordinal))
                {
                    return [];
                }

                string contractRoute = RouteConstraintRegex().Replace(
                    route["/api/v1".Length..],
                    "{$1}")
                    .TrimEnd('/');
                return methods.HttpMethods.Select(method =>
                    $"{method.ToUpperInvariant()} {contractRoute}");
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(documented, implemented);
    }

    private static string[] ParseOpenApiOperations(string contractPath)
    {
        Assert.True(File.Exists(contractPath), $"Missing copied contract: {contractPath}");
        List<string> operations = [];
        string? currentPath = null;
        foreach (string line in File.ReadLines(contractPath))
        {
            Match path = OpenApiPathRegex().Match(line);
            if (path.Success)
            {
                currentPath = path.Groups[1].Value;
                continue;
            }

            Match method = OpenApiMethodRegex().Match(line);
            if (currentPath is not null && method.Success)
            {
                operations.Add($"{method.Groups[1].Value.ToUpperInvariant()} {currentPath}");
            }
        }

        return operations.Order(StringComparer.Ordinal).ToArray();
    }

    [GeneratedRegex("^  (/[^:]+):\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex OpenApiPathRegex();

    [GeneratedRegex(
        "^    (get|post|put|patch|delete):\\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OpenApiMethodRegex();

    [GeneratedRegex("\\{([^}:]+):[^}]+\\}", RegexOptions.CultureInvariant)]
    private static partial Regex RouteConstraintRegex();
}
