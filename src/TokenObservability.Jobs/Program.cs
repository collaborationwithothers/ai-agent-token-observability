using TokenObservability.Jobs;
using TokenObservability.Infrastructure.Persistence;
using Npgsql;

var clock = new SystemTenantMetadataClock();
var connectionString =
    Environment.GetEnvironmentVariable("ConnectionStrings__ProductMetadataStore") ??
    Environment.GetEnvironmentVariable("ProductMetadataStore__ConnectionString");
ITenantMetadataStore tenantMetadataStore = string.IsNullOrWhiteSpace(connectionString)
    ? new InMemoryTenantMetadataStore(clock)
    : new PostgreSqlTenantMetadataStore(NpgsqlDataSource.Create(connectionString), clock);

return await TokenObservabilityJobsCommandLine.RunAsync(args, Console.Out, tenantMetadataStore);
