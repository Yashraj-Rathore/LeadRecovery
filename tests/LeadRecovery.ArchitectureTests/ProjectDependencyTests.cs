using System.Xml.Linq;

namespace LeadRecovery.ArchitectureTests;

public sealed class ProjectDependencyTests
{
    private static readonly Dictionary<string, HashSet<string>> AllowedDependencies =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["LeadRecovery.Domain"] = [],
            ["LeadRecovery.Application"] = ["LeadRecovery.Domain"],
            ["LeadRecovery.Contracts"] = [],
            ["LeadRecovery.Infrastructure"] =
                ["LeadRecovery.Application", "LeadRecovery.Domain"],
            ["LeadRecovery.Api"] =
                ["LeadRecovery.Application", "LeadRecovery.Contracts", "LeadRecovery.Infrastructure"],
            ["LeadRecovery.Worker"] =
                ["LeadRecovery.Application", "LeadRecovery.Infrastructure"],
        };

    [Fact]
    public void SourceProjectReferencesFollowApprovedDependencyGraph()
    {
        DirectoryInfo repositoryRoot = FindRepositoryRoot();
        string sourceDirectory = Path.Combine(repositoryRoot.FullName, "src");
        string[] projectFiles = Directory.GetFiles(
            sourceDirectory,
            "*.csproj",
            SearchOption.AllDirectories);

        Assert.Equal(AllowedDependencies.Count, projectFiles.Length);

        foreach (string projectFile in projectFiles)
        {
            string projectName = Path.GetFileNameWithoutExtension(projectFile);
            Assert.True(
                AllowedDependencies.TryGetValue(projectName, out HashSet<string>? expected),
                $"Source project '{projectName}' is missing from the approved dependency graph.");

            XDocument project = XDocument.Load(projectFile);
            HashSet<string> actual = project
                .Descendants("ProjectReference")
                .Select(static reference => reference.Attribute("Include")?.Value)
                .Where(static include => !string.IsNullOrWhiteSpace(include))
                .Select(static include => include!.Replace('\\', '/'))
                .Select(static include =>
                    Path.GetFileNameWithoutExtension(include) ??
                    throw new InvalidOperationException("A project reference has no file name."))
                .ToHashSet(StringComparer.Ordinal);

            Assert.True(
                expected.SetEquals(actual),
                $"Project '{projectName}' references [{string.Join(", ", actual)}]; " +
                $"expected [{string.Join(", ", expected)}].");
        }
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LeadRecovery.sln")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
