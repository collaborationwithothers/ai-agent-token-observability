using TokenObservability.Jobs;
using TokenObservability.Infrastructure.Persistence;

var tenantMetadataStore = new InMemoryTenantMetadataStore(new SystemTenantMetadataClock());

return await TokenObservabilityJobsCommandLine.RunAsync(args, Console.Out, tenantMetadataStore);
