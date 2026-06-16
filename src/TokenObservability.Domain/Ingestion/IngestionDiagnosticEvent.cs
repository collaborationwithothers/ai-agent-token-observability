using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Ingestion;

public sealed record IngestionDiagnosticEvent(
    string OperationName,
    string SignalType,
    string Outcome,
    string? RejectionReason,
    int HttpStatus,
    string RequestRoute,
    string CorrelationId,
    CustomerOrganizationId? CustomerOrganizationId,
    string? CustomerOrganizationSlug,
    string? Harness,
    string? HarnessSetupProfileId,
    string DiagnosticStore,
    string ContentCaptureState,
    string? AuditEventId,
    string? TelemetryEnvelopeId,
    string? AgentSessionId,
    string AggregateMetricExportOutcome,
    int? AggregateMetricPointCount,
    string? AggregateMetricExportFailureReason,
    IReadOnlyDictionary<string, string> Properties);

public interface IIngestionDiagnosticSink
{
    Task RouteAsync(
        IngestionDiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken = default);
}
