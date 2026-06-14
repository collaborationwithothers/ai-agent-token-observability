namespace TokenObservability.Domain.Tenancy;

public sealed record ProductUser(
    ProductUserId ProductUserId,
    CustomerOrganizationId CustomerOrganizationId,
    IdentityTenantId IdentityTenantId,
    string ExternalSubjectId,
    string DisplayLabel,
    string? Email,
    ProductUserStatus Status,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public readonly record struct ProductUserId(Guid Value)
{
    public static ProductUserId Empty { get; } = new(Guid.Empty);

    public static ProductUserId NewId()
    {
        return new ProductUserId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public enum ProductUserStatus
{
    Active,
    Disabled,
    Deleted
}

public sealed record CreateProductUserRequest(
    string ExternalSubjectId,
    string DisplayLabel,
    string? Email);
