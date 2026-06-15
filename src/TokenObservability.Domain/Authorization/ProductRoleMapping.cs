using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Authorization;

public sealed record ProductRoleMapping(
    ProductRoleMappingId ProductRoleMappingId,
    CustomerOrganizationId CustomerOrganizationId,
    IdentityTenantId IdentityTenantId,
    ExternalPrincipalType ExternalPrincipalType,
    string ExternalPrincipalId,
    ProductRole ProductRole,
    ProductScopeKind ScopeKind,
    string? ScopeId,
    ProductRoleMappingStatus Status,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    ProductUserId CreatedByProductUserId,
    ProductUserId ChangedByProductUserId,
    string AuditEventId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public readonly record struct ProductRoleMappingId(Guid Value)
{
    public static ProductRoleMappingId Empty { get; } = new(Guid.Empty);

    public static ProductRoleMappingId NewId()
    {
        return new ProductRoleMappingId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public sealed record CreateProductRoleMappingRequest(
    IdentityTenantId IdentityTenantId,
    ExternalPrincipalType ExternalPrincipalType,
    string ExternalPrincipalId,
    ProductRole ProductRole,
    ProductScopeKind ScopeKind,
    string? ScopeId,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    AuthenticatedTokenClaims ChangedByClaims,
    string CorrelationId,
    string AuditEventId);
