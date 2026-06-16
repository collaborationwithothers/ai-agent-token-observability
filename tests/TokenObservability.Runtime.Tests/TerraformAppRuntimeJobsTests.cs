namespace TokenObservability.Runtime.Tests;

public sealed class TerraformAppRuntimeJobsTests
{
    private static readonly string[] ExpectedCommands =
    [
        "normalize-telemetry",
        "detect-hotspots",
        "generate-recommendations",
        "redact-content",
        "refresh-pricing",
        "retention-cleanup",
        "reprocess-session",
        "tenant-maintenance"
    ];

    [Fact]
    public void AppRuntimeStageUsesAvmWrapperForContainerAppsJobs()
    {
        var root = FindRepositoryRoot();
        var stageMain = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "stages", "app_runtime", "main.tf"));
        var wrapperMain = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "modules", "container_app_job", "main.tf"));

        Assert.Contains("source  = \"Azure/avm-res-app-job/azurerm\"", wrapperMain);
        Assert.Contains("version = \"0.2.1\"", wrapperMain);
        Assert.Contains("enable_telemetry", wrapperMain);
        Assert.Contains("false", wrapperMain);
        Assert.Contains("module \"container_app_jobs\"", stageMain);
        Assert.Contains("source   = \"../../modules/container_app_job\"", stageMain);
        Assert.DoesNotContain("resource \"azurerm_container_app_job\"", stageMain, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppRuntimeStageDefinesExplicitSharedJobCommands()
    {
        var root = FindRepositoryRoot();
        var locals = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "stages", "app_runtime", "locals.tf"));
        var main = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "stages", "app_runtime", "main.tf"));
        var variables = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "stages", "app_runtime", "variables.tf"));

        Assert.Contains("variable \"shared_jobs_image\"", variables);
        Assert.Contains("command = [\"dotnet\", \"TokenObservability.Jobs.dll\"]", main);
        Assert.Contains("args    = [each.value.command]", main);

        foreach (var expectedCommand in ExpectedCommands)
        {
            Assert.Contains($"command      = \"{expectedCommand}\"", locals);
        }
    }

    [Fact]
    public void AppRuntimeStageRoutesJobDiagnosticsAndAvoidsPlainSecretInputs()
    {
        var root = FindRepositoryRoot();
        var locals = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "stages", "app_runtime", "locals.tf"));
        var variables = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "stages", "app_runtime", "variables.tf"));
        var outputs = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "stages", "app_runtime", "outputs.tf"));
        var wrapperVariables = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "modules", "container_app_job", "variables.tf"));

        Assert.Contains("module.container_app_jobs", locals);
        Assert.Contains("variable \"container_app_job_key_vault_secret_ids\"", variables);
        Assert.Contains("variable \"container_app_job_secret_names\"", variables);
        Assert.Contains("output \"container_app_job_ids\"", outputs);
        Assert.Contains("Plain secret values are intentionally not supported", wrapperVariables);
        Assert.Contains("variable \"key_vault_secrets\"", wrapperVariables);
        Assert.Contains("key_vault_secret_id = string", wrapperVariables);
        Assert.DoesNotContain("container_app_job_secret_values", variables, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("variable \"plain_text_secrets\"", wrapperVariables, StringComparison.OrdinalIgnoreCase);
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
