using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Pricing;
using TokenObservability.Domain.Recommendations;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Ingestion;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Api;

internal static class TokenObservabilityApiEndpointExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PricingMutationIdempotencyTtl = TimeSpan.FromHours(24);

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
        api.MapGet("/overview", GetOverview);
        api.MapGet("/overview/token-timeline", GetOverviewTokenTimeline);
        api.MapGet("/pricing/basis", GetPricingBasis);
        api.MapPost("/pricing/basis", CreatePricingBasisOverride);
        api.MapPost("/pricing/basis/{pricingBasisId}/approve", ApprovePricingBasis);
        api.MapPost("/pricing/basis/{pricingBasisId}/reject", RejectPricingBasis);
        api.MapGet("/grafana/drilldown", GetGrafanaDrilldown);
        api.MapGet("/sessions", GetSessions);
        api.MapGet("/sessions/{sessionId}/recommendations", GetSessionRecommendations);
        api.MapGet("/recommendations/{recommendationId}", GetRecommendation);
        api.MapPost("/recommendations/regeneration-requests", CreateRecommendationRegenerationRequest);
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
        InMemoryTenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
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
        InMemoryTenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
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
            var tokenHotspots = await tenantMetadataStore.ListTokenHotspotsAsync(
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
                }).ToArray(),
                tokenHotspots = tokenHotspots.Select(static hotspot => new
                {
                    tokenHotspotId = hotspot.TokenHotspotId.ToString(),
                    hotspotType = ToWireHotspotType(hotspot.HotspotType),
                    findingState = ToWireHotspotFindingState(hotspot.FindingState),
                    attributionType = ToWireHotspotAttributionType(hotspot.AttributionType),
                    confidence = ToWireHotspotConfidence(hotspot.Confidence),
                    metricStatus = ToWireMetricStatus(hotspot.MetricStatus),
                    metricConfidence = ToWireMetricConfidence(hotspot.MetricConfidence),
                    promptCacheEvidenceState = ToWirePromptCacheEvidenceState(hotspot.PromptCacheEvidenceState),
                    harness = hotspot.Harness,
                    modelName = hotspot.ModelName,
                    evidenceSummary = hotspot.EvidenceSummary,
                    evidenceReferenceIds = hotspot.EvidenceReferenceIds,
                    tokenBurnScore = hotspot.TokenBurnScore,
                    estimatedCostImpact = hotspot.EstimatedCostImpact,
                    createdAtUtc = hotspot.CreatedAtUtc
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

    private static async Task<IResult> GetSessionRecommendations(
        string sessionId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore)
    {
        var resolution = await ResolveRecommendationAuthorizationAsync(
            httpContext,
            authorizationContextResolver,
            ProductAuthorizationAction.RecommendationRead);

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var customerOrganizationId = resolution.Context.CustomerOrganization.CustomerOrganizationId;
        var session = await FindSessionAsync(tenantMetadataStore, customerOrganizationId, sessionId);
        if (session is null)
        {
            return CreateProblem(httpContext, "Session was not found.", StatusCodes.Status404NotFound, "session_not_found");
        }

        if (!CanAccessRecommendationSession(resolution.Context, session))
        {
            return CreateProblem(httpContext, "Recommendation access is denied.", StatusCodes.Status403Forbidden, "recommendation_access_denied");
        }

        var recommendations = await tenantMetadataStore.ListRecommendationsForSessionAsync(
            customerOrganizationId,
            session.AgentSessionId);

        return Results.Ok(new
        {
            items = recommendations.Select(ToRecommendationResponse).ToArray(),
            nextCursor = (string?)null,
            totalEstimate = recommendations.Count
        });
    }

    private static async Task<IResult> GetRecommendation(
        string recommendationId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore)
    {
        if (!Guid.TryParse(recommendationId, out var parsedRecommendationId) || parsedRecommendationId == Guid.Empty)
        {
            return CreateProblem(httpContext, "Recommendation id is invalid.", StatusCodes.Status400BadRequest, "invalid_recommendation_id");
        }

        var resolution = await ResolveRecommendationAuthorizationAsync(
            httpContext,
            authorizationContextResolver,
            ProductAuthorizationAction.RecommendationRead);

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var customerOrganizationId = resolution.Context.CustomerOrganization.CustomerOrganizationId;
        var recommendation = await tenantMetadataStore.FindRecommendationAsync(
            customerOrganizationId,
            new RecommendationId(parsedRecommendationId));
        if (recommendation is null)
        {
            return CreateProblem(httpContext, "Recommendation was not found.", StatusCodes.Status404NotFound, "recommendation_not_found");
        }

        var session = await FindSessionAsync(tenantMetadataStore, customerOrganizationId, recommendation.AgentSessionId);
        if (session is null)
        {
            return CreateProblem(httpContext, "Session was not found.", StatusCodes.Status404NotFound, "session_not_found");
        }

        if (!CanAccessRecommendationSession(resolution.Context, session))
        {
            return CreateProblem(httpContext, "Recommendation access is denied.", StatusCodes.Status403Forbidden, "recommendation_access_denied");
        }

        return Results.Ok(ToRecommendationResponse(recommendation));
    }

    private static async Task<IResult> CreateRecommendationRegenerationRequest(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
    {
        if (!HasIdempotencyKey(httpContext))
        {
            return CreateProblem(httpContext, "Idempotency key required.", StatusCodes.Status400BadRequest, "idempotency_key_required");
        }

        var resolution = await ResolveRecommendationAuthorizationAsync(
            httpContext,
            authorizationContextResolver,
            ProductAuthorizationAction.RecommendationRegenerate);

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var rawBody = await ReadRequestBodyAsync(httpContext);
        var idempotency = await ResolvePricingMutationIdempotencyAsync(
            httpContext,
            resolution.Context,
            idempotencyStore,
            rawBody);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        var body = DeserializePricingMutationBody<RecommendationRegenerationBody>(rawBody);
        if (body is null)
        {
            return CreateProblem(httpContext, "Recommendation regeneration request body required.", StatusCodes.Status400BadRequest, "recommendation_regeneration_body_required");
        }

        var tokenHotspotId = ParseOptionalTokenHotspotId(body.TokenHotspotId);
        if (!string.IsNullOrWhiteSpace(body.TokenHotspotId) && tokenHotspotId is null)
        {
            return CreateProblem(httpContext, "Recommendation regeneration hotspot id is invalid.", StatusCodes.Status400BadRequest, "invalid_token_hotspot_id");
        }

        var targetSession = await FindRecommendationTargetSessionAsync(
            tenantMetadataStore,
            resolution.Context.CustomerOrganization.CustomerOrganizationId,
            body.AgentSessionId,
            tokenHotspotId);
        if (targetSession is null)
        {
            return CreateProblem(httpContext, "Recommendation regeneration target was not found.", StatusCodes.Status404NotFound, "recommendation_target_not_found");
        }

        if (!CanAccessRecommendationSession(resolution.Context, targetSession))
        {
            return CreateProblem(httpContext, "Recommendation regeneration access is denied.", StatusCodes.Status403Forbidden, "recommendation_access_denied");
        }

        try
        {
            var request = await tenantMetadataStore.CreateRecommendationRegenerationRequestAsync(
                new CreateRecommendationRegenerationRequest(
                    resolution.Context.CustomerOrganization.CustomerOrganizationId,
                    targetSession.AgentSessionId,
                    tokenHotspotId,
                    body.Reason ?? "not_provided",
                    $"audit-recommendation-regeneration-{Guid.NewGuid():N}",
                    resolution.Context.CorrelationId),
                resolution.Context.ProductUser.ProductUserId,
                resolution.Context.EffectiveRoles.First());

            var location = $"/api/v1/recommendations/regeneration-requests/{request.RecommendationRegenerationRequestId}";
            return await StorePricingMutationIdempotencyResultAsync(
                idempotencyStore,
                resolution.Context,
                idempotency,
                request.RecommendationRegenerationRequestId.ToString(),
                StatusCodes.Status201Created,
                location,
                ToRecommendationRegenerationResponse(request));
        }
        catch (ArgumentException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status400BadRequest, "invalid_recommendation_regeneration_request");
        }
        catch (InvalidOperationException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status409Conflict, "recommendation_regeneration_conflict");
        }
    }

    private static async Task<IResult> GetOverview(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore)
    {
        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.OverviewRead,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var costMix = await tenantMetadataStore.ListCostMixAsync(
            resolution.Context.CustomerOrganization.CustomerOrganizationId);

        return Results.Ok(new
        {
            costMix = costMix.Select(static bucket => new
            {
                providerName = bucket.ProviderName,
                modelName = bucket.ModelName,
                billingRoute = bucket.BillingRoute,
                tokenType = InMemoryTenantMetadataStore.ToWirePricingTokenType(bucket.TokenType),
                costStatus = InMemoryTenantMetadataStore.ToWireCostStatus(bucket.CostStatus),
                currency = bucket.Currency,
                estimatedCost = bucket.EstimatedCost,
                estimateCount = bucket.EstimateCount,
                metricStatus = ToWireMetricStatus(bucket.TokenMetricStatus)
            }).ToArray(),
            nextCursor = (string?)null,
            totalEstimate = costMix.Count
        });
    }

    private static async Task<IResult> GetPricingBasis(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore)
    {
        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.PricingManage,
            new ProductScope(ProductScopeKind.Pricing, ScopeId: "pricing"));

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var records = await tenantMetadataStore.ListPricingBasisRecordsAsync(
            resolution.Context.CustomerOrganization.CustomerOrganizationId);

        return Results.Ok(new
        {
            items = records.Select(ToPricingBasisResponse).ToArray(),
            nextCursor = (string?)null,
            totalEstimate = records.Count
        });
    }

    private static async Task<IResult> CreatePricingBasisOverride(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
    {
        if (!HasIdempotencyKey(httpContext))
        {
            return CreateProblem(httpContext, "Idempotency key required.", StatusCodes.Status400BadRequest, "idempotency_key_required");
        }

        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.PricingManage,
            new ProductScope(ProductScopeKind.Pricing, ScopeId: "pricing"));

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var rawBody = await ReadRequestBodyAsync(httpContext);
        var idempotency = await ResolvePricingMutationIdempotencyAsync(
            httpContext,
            resolution.Context,
            idempotencyStore,
            rawBody);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        var body = DeserializePricingMutationBody<PricingBasisOverrideRequest>(rawBody);
        if (body is null)
        {
            return CreateProblem(httpContext, "Pricing override request body required.", StatusCodes.Status400BadRequest, "pricing_body_required");
        }

        try
        {
            var record = await tenantMetadataStore.CreateCustomerPricingOverrideAsync(
                new CreatePricingBasisRecordRequest(
                    resolution.Context.CustomerOrganization.CustomerOrganizationId,
                    body.Harness,
                    body.ProviderName,
                    body.ModelName,
                    ParsePricingTokenType(body.TokenType),
                    body.BillingRoute,
                    body.Currency,
                    body.PricePerMillionTokens,
                    body.PricingVersion,
                    PricingSourceKind.AdminOverride,
                    PricingReviewState.Approved,
                    body.EffectiveFromUtc,
                    body.EffectiveToUtc,
                    $"audit-pricing-override-{Guid.NewGuid():N}",
                    body.SourceMetadata ?? new Dictionary<string, string>(StringComparer.Ordinal)),
                resolution.Context.ProductUser.ProductUserId,
                resolution.Context.EffectiveRoles.First(),
                resolution.Context.CorrelationId);

            var location = $"/api/v1/pricing/basis/{record.PricingBasisId}";
            return await StorePricingMutationIdempotencyResultAsync(
                idempotencyStore,
                resolution.Context,
                idempotency,
                operationId: record.PricingBasisId,
                StatusCodes.Status201Created,
                location,
                ToPricingBasisResponse(record));
        }
        catch (ArgumentException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status400BadRequest, "invalid_pricing_basis");
        }
        catch (InvalidOperationException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status409Conflict, "pricing_basis_conflict");
        }
    }

    private static async Task<IResult> ApprovePricingBasis(
        string pricingBasisId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
    {
        return await ReviewPricingBasis(
            pricingBasisId,
            httpContext,
            authorizationContextResolver,
            tenantMetadataStore,
            idempotencyStore,
            approve: true);
    }

    private static async Task<IResult> RejectPricingBasis(
        string pricingBasisId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
    {
        return await ReviewPricingBasis(
            pricingBasisId,
            httpContext,
            authorizationContextResolver,
            tenantMetadataStore,
            idempotencyStore,
            approve: false);
    }

    private static async Task<IResult> ReviewPricingBasis(
        string pricingBasisId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore,
        bool approve)
    {
        if (!HasIdempotencyKey(httpContext))
        {
            return CreateProblem(httpContext, "Idempotency key required.", StatusCodes.Status400BadRequest, "idempotency_key_required");
        }

        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.PricingManage,
            new ProductScope(ProductScopeKind.Pricing, ScopeId: "pricing"));

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var rawBody = await ReadRequestBodyAsync(httpContext);
        var idempotency = await ResolvePricingMutationIdempotencyAsync(
            httpContext,
            resolution.Context,
            idempotencyStore,
            rawBody);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        var body = DeserializePricingMutationBody<PricingBasisReviewBody>(rawBody);
        var request = new PricingBasisReviewRequest(
            resolution.Context.CustomerOrganization.CustomerOrganizationId,
            pricingBasisId,
            $"audit-pricing-review-{Guid.NewGuid():N}",
            resolution.Context.CorrelationId,
            body?.DecisionReason ?? "not_provided");

        try
        {
            var record = approve
                ? await tenantMetadataStore.ApprovePricingBasisAsync(
                    request,
                    resolution.Context.ProductUser.ProductUserId,
                    resolution.Context.EffectiveRoles.First())
                : await tenantMetadataStore.RejectPricingBasisAsync(
                    request,
                    resolution.Context.ProductUser.ProductUserId,
                    resolution.Context.EffectiveRoles.First());

            return await StorePricingMutationIdempotencyResultAsync(
                idempotencyStore,
                resolution.Context,
                idempotency,
                operationId: request.AuditEventId,
                StatusCodes.Status200OK,
                Location: null,
                ToPricingBasisResponse(record));
        }
        catch (ArgumentException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status400BadRequest, "invalid_pricing_review");
        }
        catch (InvalidOperationException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status409Conflict, "pricing_review_conflict");
        }
    }

    private static async Task<IResult> GetOverviewTokenTimeline(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        InMemoryTenantMetadataStore tenantMetadataStore)
    {
        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.OverviewRead,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        if (!TryReadDateQuery(httpContext, "from", out var from) ||
            !TryReadDateQuery(httpContext, "to", out var to) ||
            !TryReadIntQuery(httpContext, "movingAverageWindowDays", out var movingAverageWindowDays, defaultValue: 7) ||
            to < from ||
            movingAverageWindowDays is < 1 or > 90)
        {
            return CreateProblem(
                httpContext,
                "Invalid token timeline query.",
                StatusCodes.Status400BadRequest,
                "invalid_token_timeline_query");
        }

        var environment = TryReadQuery(httpContext, "environment", out var environmentQuery)
            ? environmentQuery
            : "dv";
        var query = new AggregateTokenTimelineQuery(from, to, movingAverageWindowDays);
        var exporter = new AggregateMetricsExporter(
            tenantMetadataStore,
            new NoopAggregateMetricSink(),
            new AggregateMetricsExportOptions(environment));

        try
        {
            var buckets = await exporter.BuildDailyTokenTimelineAsync(
                resolution.Context.CustomerOrganization.CustomerOrganizationId,
                query,
                httpContext.RequestAborted);

            return Results.Ok(new
            {
                items = buckets.Select(static bucket => new
                {
                    customerOrganizationSlug = bucket.CustomerOrganizationSlug,
                    environment = bucket.Environment,
                    region = bucket.Region,
                    bucketDateUtc = bucket.BucketDateUtc.ToString("yyyy-MM-dd"),
                    period = bucket.Period,
                    tokenBurn = bucket.TokenBurn,
                    metricStatus = ToWireMetricStatus(bucket.MetricStatus),
                    metricConfidence = ToWireMetricConfidence(bucket.MetricConfidence),
                    movingAverageTokenBurn = bucket.MovingAverageTokenBurn,
                    movingAverageWindowDays = bucket.MovingAverageWindowDays,
                    isDenseZeroBurn = bucket.IsDenseZeroBurn,
                    calculatedAtUtc = bucket.CalculatedAtUtc
                }).ToArray(),
                nextCursor = (string?)null,
                totalEstimate = buckets.Count
            });
        }
        catch (ArgumentException)
        {
            return CreateProblem(
                httpContext,
                "Invalid token timeline query.",
                StatusCodes.Status400BadRequest,
                "invalid_token_timeline_query");
        }
    }

    private static async Task<IResult> GetGrafanaDrilldown(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver)
    {
        if (!TryReadQuery(httpContext, "route", out var route) ||
            !IsAllowedGrafanaRoute(route) ||
            ContainsAbsoluteUrl(httpContext.Request.QueryString.Value))
        {
            return CreateProblem(
                httpContext,
                "Invalid Grafana drilldown filter.",
                StatusCodes.Status400BadRequest,
                "invalid_grafana_drilldown_filter");
        }

        foreach (var queryParameter in httpContext.Request.Query)
        {
            if (!IsAllowedGrafanaDrilldownParameter(queryParameter.Key))
            {
                return CreateProblem(
                    httpContext,
                    "Invalid Grafana drilldown filter.",
                    StatusCodes.Status400BadRequest,
                    "invalid_grafana_drilldown_filter");
            }

            if (queryParameter.Value.Any(static value => ContainsAbsoluteUrl(value)))
            {
                return CreateProblem(
                    httpContext,
                    "Invalid Grafana drilldown filter.",
                    StatusCodes.Status400BadRequest,
                    "invalid_grafana_drilldown_filter");
            }
        }

        var resolution = StringComparer.Ordinal.Equals(route, "/overview")
            ? await authorizationContextResolver.ResolveAsync(
                httpContext,
                ProductAuthorizationAction.OverviewRead,
                new ProductScope(ProductScopeKind.Organization, ScopeId: null))
            : await ResolveSessionReadForDrilldownAsync(httpContext, authorizationContextResolver);

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var filters = httpContext.Request.Query
            .Where(static parameter => !StringComparer.Ordinal.Equals(parameter.Key, "route"))
            .OrderBy(static parameter => parameter.Key, StringComparer.Ordinal)
            .ToDictionary(
                static parameter => parameter.Key,
                static parameter => parameter.Value.ToString(),
                StringComparer.Ordinal);

        return Results.Ok(new
        {
            route,
            filters
        });
    }

    private static async Task<TokenObservabilityAuthorizationResolution> ResolveSessionReadForDrilldownAsync(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver)
    {
        var scopedResolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.SessionReadScoped,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        if (scopedResolution.IsAllowed)
        {
            return scopedResolution;
        }

        var selfResolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.SessionReadOwn,
            new ProductScope(ProductScopeKind.Self, ScopeId: null));

        return selfResolution.IsAllowed ? selfResolution : scopedResolution;
    }

    private static async Task<TokenObservabilityAuthorizationResolution> ResolveRecommendationAuthorizationAsync(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ProductAuthorizationAction action)
    {
        var organizationResolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            action,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        if (organizationResolution.IsAllowed)
        {
            return organizationResolution;
        }

        var selfResolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            action,
            new ProductScope(ProductScopeKind.Self, ScopeId: null));

        return selfResolution.IsAllowed ? selfResolution : organizationResolution;
    }

    private static async Task<AgentSessionRecord?> FindSessionAsync(
        InMemoryTenantMetadataStore tenantMetadataStore,
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId)
    {
        var normalizedAgentSessionId = agentSessionId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedAgentSessionId))
        {
            return null;
        }

        var sessions = await tenantMetadataStore.ListAgentSessionsAsync(customerOrganizationId);
        return sessions.SingleOrDefault(session =>
            StringComparer.Ordinal.Equals(session.AgentSessionId, normalizedAgentSessionId));
    }

    private static async Task<AgentSessionRecord?> FindRecommendationTargetSessionAsync(
        InMemoryTenantMetadataStore tenantMetadataStore,
        CustomerOrganizationId customerOrganizationId,
        string? agentSessionId,
        TokenHotspotId? tokenHotspotId)
    {
        if (!string.IsNullOrWhiteSpace(agentSessionId))
        {
            return await FindSessionAsync(tenantMetadataStore, customerOrganizationId, agentSessionId);
        }

        if (tokenHotspotId is null)
        {
            return null;
        }

        var sessions = await tenantMetadataStore.ListAgentSessionsAsync(customerOrganizationId);
        foreach (var session in sessions)
        {
            var hotspots = await tenantMetadataStore.ListTokenHotspotsAsync(customerOrganizationId, session.AgentSessionId);
            if (hotspots.Any(hotspot => hotspot.TokenHotspotId == tokenHotspotId))
            {
                return session;
            }
        }

        return null;
    }

    private static bool CanAccessRecommendationSession(
        TokenObservabilityAuthorizationContext authorizationContext,
        AgentSessionRecord session)
    {
        if (authorizationContext.EffectiveRoles.Any(static role =>
                role is ProductRole.PlatformAdmin or ProductRole.SecurityReviewer or ProductRole.EngineeringLead))
        {
            return true;
        }

        return session.ProductUserId == authorizationContext.ProductUser.ProductUserId;
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

    private static bool TryReadDateQuery(HttpContext httpContext, string key, out DateOnly value)
    {
        value = default;

        return TryReadQuery(httpContext, key, out var rawValue) &&
            DateOnly.TryParseExact(
                rawValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out value);
    }

    private static bool TryReadIntQuery(
        HttpContext httpContext,
        string key,
        out int value,
        int defaultValue)
    {
        if (!TryReadQuery(httpContext, key, out var rawValue))
        {
            value = defaultValue;
            return true;
        }

        return int.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsAllowedGrafanaRoute(string route)
    {
        return route is "/overview" or "/sessions";
    }

    private static bool IsAllowedGrafanaDrilldownParameter(string parameter)
    {
        return parameter is "route" or
            "from" or
            "to" or
            "environment" or
            "region" or
            "harness" or
            "model" or
            "modelProvider" or
            "hotspotType" or
            "cacheBustCategory" or
            "findingState" or
            "signalType" or
            "result" or
            "rejectionReason";
    }

    private static bool ContainsAbsoluteUrl(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            (value.Contains("://", StringComparison.Ordinal) ||
                value.StartsWith("//", StringComparison.Ordinal));
    }

    private static bool HasIdempotencyKey(HttpContext httpContext)
    {
        return httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var value) &&
            value.Count == 1 &&
            !string.IsNullOrWhiteSpace(value.ToString());
    }

    private static async Task<string> ReadRequestBodyAsync(HttpContext httpContext)
    {
        using var reader = new StreamReader(
            httpContext.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: false);
        return await reader.ReadToEndAsync(httpContext.RequestAborted);
    }

    private static T? DeserializePricingMutationBody<T>(string rawBody)
    {
        return string.IsNullOrWhiteSpace(rawBody)
            ? default
            : JsonSerializer.Deserialize<T>(rawBody, JsonOptions);
    }

    private static async Task<PricingMutationIdempotencyResolution> ResolvePricingMutationIdempotencyAsync(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContext authorizationContext,
        IProductApiIdempotencyStore idempotencyStore,
        string rawBody)
    {
        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString().Trim();
        var route = httpContext.Request.Path.Value ?? string.Empty;
        var requestHash = ComputeRequestHash(rawBody);
        var reservation = await idempotencyStore.ReserveProductApiIdempotencyRecordAsync(
            new ReserveProductApiIdempotencyRecordRequest(
                authorizationContext.CustomerOrganization.CustomerOrganizationId,
                authorizationContext.ProductUser.ProductUserId,
                route,
                idempotencyKey,
                requestHash,
                DateTimeOffset.UtcNow.Add(PricingMutationIdempotencyTtl)));

        return reservation.State switch
        {
            ProductApiIdempotencyReservationState.Reserved => new PricingMutationIdempotencyResolution(route, idempotencyKey, requestHash, Result: null),
            ProductApiIdempotencyReservationState.Replay => new PricingMutationIdempotencyResolution(route, idempotencyKey, requestHash, ToStoredPricingMutationResult(reservation.Record!)),
            ProductApiIdempotencyReservationState.Conflict => new PricingMutationIdempotencyResolution(route, idempotencyKey, requestHash, CreateProblem(httpContext, "Idempotency key was already used with a different request body.", StatusCodes.Status409Conflict, "idempotency_key_conflict")),
            ProductApiIdempotencyReservationState.InProgress => new PricingMutationIdempotencyResolution(route, idempotencyKey, requestHash, CreateProblem(httpContext, "Idempotency key is already processing.", StatusCodes.Status409Conflict, "idempotency_key_in_progress")),
            _ => throw new InvalidOperationException("Unknown idempotency reservation state.")
        };
    }

    private static async Task<IResult> StorePricingMutationIdempotencyResultAsync(
        IProductApiIdempotencyStore idempotencyStore,
        TokenObservabilityAuthorizationContext authorizationContext,
        PricingMutationIdempotencyResolution idempotency,
        string operationId,
        int statusCode,
        string? Location,
        object response)
    {
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        var stored = await idempotencyStore.CompleteProductApiIdempotencyRecordAsync(
            new CompleteProductApiIdempotencyRecordRequest(
                authorizationContext.CustomerOrganization.CustomerOrganizationId,
                authorizationContext.ProductUser.ProductUserId,
                idempotency.Route,
                idempotency.IdempotencyKey,
                idempotency.RequestHash,
                operationId,
                statusCode,
                Location,
                responseJson,
                DateTimeOffset.UtcNow.Add(PricingMutationIdempotencyTtl)));
        return ToStoredPricingMutationResult(stored);
    }

    private static IResult ToStoredPricingMutationResult(ProductApiIdempotencyRecord record)
    {
        if (!record.IsCompleted)
        {
            throw new InvalidOperationException("Product API idempotency record is not complete.");
        }

        return new StoredPricingMutationResult(
            record.ResponseJson!,
            record.ResponseStatusCode!.Value,
            record.ResponseLocation);
    }

    private static string ComputeRequestHash(string rawBody)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawBody));
        return Convert.ToHexString(hash);
    }

    private static object ToPricingBasisResponse(PricingBasisRecord record)
    {
        return new
        {
            pricingBasisId = record.PricingBasisId,
            customerOrganizationId = record.CustomerOrganizationId.ToString(),
            harness = record.Harness,
            providerName = record.ProviderName,
            modelName = record.ModelName,
            tokenType = InMemoryTenantMetadataStore.ToWirePricingTokenType(record.TokenType),
            billingRoute = record.BillingRoute,
            currency = record.Currency,
            pricePerMillionTokens = record.PricePerMillionTokens,
            pricingVersion = record.PricingVersion,
            sourceKind = InMemoryTenantMetadataStore.ToWirePricingSourceKind(record.SourceKind),
            reviewState = InMemoryTenantMetadataStore.ToWirePricingReviewState(record.ReviewState),
            effectiveFromUtc = record.EffectiveFromUtc,
            effectiveToUtc = record.EffectiveToUtc,
            auditEventId = record.AuditEventId,
            sourceMetadata = record.SourceMetadata,
            createdAtUtc = record.CreatedAtUtc,
            updatedAtUtc = record.UpdatedAtUtc
        };
    }

    private static object ToRecommendationResponse(RecommendationRecord record)
    {
        return new
        {
            recommendationId = record.RecommendationId.ToString(),
            customerOrganizationId = record.CustomerOrganizationId.ToString(),
            agentSessionId = record.AgentSessionId,
            tokenHotspotId = record.TokenHotspotId?.ToString(),
            ruleId = record.RuleId,
            kind = InMemoryTenantMetadataStore.ToWireRecommendationKind(record.Kind),
            state = InMemoryTenantMetadataStore.ToWireRecommendationState(record.State),
            authorityState = InMemoryTenantMetadataStore.ToWireRecommendationAuthorityState(record.AuthorityState),
            confidence = InMemoryTenantMetadataStore.ToWireRecommendationConfidence(record.Confidence),
            validationState = InMemoryTenantMetadataStore.ToWireRecommendationValidationState(record.ValidationState),
            visibilityScope = InMemoryTenantMetadataStore.ToWireRecommendationVisibilityScope(record.VisibilityScope),
            evidencePacketVersion = record.EvidencePacketVersion,
            evidencePacketHash = record.EvidencePacketHash,
            summary = record.Summary,
            rationale = record.Rationale,
            recommendedAction = record.RecommendedAction,
            expectedBenefit = record.ExpectedBenefit,
            modelPolicyVersionId = record.ModelPolicyVersionId,
            promptTemplateVersion = record.PromptTemplateVersion,
            evidenceReferenceIds = record.EvidenceReferenceIds,
            policyMetadata = record.PolicyMetadata,
            auditEventId = record.AuditEventId,
            createdAtUtc = record.CreatedAtUtc
        };
    }

    private static object ToRecommendationRegenerationResponse(RecommendationRegenerationRequest request)
    {
        return new
        {
            recommendationRegenerationRequestId = request.RecommendationRegenerationRequestId.ToString(),
            customerOrganizationId = request.CustomerOrganizationId.ToString(),
            agentSessionId = request.AgentSessionId,
            tokenHotspotId = request.TokenHotspotId?.ToString(),
            reason = request.Reason,
            state = InMemoryTenantMetadataStore.ToWireRecommendationRegenerationState(request.State),
            auditEventId = request.AuditEventId,
            correlationId = request.CorrelationId,
            createdAtUtc = request.CreatedAtUtc
        };
    }

    private static TokenHotspotId? ParseOptionalTokenHotspotId(string? tokenHotspotId)
    {
        if (string.IsNullOrWhiteSpace(tokenHotspotId))
        {
            return null;
        }

        return Guid.TryParse(tokenHotspotId, out var parsed) && parsed != Guid.Empty
            ? new TokenHotspotId(parsed)
            : null;
    }

    private static PricingTokenType ParsePricingTokenType(string tokenType)
    {
        return tokenType.Trim().ToLowerInvariant() switch
        {
            "input" => PricingTokenType.Input,
            "output" => PricingTokenType.Output,
            "cached_input" => PricingTokenType.CachedInput,
            "reasoning_output" => PricingTokenType.ReasoningOutput,
            _ => throw new ArgumentException("Pricing token type is not supported.", nameof(tokenType))
        };
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

    private static string ToWireHotspotType(TokenHotspotType hotspotType)
    {
        return hotspotType switch
        {
            TokenHotspotType.PromptCacheBreakage => "prompt_cache_breakage",
            TokenHotspotType.LargeContext => "large_context",
            TokenHotspotType.ToolLoop => "tool_loop",
            TokenHotspotType.ModelRetry => "model_retry",
            TokenHotspotType.RepoContextBloat => "repo_context_bloat",
            TokenHotspotType.GeneratedArtifactBloat => "generated_artifact_bloat",
            TokenHotspotType.ExpensiveModelChoice => "expensive_model_choice",
            TokenHotspotType.ErrorRework => "error_rework",
            TokenHotspotType.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(hotspotType), hotspotType, null)
        };
    }

    private static string ToWireHotspotFindingState(TokenHotspotFindingState findingState)
    {
        return findingState switch
        {
            TokenHotspotFindingState.Confirmed => "confirmed",
            TokenHotspotFindingState.CandidateLlmInferred => "candidate_llm_inferred",
            TokenHotspotFindingState.CandidateCorrelated => "candidate_correlated",
            TokenHotspotFindingState.Rejected => "rejected",
            TokenHotspotFindingState.Superseded => "superseded",
            _ => throw new ArgumentOutOfRangeException(nameof(findingState), findingState, null)
        };
    }

    private static string ToWireHotspotAttributionType(TokenHotspotAttributionType attributionType)
    {
        return attributionType switch
        {
            TokenHotspotAttributionType.Direct => "direct",
            TokenHotspotAttributionType.Correlated => "correlated",
            TokenHotspotAttributionType.LlmInferred => "llm_inferred",
            TokenHotspotAttributionType.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(attributionType), attributionType, null)
        };
    }

    private static string ToWireHotspotConfidence(TokenHotspotConfidence confidence)
    {
        return confidence switch
        {
            TokenHotspotConfidence.High => "high",
            TokenHotspotConfidence.Medium => "medium",
            TokenHotspotConfidence.Low => "low",
            TokenHotspotConfidence.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(confidence), confidence, null)
        };
    }

    private static string ToWirePromptCacheEvidenceState(PromptCacheEvidenceState evidenceState)
    {
        return evidenceState switch
        {
            PromptCacheEvidenceState.KnownReason => "known_reason",
            PromptCacheEvidenceState.InferredCandidate => "inferred_candidate",
            PromptCacheEvidenceState.Unknown => "unknown",
            PromptCacheEvidenceState.Unavailable => "unavailable",
            PromptCacheEvidenceState.NotApplicable => "not_applicable",
            _ => throw new ArgumentOutOfRangeException(nameof(evidenceState), evidenceState, null)
        };
    }

    private sealed record ReadinessDependency(string Name, string Status);

    private sealed record PricingBasisOverrideRequest(
        string Harness,
        string ProviderName,
        string ModelName,
        string TokenType,
        string BillingRoute,
        string Currency,
        decimal PricePerMillionTokens,
        string PricingVersion,
        DateTimeOffset EffectiveFromUtc,
        DateTimeOffset? EffectiveToUtc,
        IReadOnlyDictionary<string, string> SourceMetadata);

    private sealed record PricingBasisReviewBody(string? DecisionReason);

    private sealed record RecommendationRegenerationBody(
        string? AgentSessionId,
        string? TokenHotspotId,
        string? Reason);

    private sealed record PricingMutationIdempotencyResolution(
        string Route,
        string IdempotencyKey,
        string RequestHash,
        IResult? Result);

    private sealed class StoredPricingMutationResult(
        string responseJson,
        int statusCode,
        string? location) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            if (!string.IsNullOrWhiteSpace(location))
            {
                httpContext.Response.Headers.Location = location;
            }

            await httpContext.Response.WriteAsync(responseJson);
        }
    }
}
