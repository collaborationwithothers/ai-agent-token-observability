namespace TokenObservability.Domain.Tenancy;

public sealed record IdentityTenant(
    IdentityTenantId IdentityTenantId,
    CustomerOrganizationId CustomerOrganizationId,
    IdentityTenantProvider Provider,
    string Issuer,
    string ExternalTenantId,
    IReadOnlyList<string> AllowedAudiences,
    Uri? JwksUri,
    string DisplayName,
    IdentityTenantStatus Status,
    DateTimeOffset? LastValidatedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public readonly record struct IdentityTenantId(Guid Value)
{
    public static IdentityTenantId Empty { get; } = new(Guid.Empty);

    public static IdentityTenantId NewId()
    {
        return new IdentityTenantId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public enum IdentityTenantProvider
{
    MicrosoftEntra
}

public enum IdentityTenantStatus
{
    Active,
    Disabled,
    PendingValidation
}

public sealed record CreateIdentityTenantRequest(
    IdentityTenantProvider Provider,
    string Issuer,
    string ExternalTenantId,
    IReadOnlyList<string> AllowedAudiences,
    Uri? JwksUri,
    string DisplayName);
