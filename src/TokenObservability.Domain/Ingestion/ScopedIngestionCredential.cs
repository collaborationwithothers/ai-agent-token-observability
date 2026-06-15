using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Ingestion;

public sealed record ScopedIngestionCredential(
    ScopedIngestionCredentialId ScopedIngestionCredentialId,
    CustomerOrganizationId CustomerOrganizationId,
    string HarnessSetupProfileId,
    ProductUserId ProductUserId,
    string CredentialHash,
    string? CredentialPrefix,
    CodingAgentHarness AllowedHarness,
    IReadOnlyList<ProductScope> AllowedScopes,
    ScopedIngestionCredentialStatus Status,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? RotatedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    ProductUserId CreatedByProductUserId,
    ProductUserId ChangedByProductUserId,
    IReadOnlyList<string> AuditEventIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public readonly record struct ScopedIngestionCredentialId(Guid Value)
{
    public static ScopedIngestionCredentialId Empty { get; } = new(Guid.Empty);

    public static ScopedIngestionCredentialId NewId()
    {
        return new ScopedIngestionCredentialId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public enum CodingAgentHarness
{
    CodexCli
}

public enum ScopedIngestionCredentialStatus
{
    Active,
    Disabled,
    Revoked,
    Expired,
    PendingRotation
}

public sealed record CreateScopedIngestionCredentialRequest(
    string HarnessSetupProfileId,
    ProductUserId ProductUserId,
    string CredentialHash,
    string? CredentialPrefix,
    CodingAgentHarness AllowedHarness,
    IReadOnlyList<ProductScope> AllowedScopes,
    DateTimeOffset ExpiresAtUtc,
    ProductUserId CreatedByProductUserId,
    ProductRole ActorEffectiveRole,
    string CorrelationId,
    string AuditEventId);

public sealed record ScopedIngestionCredentialLifecycleRequest(
    ProductUserId ChangedByProductUserId,
    ProductRole ActorEffectiveRole,
    string CorrelationId,
    string AuditEventId);

public sealed record RotateScopedIngestionCredentialRequest(
    string CredentialHash,
    string? CredentialPrefix,
    DateTimeOffset ExpiresAtUtc,
    ProductUserId ChangedByProductUserId,
    ProductRole ActorEffectiveRole,
    string CorrelationId,
    string AuditEventId);
