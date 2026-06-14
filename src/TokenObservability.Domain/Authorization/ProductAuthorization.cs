using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Authorization;

public enum ProductRole
{
    PlatformAdmin,
    SecurityReviewer,
    EngineeringLead,
    Developer,
    ReadOnlyViewer
}

public enum ProductScopeKind
{
    Organization,
    Team,
    Repository,
    HarnessProfile,
    Self,
    ContentReviewQueue,
    Pricing,
    TenantAdmin
}

public enum ExternalPrincipalType
{
    AppRole,
    GroupObjectId,
    UserSubject,
    ServicePrincipal
}

public enum ProductRoleMappingStatus
{
    Active,
    Disabled,
    Expired
}

public enum ProductAuthorizationAction
{
    TenantRead,
    TenantUpdate,
    OverviewRead,
    IdentityManage,
    HarnessProfileManage,
    IngestionCredentialManage,
    SessionReadOwn,
    SessionReadScoped,
    SessionInvestigate,
    ContentReviewRead,
    ContentReviewDecide,
    RecommendationRead,
    RecommendationRegenerate,
    PricingManage,
    BudgetManage,
    AuditRead
}

public enum ProductAuthorizationDenialReason
{
    None,
    MissingRoleMapping,
    InsufficientRole,
    ScopeMismatch,
    InvalidTenant
}

public sealed record ProductScope(ProductScopeKind Kind, string? ScopeId);

public sealed record AuthenticatedTokenClaims(
    string Issuer,
    string ExternalTenantId,
    string Audience,
    string Subject,
    string DisplayLabel,
    string? Email,
    IReadOnlyList<string> GroupObjectIds,
    IReadOnlyList<string> AppRoles);

public sealed record ProductAuthorizationDecision(
    bool IsAllowed,
    ProductAuthorizationDenialReason DenialReason,
    ProductUser? ProductUser,
    IReadOnlyList<ProductRole> EffectiveRoles,
    IReadOnlyList<ProductRoleMapping> MatchedMappings);
