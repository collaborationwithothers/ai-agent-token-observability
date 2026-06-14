namespace TokenObservability.Skeleton.Tests;

public sealed class ProductionSolutionSkeletonTests
{
    private static readonly string[] ExpectedProductionProjects =
    [
        "src/TokenObservability.Api/TokenObservability.Api.csproj",
        "src/TokenObservability.Contracts/TokenObservability.Contracts.csproj",
        "src/TokenObservability.Domain/TokenObservability.Domain.csproj",
        "src/TokenObservability.Infrastructure/TokenObservability.Infrastructure.csproj",
        "src/TokenObservability.Ingestion/TokenObservability.Ingestion.csproj",
        "src/TokenObservability.Jobs/TokenObservability.Jobs.csproj",
        "tests/TokenObservability.Runtime.Tests/TokenObservability.Runtime.Tests.csproj",
        "tests/TokenObservability.Skeleton.Tests/TokenObservability.Skeleton.Tests.csproj"
    ];

    private static readonly string[] ForbiddenLocalFirstProjects =
    [
        "src/AiAgentTokenObservability.AppHost/AiAgentTokenObservability.AppHost.csproj",
        "src/AiAgentTokenObservability.Dashboard.Web/AiAgentTokenObservability.Dashboard.Web.csproj",
        "src/AiAgentTokenObservability.Dashboard.Api/AiAgentTokenObservability.Dashboard.Api.csproj",
        "src/AiAgentTokenObservability.Ingestion.Worker/AiAgentTokenObservability.Ingestion.Worker.csproj"
    ];

    [Fact]
    public void ProductionSolutionIncludesOnlyProductionSkeletonProjects()
    {
        var root = FindRepositoryRoot();
        var solutionPath = Path.Combine(root, "AiAgentTokenObservability.Production.slnx");

        Assert.True(File.Exists(solutionPath), "The production solution entrypoint must exist.");

        var solution = File.ReadAllText(solutionPath);

        foreach (var expectedProject in ExpectedProductionProjects)
        {
            Assert.Contains(expectedProject, solution);
            Assert.True(
                File.Exists(Path.Combine(root, expectedProject)),
                $"Expected production project does not exist: {expectedProject}");
        }

        foreach (var forbiddenProject in ForbiddenLocalFirstProjects)
        {
            Assert.DoesNotContain(forbiddenProject, solution);
        }
    }

    [Fact]
    public void RootSolutionUsesProductionSkeletonProjectsOnly()
    {
        var root = FindRepositoryRoot();
        var solutionPath = Path.Combine(root, "AiAgentTokenObservability.slnx");

        Assert.True(File.Exists(solutionPath), "The root solution entrypoint must exist.");

        var solution = File.ReadAllText(solutionPath);

        foreach (var expectedProject in ExpectedProductionProjects)
        {
            Assert.Contains(expectedProject, solution);
        }

        foreach (var forbiddenProject in ForbiddenLocalFirstProjects)
        {
            Assert.DoesNotContain(forbiddenProject, solution);
        }
    }

    [Fact]
    public void LocalFirstRuntimeAndImportPathsAreAbsentFromActiveTree()
    {
        var root = FindRepositoryRoot();
        var forbiddenPaths = new[]
        {
            "src/AiAgentTokenObservability.AppHost",
            "src/AiAgentTokenObservability.Dashboard.Web",
            "src/AiAgentTokenObservability.Dashboard.Api",
            "src/AiAgentTokenObservability.Ingestion.Worker",
            "src/AiAgentTokenObservability.Storage/Import",
            "tests/AiAgentTokenObservability.Tests/CopilotJsonlImportTests.cs"
        };

        foreach (var forbiddenPath in forbiddenPaths)
        {
            Assert.False(
                Directory.Exists(Path.Combine(root, forbiddenPath)) || File.Exists(Path.Combine(root, forbiddenPath)),
                $"Local-first path must not remain active: {forbiddenPath}");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AiAgentTokenObservability.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
