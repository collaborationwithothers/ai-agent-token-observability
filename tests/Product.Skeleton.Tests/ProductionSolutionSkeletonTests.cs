namespace Product.Skeleton.Tests;

public sealed class ProductionSolutionSkeletonTests
{
    private static readonly string[] ExpectedProductionProjects =
    [
        "src/Product.Contracts/Product.Contracts.csproj",
        "src/Product.Domain/Product.Domain.csproj",
        "src/Product.Infrastructure/Product.Infrastructure.csproj"
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
