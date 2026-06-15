namespace TokenObservability.Api;

internal sealed class TokenObservabilityApiReadinessOptions
{
    public bool? ProductMetadataStore { get; set; }

    public bool? TelemetryBackends { get; set; }

    public bool? ContentStore { get; set; }

    public bool? RecommendationDependencies { get; set; }

    public bool? AuthorizationEnforcement { get; set; }
}
