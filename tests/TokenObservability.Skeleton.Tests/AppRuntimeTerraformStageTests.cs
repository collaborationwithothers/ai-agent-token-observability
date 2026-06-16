namespace TokenObservability.Skeleton.Tests;

public sealed class AppRuntimeTerraformStageTests
{
    [Fact]
    public void AppRuntimeStageDefinesContainerAppsEnvironmentAndLongRunningServices()
    {
        var root = FindRepositoryRoot();
        var main = File.ReadAllText(Path.Combine(root, "infrastructure/azure/stages/app_runtime/main.tf"));
        var locals = File.ReadAllText(Path.Combine(root, "infrastructure/azure/stages/app_runtime/locals.tf"));

        Assert.Contains("resource \"azurerm_container_app_environment\" \"this\"", main);
        Assert.Contains("resource \"azurerm_container_app\" \"services\"", main);
        Assert.Contains("for_each = local.long_running_container_apps", main);
        Assert.Contains("resource \"azurerm_user_assigned_identity\" \"services\"", main);

        Assert.Contains("product_dashboard", locals);
        Assert.Contains("product_api", locals);
        Assert.Contains("product_ingestion_endpoint", locals);
    }

    [Fact]
    public void AppRuntimeStagePinsImagePortIngressAndProbeContracts()
    {
        var root = FindRepositoryRoot();
        var locals = File.ReadAllText(Path.Combine(root, "infrastructure/azure/stages/app_runtime/locals.tf"));
        var variables = File.ReadAllText(Path.Combine(root, "infrastructure/azure/stages/app_runtime/variables.tf"));
        var viteConfig = File.ReadAllText(Path.Combine(root, "web/token-observability-dashboard/vite.config.ts"));
        var packageJson = File.ReadAllText(Path.Combine(root, "web/token-observability-dashboard/package.json"));

        Assert.Contains("var.dashboard_image", locals);
        Assert.Contains("var.product_api_image", locals);
        Assert.Contains("var.product_ingestion_image", locals);
        Assert.Contains("var.dashboard_target_port", locals);
        Assert.Contains("var.product_api_target_port", locals);
        Assert.Contains("var.product_ingestion_target_port", locals);
        Assert.Contains("\"/health/live\"", locals);
        Assert.Contains("\"/health/ready\"", locals);
        Assert.Contains("external_enabled", locals);
        Assert.Contains("default     = 8080", variables);
        Assert.Contains("port: 8080", viteConfig);
        Assert.Contains("\"start\": \"vite preview --host 0.0.0.0 --port 8080 --strictPort\"", packageJson);
    }

    [Fact]
    public void AppRuntimeStageRequiresActualTerraformWorkspace()
    {
        var root = FindRepositoryRoot();
        var locals = File.ReadAllText(Path.Combine(root, "infrastructure/azure/stages/app_runtime/locals.tf"));
        var readme = File.ReadAllText(Path.Combine(root, "infrastructure/azure/stages/app_runtime/README.md"));

        Assert.Contains("terraform.workspace == local.expected_workspace_name", locals);
        Assert.Contains("local.configured_workspace_name == terraform.workspace", locals);
        Assert.Contains("terraform.workspace != \"default\"", locals);
        Assert.Contains("terraform workspace select dv_eastus2_internal || terraform workspace new dv_eastus2_internal", readme);
    }

    [Fact]
    public void AppRuntimeStageRepresentsDiagnosticsAndAvoidsSecretValues()
    {
        var root = FindRepositoryRoot();
        var main = File.ReadAllText(Path.Combine(root, "infrastructure/azure/stages/app_runtime/main.tf"));
        var variables = File.ReadAllText(Path.Combine(root, "infrastructure/azure/stages/app_runtime/variables.tf"));

        Assert.Contains("resource \"azurerm_monitor_diagnostic_setting\" \"container_apps\"", main);
        Assert.Contains("log_analytics_workspace_id", variables);
        Assert.Contains("variable \"container_app_secret_names\"", variables);
        Assert.DoesNotContain("secret_value", variables, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", variables, StringComparison.OrdinalIgnoreCase);
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
