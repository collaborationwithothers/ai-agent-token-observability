using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Ingestion;

public sealed record IngestionRejectionRecord(
    IngestionRejectionId IngestionRejectionId,
    CustomerOrganizationId? CustomerOrganizationId,
    string? HarnessSetupProfileId,
    ScopedIngestionCredentialId? ScopedIngestionCredentialId,
    string? DeclaredHarness,
    string SignalType,
    string RequestRoute,
    string ReasonCode,
    int HttpStatus,
    string CorrelationId,
    string? AuditEventId,
    IReadOnlyDictionary<string, string> EvidenceMetadata,
    DateTimeOffset ReceivedAtUtc);

public readonly record struct IngestionRejectionId(Guid Value)
{
    public static IngestionRejectionId Empty { get; } = new(Guid.Empty);

    public static IngestionRejectionId NewId()
    {
        return new IngestionRejectionId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public sealed record CreateIngestionRejectionRecordRequest(
    CustomerOrganizationId? CustomerOrganizationId,
    string? HarnessSetupProfileId,
    ScopedIngestionCredentialId? ScopedIngestionCredentialId,
    string? DeclaredHarness,
    string SignalType,
    string RequestRoute,
    string ReasonCode,
    int HttpStatus,
    string CorrelationId,
    string? AuditEventId,
    IReadOnlyDictionary<string, string> EvidenceMetadata);
