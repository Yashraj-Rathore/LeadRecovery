using System.Reflection;

namespace LeadRecovery.Domain.Tests;

public sealed class DomainAssemblyBoundaryTests
{
    private static readonly string[] ForbiddenReferencePrefixes =
    [
        "Hangfire",
        "LeadRecovery.Api",
        "LeadRecovery.Application",
        "LeadRecovery.Contracts",
        "LeadRecovery.Infrastructure",
        "LeadRecovery.Worker",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Twilio",
    ];

    [Fact]
    public void DomainAssemblyDoesNotReferenceOuterLayersOrAdapters()
    {
        Assembly domainAssembly = Assembly.Load(new AssemblyName("LeadRecovery.Domain"));
        string[] forbiddenReferences = domainAssembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .Where(static reference => ForbiddenReferencePrefixes.Any(
                prefix => reference.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }
}
