using TokenObservability.Domain.Ingestion;

namespace TokenObservability.Infrastructure.Ingestion;

public sealed class NoopAggregateMetricSink : IAggregateMetricSink
{
    public Task ExportAsync(
        IReadOnlyList<AggregateMetricPoint> points,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
