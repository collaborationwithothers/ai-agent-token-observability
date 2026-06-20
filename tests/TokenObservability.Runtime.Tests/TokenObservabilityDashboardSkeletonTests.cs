using System.Text.Json;

namespace TokenObservability.Runtime.Tests;

public sealed class TokenObservabilityDashboardSkeletonTests
{
    [Fact]
    public void TokenObservabilityDashboardShellUsesReactViteOutsideBlazorProject()
    {
        var root = FindRepositoryRoot();
        var dashboardRoot = Path.Combine(root, "web", "token-observability-dashboard");
        var packageJsonPath = Path.Combine(dashboardRoot, "package.json");
        var appPath = Path.Combine(dashboardRoot, "src", "App.tsx");
        var indexPath = Path.Combine(dashboardRoot, "index.html");
        var dockerfilePath = Path.Combine(dashboardRoot, "Dockerfile");
        var nginxConfigPath = Path.Combine(dashboardRoot, "nginx.conf");
        var runtimeConfigPath = Path.Combine(dashboardRoot, "public", "runtime-config.js");
        var dockerEntrypointPath = Path.Combine(dashboardRoot, "docker-entrypoint.sh");

        Assert.True(Directory.Exists(dashboardRoot), "Product Dashboard must live under web/token-observability-dashboard.");
        Assert.True(File.Exists(packageJsonPath), "Product Dashboard package.json must exist.");
        Assert.True(File.Exists(indexPath));
        Assert.True(File.Exists(Path.Combine(dashboardRoot, "src", "main.tsx")));
        Assert.True(File.Exists(appPath));
        Assert.True(File.Exists(nginxConfigPath));
        Assert.True(File.Exists(runtimeConfigPath));
        Assert.True(File.Exists(dockerEntrypointPath));

        using var packageJson = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        var rootElement = packageJson.RootElement;

        Assert.Equal("token-observability-dashboard", rootElement.GetProperty("name").GetString());
        Assert.True(rootElement.GetProperty("dependencies").TryGetProperty("react", out _));
        Assert.True(rootElement.GetProperty("devDependencies").TryGetProperty("vite", out _));
        Assert.True(rootElement.GetProperty("devDependencies").TryGetProperty("@vitejs/plugin-react", out _));

        var appSource = File.ReadAllText(appPath);
        var indexSource = File.ReadAllText(indexPath);
        var dockerfile = File.ReadAllText(dockerfilePath);
        var nginxConfig = File.ReadAllText(nginxConfigPath);
        var runtimeConfig = File.ReadAllText(runtimeConfigPath);
        var dockerEntrypoint = File.ReadAllText(dockerEntrypointPath);

        Assert.Contains("defaultProductApiBaseUrl = \"/api/v1\"", appSource);
        Assert.Contains("fetch(`${productApiBaseUrl}/me`", appSource);
        Assert.Contains("requiredScopeKinds", appSource);
        Assert.Contains("scopeMatchesRoute", appSource);
        Assert.DoesNotContain("currentUser.scopes.length > 0", appSource);
        Assert.Contains("runtime-config.js", indexSource);
        Assert.Contains("__TOKENOBSERVABILITY_CONFIG__", runtimeConfig);
        Assert.Contains("PRODUCT_API_BASE_URL", dockerEntrypoint);
        Assert.Contains("PRODUCT_API_PUBLIC_HOSTNAME", dockerEntrypoint);
        Assert.Contains("nginxinc/nginx-unprivileged:stable-alpine", dockerfile);
        Assert.Contains("listen 8080", nginxConfig);
        Assert.Contains("try_files $uri $uri/ /index.html", nginxConfig);
        Assert.Contains("location = /healthz", nginxConfig);

        foreach (var route in new[]
        {
            "/overview",
            "/sessions",
            "/sessions/:sessionId",
            "/content-review",
            "/recommendations",
            "/admin/identity",
            "/admin/harness-setup",
            "/admin/pricing",
            "/admin/budgets",
            "/admin/audit",
            "/settings/me"
        })
        {
            Assert.Contains(route, appSource);
        }

        foreach (var stateText in new[]
        {
            "Loading dashboard context",
            "Tenant context required",
            "Unauthorized",
            "Not authorized",
            "No routes available",
            "No Product API data loaded"
        })
        {
            Assert.Contains(stateText, appSource);
        }

        Assert.DoesNotContain("Blazor", appSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sample", appSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("leaderboard", appSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ranking", appSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blame", appSource, StringComparison.OrdinalIgnoreCase);
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
