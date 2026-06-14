namespace TokenObservability.Domain.Tenancy;

public sealed record CustomerOrganization(
    CustomerOrganizationId CustomerOrganizationId,
    string Slug,
    string DisplayName,
    string DataResidencyRegion,
    CustomerOrganizationIsolationTier IsolationTier,
    CustomerOrganizationStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public readonly record struct CustomerOrganizationId(Guid Value)
{
    public static CustomerOrganizationId Empty { get; } = new(Guid.Empty);

    public static CustomerOrganizationId NewId()
    {
        return new CustomerOrganizationId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public enum CustomerOrganizationIsolationTier
{
    Shared,
    DedicatedData,
    DedicatedCell
}

public enum CustomerOrganizationStatus
{
    Active,
    Suspended,
    Offboarding,
    Deleted
}

public sealed record CreateCustomerOrganizationRequest(
    string Slug,
    string DisplayName,
    string DataResidencyRegion,
    CustomerOrganizationIsolationTier IsolationTier);
