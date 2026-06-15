using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Authorization;

public sealed record GovernanceAuditEvent(
    string AuditEventId,
    CustomerOrganizationId CustomerOrganizationId,
    ProductUserId? ActorProductUserId,
    ProductRole? EffectiveRole,
    ProductAuthorizationAction Action,
    string TargetResourceKind,
    string TargetResourceId,
    string Decision,
    ProductAuthorizationDenialReason? DenialReason,
    string CorrelationId,
    IReadOnlyDictionary<string, string> EvidenceMetadata,
    DateTimeOffset CreatedAtUtc);
