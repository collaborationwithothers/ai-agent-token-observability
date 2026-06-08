var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPostgres("postgres")
    .AddDatabase("tokenobservability");

var dashboardApi = builder.AddProject<Projects.AiAgentTokenObservability_Dashboard_Api>("dashboard-api")
    .WithReference(database)
    .WaitFor(database);

var ingestionWorker = builder.AddProject<Projects.AiAgentTokenObservability_Ingestion_Worker>("ingestion-worker")
    .WithReference(database)
    .WaitFor(database);

var importPath = builder.Configuration["DirectFileImport:SourceFilePath"];
if (!string.IsNullOrWhiteSpace(importPath))
{
    ingestionWorker.WithEnvironment("DirectFileImport__SourceFilePath", importPath);
}

var repoPath = builder.Configuration["DirectFileImport:RepoPath"];
if (!string.IsNullOrWhiteSpace(repoPath))
{
    ingestionWorker.WithEnvironment("DirectFileImport__RepoPath", repoPath);
}

var repoFriendlyName = builder.Configuration["DirectFileImport:RepoFriendlyName"];
if (!string.IsNullOrWhiteSpace(repoFriendlyName))
{
    ingestionWorker.WithEnvironment("DirectFileImport__RepoFriendlyName", repoFriendlyName);
}

var developerIdentity = builder.Configuration["DirectFileImport:DeveloperIdentity"];
if (!string.IsNullOrWhiteSpace(developerIdentity))
{
    ingestionWorker.WithEnvironment("DirectFileImport__DeveloperIdentity", developerIdentity);
}

builder.AddProject<Projects.AiAgentTokenObservability_Dashboard_Web>("dashboard-web")
    .WithReference(dashboardApi)
    .WaitFor(dashboardApi);

builder.Build().Run();
