namespace TokenObservability.Jobs;

public static class JobCommandCatalog
{
    public static IReadOnlyList<JobCommandDescriptor> Commands { get; } =
    [
        new("normalize-telemetry", "Normalize accepted telemetry envelopes into product records."),
        new("detect-hotspots", "Create or update Token Hotspots from normalized evidence."),
        new("generate-recommendations", "Generate deterministic and policy-approved recommendation records."),
        new("redact-content", "Run bounded redaction retries or review-approved excerpt processing."),
        new("refresh-pricing", "Fetch automated pricing seed candidates and prepare reviewable diffs."),
        new("retention-cleanup", "Apply Data-Class Retention Policy to product metadata and content references."),
        new("reprocess-session", "Re-run normalization, hotspot detection, or recommendations for a scoped session."),
        new("tenant-maintenance", "Run tenant-scoped maintenance tasks.")
    ];
}
