namespace TokenObservability.Skeleton.Tests;

public sealed class TerraformStageSkeletonTests
{
    private static readonly string[] RequiredStages =
    [
        "foundation",
        "network_private_data_plane",
        "observability_foundation",
        "data_platform",
        "ai_services",
        "app_runtime",
        "managed_grafana",
        "edge"
    ];

    private static readonly string[] RequiredStageFiles =
    [
        "backend.tf",
        "providers.tf",
        "versions.tf",
        "variables.tf",
        "locals.tf",
        "main.tf",
        "outputs.tf",
        "README.md"
    ];

    [Fact]
    public void TerraformProductionStagesExistWithRequiredFiles()
    {
        var root = FindRepositoryRoot();

        foreach (var stage in RequiredStages)
        {
            var stageRoot = Path.Combine(root, "infrastructure", "azure", "stages", stage);

            Assert.True(Directory.Exists(stageRoot), $"Terraform stage directory must exist: {stage}");

            foreach (var file in RequiredStageFiles)
            {
                Assert.True(File.Exists(Path.Combine(stageRoot, file)), $"Terraform stage file must exist: {stage}/{file}");
            }
        }
    }

    [Fact]
    public void TerraformStagesUseAzureBlobBackendSkeletonAndWorkspaceContract()
    {
        var root = FindRepositoryRoot();

        foreach (var stage in RequiredStages)
        {
            var stageRoot = Path.Combine(root, "infrastructure", "azure", "stages", stage);
            var backend = File.ReadAllText(Path.Combine(stageRoot, "backend.tf"));
            var variables = File.ReadAllText(Path.Combine(stageRoot, "variables.tf"));
            var locals = File.ReadAllText(Path.Combine(stageRoot, "locals.tf"));
            var readme = File.ReadAllText(Path.Combine(stageRoot, "README.md"));

            Assert.Contains("backend \"azurerm\"", backend);
            Assert.Contains($"{stage}.tfstate", readme);
            Assert.Contains("terraform init -backend=false", readme);
            Assert.Contains("terraform validate", readme);

            Assert.Contains("dv", variables);
            Assert.Contains("qa", variables);
            Assert.Contains("pp", variables);
            Assert.Contains("pd", variables);
            Assert.Contains("allowed_regions", variables);
            Assert.Contains("allowed_customer_organization_slugs", variables);
            Assert.Contains("terraform_workspace_name", variables);
            Assert.Contains("terraform_workspace_name must match {environment}_{azureRegion}_{customerOrganizationSlug}", variables);

            Assert.Contains("expected_workspace_name", locals);
            Assert.Contains("configured_workspace_name", locals);
            Assert.Contains("workspace_name_matches_context", locals);
            Assert.Contains("var.environment", locals);
            Assert.Contains("var.azure_region", locals);
            Assert.Contains("var.customer_organization_slug", locals);
        }
    }

    [Fact]
    public void TerraformSkeletonDocumentsAvmFirstResourcePolicy()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "README.md"));

        Assert.Contains("Azure Verified Modules", readme);
        Assert.Contains("AVM", readme);
        Assert.Contains("AzureRM", readme);
        Assert.Contains("AzAPI", readme);
        Assert.Contains("No Azure resources are created by this skeleton issue", readme);
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
