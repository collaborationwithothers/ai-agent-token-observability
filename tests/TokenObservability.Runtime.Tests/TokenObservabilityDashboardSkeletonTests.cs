using System.Text.Json;

namespace TokenObservability.Runtime.Tests;

public sealed class TokenObservabilityDashboardSkeletonTests
{
    [Fact]
    public void TokenObservabilityDashboardPlaceholderUsesReactViteOutsideBlazorProject()
    {
        var root = FindRepositoryRoot();
        var dashboardRoot = Path.Combine(root, "web", "token-observability-dashboard");
        var packageJsonPath = Path.Combine(dashboardRoot, "package.json");

        Assert.True(Directory.Exists(dashboardRoot), "Product Dashboard must live under web/token-observability-dashboard.");
        Assert.True(File.Exists(packageJsonPath), "Product Dashboard package.json must exist.");
        Assert.True(File.Exists(Path.Combine(dashboardRoot, "index.html")));
        Assert.True(File.Exists(Path.Combine(dashboardRoot, "src", "main.tsx")));
        Assert.True(File.Exists(Path.Combine(dashboardRoot, "src", "App.tsx")));

        using var packageJson = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        var rootElement = packageJson.RootElement;

        Assert.Equal("token-observability-dashboard", rootElement.GetProperty("name").GetString());
        Assert.True(rootElement.GetProperty("dependencies").TryGetProperty("react", out _));
        Assert.True(rootElement.GetProperty("devDependencies").TryGetProperty("vite", out _));
        Assert.True(rootElement.GetProperty("devDependencies").TryGetProperty("@vitejs/plugin-react", out _));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AiAgentTokenObservability.Production.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
