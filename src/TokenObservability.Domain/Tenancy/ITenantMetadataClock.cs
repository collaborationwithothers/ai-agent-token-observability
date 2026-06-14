namespace TokenObservability.Domain.Tenancy;

public interface ITenantMetadataClock
{
    DateTimeOffset UtcNow { get; }
}
