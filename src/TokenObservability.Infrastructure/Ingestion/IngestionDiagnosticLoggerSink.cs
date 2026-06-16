using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TokenObservability.Domain.Ingestion;

namespace TokenObservability.Infrastructure.Ingestion;

public sealed class IngestionDiagnosticLoggerSink(
    ILogger<IngestionDiagnosticLoggerSink> logger) : IIngestionDiagnosticSink
{
    public const string ActivitySourceName = "TokenObservability.Ingestion.Diagnostics";

    private static readonly EventId IngestionDiagnosticEventId = new(5300, "TokenObservabilityIngestionDiagnostic");
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public Task RouteAsync(
        IngestionDiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = ActivitySource.StartActivity(
            diagnosticEvent.OperationName,
            ActivityKind.Internal);
        SetActivityTags(activity, diagnosticEvent);

        logger.LogInformation(
            IngestionDiagnosticEventId,
            "Token observability ingestion diagnostic event {OperationName} {SignalType} {Outcome} {RejectionReason} {HttpStatus} {RequestRoute} {CorrelationId} {CustomerOrganizationSlug} {Harness} {HarnessSetupProfileId} {DiagnosticStore} {ContentCaptureState} {AuditEventId} {TelemetryEnvelopeId} {AgentSessionId} {AggregateMetricExportOutcome} {AggregateMetricPointCount} {AggregateMetricExportFailureReason} {Properties}",
            diagnosticEvent.OperationName,
            diagnosticEvent.SignalType,
            diagnosticEvent.Outcome,
            diagnosticEvent.RejectionReason,
            diagnosticEvent.HttpStatus,
            diagnosticEvent.RequestRoute,
            diagnosticEvent.CorrelationId,
            diagnosticEvent.CustomerOrganizationSlug,
            diagnosticEvent.Harness,
            diagnosticEvent.HarnessSetupProfileId,
            diagnosticEvent.DiagnosticStore,
            diagnosticEvent.ContentCaptureState,
            diagnosticEvent.AuditEventId,
            diagnosticEvent.TelemetryEnvelopeId,
            diagnosticEvent.AgentSessionId,
            diagnosticEvent.AggregateMetricExportOutcome,
            diagnosticEvent.AggregateMetricPointCount,
            diagnosticEvent.AggregateMetricExportFailureReason,
            diagnosticEvent.Properties);

        return Task.CompletedTask;
    }

    private static void SetActivityTags(Activity? activity, IngestionDiagnosticEvent diagnosticEvent)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("tokenobs.operation_name", diagnosticEvent.OperationName);
        activity.SetTag("tokenobs.signal_type", diagnosticEvent.SignalType);
        activity.SetTag("tokenobs.ingestion.outcome", diagnosticEvent.Outcome);
        activity.SetTag("tokenobs.ingestion.rejection_reason", diagnosticEvent.RejectionReason);
        activity.SetTag("tokenobs.http_status", diagnosticEvent.HttpStatus);
        activity.SetTag("tokenobs.request_route", diagnosticEvent.RequestRoute);
        activity.SetTag("tokenobs.correlation_id", diagnosticEvent.CorrelationId);
        activity.SetTag("tokenobs.customer_organization_slug", diagnosticEvent.CustomerOrganizationSlug);
        activity.SetTag("tokenobs.harness", diagnosticEvent.Harness);
        activity.SetTag("tokenobs.harness_setup_profile_id", diagnosticEvent.HarnessSetupProfileId);
        activity.SetTag("tokenobs.diagnostic_store", diagnosticEvent.DiagnosticStore);
        activity.SetTag("tokenobs.content_capture_state", diagnosticEvent.ContentCaptureState);
        activity.SetTag("tokenobs.audit_event_id", diagnosticEvent.AuditEventId);
        activity.SetTag("tokenobs.telemetry_envelope_id", diagnosticEvent.TelemetryEnvelopeId);
        activity.SetTag("tokenobs.agent_session_id", diagnosticEvent.AgentSessionId);
        activity.SetTag("tokenobs.aggregate_metric_export_outcome", diagnosticEvent.AggregateMetricExportOutcome);
        activity.SetTag("tokenobs.aggregate_metric_point_count", diagnosticEvent.AggregateMetricPointCount);
        activity.SetTag("tokenobs.aggregate_metric_export_failure_reason", diagnosticEvent.AggregateMetricExportFailureReason);
    }
}
