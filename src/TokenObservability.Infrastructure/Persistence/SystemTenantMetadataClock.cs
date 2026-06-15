using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Infrastructure.Persistence;

public sealed class SystemTenantMetadataClock : ITenantMetadataClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
