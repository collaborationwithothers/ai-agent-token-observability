using Microsoft.Extensions.Options;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Api;

internal static class TokenObservabilityApiEndpointExtensions
{
    public static void AddTokenObservabilityApiServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<TokenObservabilityApiReadinessOptions>(
            builder.Configuration.GetSection("ProductApi:Readiness"));
        builder.Services.AddTokenObservabilityAuthorizationContext();
    }

    public static void MapTokenObservabilityApiEndpoints(this WebApplication app)
    {
        app.MapGet("/healthz", GetHealth);
        app.MapGet("/readyz", GetReadiness);

        var api = app.MapGroup("/api/v1");

        api.MapGet("/", () => Results.Ok(new
        {
            service = "token-observability-api",
            apiVersion = "v1",
            status = "available"
        }));

        api.MapGet("/system/health", GetHealth);
        api.MapGet("/system/readiness", GetProtectedReadiness);
        api.MapGet("/me", GetCurrentUser);
        api.MapGet("/audit-events", GetAuditEvents);
        api.MapGet("/ingestion-rejections", GetIngestionRejections);
        api.MapGet("/sessions", GetSessions);
    }

    private static IResult GetHealth()
    {
        return Results.Ok(new
        {
            service = "token-observability-api",
            status = "healthy"
        });
    }

    private static IResult GetReadiness(IOptions<TokenObservabilityApiReadinessOptions> options)
    {
        var readiness = options.Value;
        var dependencies = new[]
        {
            ToDependency("product_metadata_store", readiness.ProductMetadataStore),
            ToDependency("telemetry_backends", readiness.TelemetryBackends),
            ToDependency("content_store", readiness.ContentStore),
            ToDependency("recommendation_dependencies", readiness.RecommendationDependencies),
            ToDependency("authorization_enforcement", readiness.AuthorizationEnforcement)
        };
        var ready = dependencies.All(static dependency => dependency.Status == "ready");

        return Results.Json(new
        {
            service = "token-observability-api",
            status = ready ? "ready" : "not_ready",
            dependencies
        }, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> GetProtectedReadiness(
        HttpContext httpContext,
        IOptions<TokenObservabilityApiReadinessOptions> options,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver)
    {
        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.AuditRead,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        if (!resolution.IsAllowed)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        return GetReadiness(options);
    }

    private static async Task<IResult> GetCurrentUser(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver)
    {
        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.CurrentUserRead,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var context = resolution.Context;

        return Results.Ok(new
        {
            customerOrganization = new
            {
                id = context.CustomerOrganization.CustomerOrganizationId.ToString(),
                slug = context.CustomerOrganization.Slug,
                displayName = context.CustomerOrganization.DisplayName,
                dataResidencyRegion = context.CustomerOrganization.DataResidencyRegion
            },
            identityTenant = new
            {
                id = context.IdentityTenant.IdentityTenantId.ToString(),
                provider = context.IdentityTenant.Provider.ToString(),
                externalTenantId = context.IdentityTenant.ExternalTenantId
            },
            productUser = new
            {
                id = context.ProductUser.ProductUserId.ToString(),
                displayLabel = context.ProductUser.DisplayLabel,
                email = context.ProductUser.Email
            },
            roles = context.EffectiveRoles.Select(static role => role.ToString()).ToArray(),
            scopes = context.MatchedMappings.Select(static mapping => new
            {
                kind = mapping.ScopeKind.ToString(),
                scopeId = mapping.ScopeId
            }).Distinct().ToArray(),
            correlationId = context.CorrelationId
        });
    }

    private static async Task<IResult> GetAuditEvents(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore)
    {
        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.AuditRead,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var events = await tenantMetadataStore.ListGovernanceAuditEventsAsync(
            resolution.Context.CustomerOrganization.CustomerOrganizationId);
        var filteredEvents = ApplyAuditEventFilters(httpContext, events).ToArray();

        return Results.Ok(new
        {
            items = filteredEvents.Select(static auditEvent => new
            {
                auditEventId = auditEvent.AuditEventId,
                customerOrganizationId = auditEvent.CustomerOrganizationId.ToString(),
                actorProductUserId = auditEvent.ActorProductUserId?.ToString(),
                effectiveRole = auditEvent.EffectiveRole?.ToString(),
                action = ToContractAction(auditEvent.Action),
                targetResourceKind = auditEvent.TargetResourceKind,
                targetResourceId = auditEvent.TargetResourceId,
                decision = auditEvent.Decision,
                denialReason = auditEvent.DenialReason?.ToString(),
                correlationId = auditEvent.CorrelationId,
                evidenceMetadata = auditEvent.EvidenceMetadata,
                createdAtUtc = auditEvent.CreatedAtUtc
            }),
            nextCursor = (string?)null,
            totalEstimate = filteredEvents.Length
        });
    }

    private static async Task<IResult> GetIngestionRejections(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore)
    {
        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.AuditRead,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var rejections = await tenantMetadataStore.ListIngestionRejectionsAsync(
            resolution.Context.CustomerOrganization.CustomerOrganizationId);
        var filteredRejections = ApplyIngestionRejectionFilters(httpContext, rejections).ToArray();

        return Results.Ok(new
        {
            items = filteredRejections.Select(static rejection => new
            {
                ingestionRejectionId = rejection.IngestionRejectionId.ToString(),
                customerOrganizationId = rejection.CustomerOrganizationId?.ToString(),
                harnessSetupProfileId = rejection.HarnessSetupProfileId,
                scopedIngestionCredentialId = rejection.ScopedIngestionCredentialId?.ToString(),
                declaredHarness = rejection.DeclaredHarness,
                signalType = rejection.SignalType,
                requestRoute = rejection.RequestRoute,
                reasonCode = rejection.ReasonCode,
                httpStatus = rejection.HttpStatus,
                correlationId = rejection.CorrelationId,
                auditEventId = rejection.AuditEventId,
                evidenceMetadata = rejection.EvidenceMetadata,
                receivedAtUtc = rejection.ReceivedAtUtc
            }),
            nextCursor = (string?)null,
            totalEstimate = filteredRejections.Length
        });
    }

    private static async Task<IResult> GetSessions(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore)
    {
        var scopedResolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.SessionReadScoped,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));
        var selfOnly = false;
        var resolution = scopedResolution;

        if (!scopedResolution.IsAllowed)
        {
            var selfResolution = await authorizationContextResolver.ResolveAsync(
                httpContext,
                ProductAuthorizationAction.SessionReadOwn,
                new ProductScope(ProductScopeKind.Self, ScopeId: null));

            if (selfResolution.IsAllowed)
            {
                resolution = selfResolution;
                selfOnly = true;
            }
        }

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, scopedResolution.Title, scopedResolution.StatusCode, scopedResolution.Code);
        }

        var customerOrganizationId = resolution.Context.CustomerOrganization.CustomerOrganizationId;
        var sessions = await tenantMetadataStore.ListAgentSessionsAsync(customerOrganizationId);
        var filteredSessions = ApplySessionFilters(httpContext, sessions);

        if (selfOnly)
        {
            filteredSessions = filteredSessions.Where(session =>
                session.ProductUserId == resolution.Context.ProductUser.ProductUserId);
        }

        var visibleSessions = filteredSessions.ToArray();
        var items = new List<object>(visibleSessions.Length);

        foreach (var session in visibleSessions)
        {
            var observations = await tenantMetadataStore.ListTokenObservationsAsync(
                customerOrganizationId,
                session.AgentSessionId);

            items.Add(new
            {
                agentSessionId = session.AgentSessionId.ToString(),
                customerOrganizationId = session.CustomerOrganizationId.ToString(),
                productUserId = session.ProductUserId.ToString(),
                harnessSetupProfileId = session.HarnessSetupProfileId,
                harness = session.Harness,
                providerSessionIdHash = session.ProviderSessionIdHash,
                startedAtUtc = session.StartedAtUtc,
                endedAtUtc = session.EndedAtUtc,
                sessionStatus = session.SessionStatus,
                tokenSummary = new
                {
                    metricStatus = ToWireMetricStatus(session.TokenMetricStatus),
                    metricConfidence = ToWireMetricConfidence(session.TokenMetricConfidence)
                },
                tokenObservations = observations.Select(static observation => new
                {
                    tokenObservationId = observation.TokenObservationId.ToString(),
                    metricName = ToWireMetricName(observation.MetricName),
                    value = observation.Value,
                    metricStatus = ToWireMetricStatus(observation.MetricStatus),
                    metricConfidence = ToWireMetricConfidence(observation.MetricConfidence),
                    sourceKind = ToWireSourceKind(observation.SourceKind),
                    sourceTelemetryEnvelopeId = observation.SourceTelemetryEnvelopeId?.ToString(),
                    createdAtUtc = observation.CreatedAtUtc
                }).ToArray()
            });
        }

        return Results.Ok(new
        {
            items,
            nextCursor = (string?)null,
            totalEstimate = visibleSessions.Length
        });
    }

    private static IResult CreateProblem(HttpContext httpContext, string title, int statusCode, string code)
    {
        return Results.Problem(
            type: $"https://docs.product.local/problems/{code.Replace('_', '-')}",
            title: title,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = TokenObservabilityCorrelationId.Resolve(httpContext)
            });
    }

    private static ReadinessDependency ToDependency(string name, bool? ready)
    {
        return new ReadinessDependency(name, ready switch
        {
            true => "ready",
            false => "not_ready",
            null => "not_configured"
        });
    }

    private static IEnumerable<GovernanceAuditEvent> ApplyAuditEventFilters(
        HttpContext httpContext,
        IEnumerable<GovernanceAuditEvent> auditEvents)
    {
        var filteredEvents = auditEvents;

        if (TryReadQuery(httpContext, "correlationId", out var correlationId))
        {
            filteredEvents = filteredEvents.Where(auditEvent =>
                StringComparer.Ordinal.Equals(auditEvent.CorrelationId, correlationId));
        }

        if (TryReadQuery(httpContext, "actorUserId", out var actorUserId))
        {
            filteredEvents = filteredEvents.Where(auditEvent =>
                StringComparer.Ordinal.Equals(auditEvent.ActorProductUserId?.ToString(), actorUserId));
        }

        if (TryReadQuery(httpContext, "scopeType", out var scopeType))
        {
            filteredEvents = filteredEvents.Where(auditEvent =>
                StringComparer.Ordinal.Equals(auditEvent.TargetResourceKind, scopeType));
        }

        if (TryReadQuery(httpContext, "scopeId", out var scopeId))
        {
            filteredEvents = filteredEvents.Where(auditEvent =>
                StringComparer.Ordinal.Equals(auditEvent.TargetResourceId, scopeId));
        }

        if (TryReadQuery(httpContext, "eventType", out var eventType))
        {
            filteredEvents = filteredEvents.Where(auditEvent =>
                StringComparer.Ordinal.Equals(ToContractAction(auditEvent.Action), eventType));
        }

        if (TryReadQuery(httpContext, "from", out var from) &&
            DateTimeOffset.TryParse(from, out var fromUtc))
        {
            filteredEvents = filteredEvents.Where(auditEvent => auditEvent.CreatedAtUtc >= fromUtc.ToUniversalTime());
        }

        if (TryReadQuery(httpContext, "to", out var to) &&
            DateTimeOffset.TryParse(to, out var toUtc))
        {
            filteredEvents = filteredEvents.Where(auditEvent => auditEvent.CreatedAtUtc <= toUtc.ToUniversalTime());
        }

        return filteredEvents;
    }

    private static IEnumerable<IngestionRejectionRecord> ApplyIngestionRejectionFilters(
        HttpContext httpContext,
        IEnumerable<IngestionRejectionRecord> rejections)
    {
        var filteredRejections = rejections;

        if (TryReadQuery(httpContext, "correlationId", out var correlationId))
        {
            filteredRejections = filteredRejections.Where(rejection =>
                StringComparer.Ordinal.Equals(rejection.CorrelationId, correlationId));
        }

        if (TryReadQuery(httpContext, "reasonCode", out var reasonCode))
        {
            filteredRejections = filteredRejections.Where(rejection =>
                StringComparer.Ordinal.Equals(rejection.ReasonCode, reasonCode));
        }

        if (TryReadQuery(httpContext, "signalType", out var signalType))
        {
            filteredRejections = filteredRejections.Where(rejection =>
                StringComparer.Ordinal.Equals(rejection.SignalType, signalType));
        }

        if (TryReadQuery(httpContext, "from", out var from) &&
            DateTimeOffset.TryParse(from, out var fromUtc))
        {
            filteredRejections = filteredRejections.Where(rejection => rejection.ReceivedAtUtc >= fromUtc.ToUniversalTime());
        }

        if (TryReadQuery(httpContext, "to", out var to) &&
            DateTimeOffset.TryParse(to, out var toUtc))
        {
            filteredRejections = filteredRejections.Where(rejection => rejection.ReceivedAtUtc <= toUtc.ToUniversalTime());
        }

        return filteredRejections;
    }

    private static IEnumerable<AgentSessionRecord> ApplySessionFilters(
        HttpContext httpContext,
        IEnumerable<AgentSessionRecord> sessions)
    {
        var filteredSessions = sessions;

        if (TryReadQuery(httpContext, "harnessSetupProfileId", out var harnessSetupProfileId))
        {
            filteredSessions = filteredSessions.Where(session =>
                StringComparer.Ordinal.Equals(session.HarnessSetupProfileId, harnessSetupProfileId));
        }

        if (TryReadQuery(httpContext, "metricStatus", out var metricStatus))
        {
            filteredSessions = filteredSessions.Where(session =>
                StringComparer.Ordinal.Equals(ToWireMetricStatus(session.TokenMetricStatus), metricStatus));
        }

        return filteredSessions;
    }

    private static bool TryReadQuery(HttpContext httpContext, string key, out string value)
    {
        value = httpContext.Request.Query[key].ToString().Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ToContractAction(ProductAuthorizationAction action)
    {
        return action.ToString();
    }

    private static string ToWireMetricName(TokenMetricName metricName)
    {
        return metricName switch
        {
            TokenMetricName.InputTokens => "input_tokens",
            TokenMetricName.OutputTokens => "output_tokens",
            TokenMetricName.CachedInputTokens => "cached_input_tokens",
            TokenMetricName.ReasoningOutputTokens => "reasoning_output_tokens",
            TokenMetricName.TotalTokens => "total_tokens",
            _ => throw new ArgumentOutOfRangeException(nameof(metricName), metricName, null)
        };
    }

    private static string ToWireMetricStatus(TokenMetricStatus metricStatus)
    {
        return metricStatus switch
        {
            TokenMetricStatus.Observed => "observed",
            TokenMetricStatus.Derived => "derived",
            TokenMetricStatus.Estimated => "estimated",
            TokenMetricStatus.Unavailable => "unavailable",
            TokenMetricStatus.NotApplicable => "not_applicable",
            TokenMetricStatus.Mixed => "mixed",
            _ => throw new ArgumentOutOfRangeException(nameof(metricStatus), metricStatus, null)
        };
    }

    private static string ToWireMetricConfidence(TokenMetricConfidence metricConfidence)
    {
        return metricConfidence switch
        {
            TokenMetricConfidence.Observed => "observed",
            TokenMetricConfidence.Deterministic => "deterministic",
            TokenMetricConfidence.Estimated => "estimated",
            TokenMetricConfidence.LlmInferred => "llm_inferred",
            TokenMetricConfidence.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(metricConfidence), metricConfidence, null)
        };
    }

    private static string ToWireSourceKind(TokenObservationSourceKind sourceKind)
    {
        return sourceKind switch
        {
            TokenObservationSourceKind.CodexEvent => "codex_event",
            TokenObservationSourceKind.OtlpMetric => "otel_metric",
            TokenObservationSourceKind.DerivedSummary => "derived_summary",
            TokenObservationSourceKind.Estimator => "estimator",
            TokenObservationSourceKind.Missing => "missing",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, null)
        };
    }

    private sealed record ReadinessDependency(string Name, string Status);
}
