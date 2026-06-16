using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Ingestion;

public sealed record AggregateMetricPointRecord(
    AggregateMetricPointId AggregateMetricPointId,
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string Name,
    double Value,
    string Unit,
    IReadOnlyDictionary<string, string> Labels,
    DateTimeOffset ExportedAtUtc);

public sealed record AggregateMetricPoint(
    string Name,
    double Value,
    string Unit,
    IReadOnlyDictionary<string, string> Labels,
    DateTimeOffset ExportedAtUtc);

public readonly record struct AggregateMetricPointId(Guid Value)
{
    public static AggregateMetricPointId Empty { get; } = new(Guid.Empty);

    public static AggregateMetricPointId NewId()
    {
        return new AggregateMetricPointId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public sealed record CreateAggregateMetricPointRecordRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string Name,
    double Value,
    string Unit,
    IReadOnlyDictionary<string, string> Labels);

public sealed record AggregateMetricExportFailureRecord(
    AggregateMetricExportFailureId AggregateMetricExportFailureId,
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string FailureReason,
    string CorrelationId,
    DateTimeOffset CreatedAtUtc);

public readonly record struct AggregateMetricExportFailureId(Guid Value)
{
    public static AggregateMetricExportFailureId Empty { get; } = new(Guid.Empty);

    public static AggregateMetricExportFailureId NewId()
    {
        return new AggregateMetricExportFailureId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public sealed record CreateAggregateMetricExportFailureRecordRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string FailureReason,
    string CorrelationId);

public interface IAggregateMetricSink
{
    Task ExportAsync(
        IReadOnlyList<AggregateMetricPoint> points,
        CancellationToken cancellationToken = default);
}
