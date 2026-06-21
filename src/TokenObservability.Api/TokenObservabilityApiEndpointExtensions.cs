using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        builder.Services.AddTokenObservabilityAuthorizationContext();
    }

    public static void MapTokenObservabilityApiEndpoints(this WebApplication app)
    {
        app.Use(async (httpContext, next) =>
        {
            try
            {
                await next(httpContext);
            }
            catch (NotSupportedException ex) when (IsUnsupportedProductMetadataStoreSurface(ex))
            {
                if (httpContext.Response.HasStarted)
                {
                    throw;
                }

                httpContext.Response.Clear();
                await CreateProblem(
                    httpContext,
                    "The configured metadata store does not support this Product API surface.",
                    StatusCodes.Status501NotImplemented,
                    "metadata_store_not_supported").ExecuteAsync(httpContext);
            }
        });

        app.MapGet("/healthz", GetHealth);
        app.MapGet("/health/ready", GetReadiness);
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
        api.MapPost("/pricing/basis/{pricingBasisId}/supersede", SupersedePricingBasis);
        api.MapGet("/budgets/policies", GetBudgetPolicies);
        api.MapGet("/budgets/evaluations", GetBudgetEvaluations);
        api.MapPost("/budgets/policies", CreateBudgetPolicy);
        api.MapPatch("/budgets/policies/{budgetPolicyId}", UpdateBudgetPolicy);
        api.MapGet("/grafana/drilldown", GetGrafanaDrilldown);
        api.MapGet("/sessions", GetSessions);
        api.MapGet("/sessions/{sessionId}", GetSessionSummary);
        api.MapGet("/sessions/{sessionId}/timeline", GetSessionTimeline);
        api.MapGet("/sessions/{sessionId}/content-references", GetSessionContentReferences);
        api.MapGet("/content-review/items", GetContentReviewItems);
        api.MapGet("/content-review/items/{contentReferenceId}", GetContentReviewItem);
        api.MapPost("/content-review/items/{contentReferenceId}/retry-redaction", RetryContentRedaction);
        api.MapPost("/content-review/items/{contentReferenceId}/discard", DiscardContentReference);
        api.MapPost("/content-review/items/{contentReferenceId}/approve-excerpt", ApproveContentReferenceExcerpt);
        api.MapPost("/content-review/items/{contentReferenceId}/mark-recommendation-ineligible", MarkContentReferenceRecommendationIneligible);
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

    private static bool IsUnsupportedProductMetadataStoreSurface(NotSupportedException exception)
    {
        return exception.Message.Contains("PostgreSQL tenant metadata runtime support", StringComparison.Ordinal);
    }

    private static async Task<IResult> GetReadiness(
        IConfiguration configuration,
        ITenantMetadataStore tenantMetadataStore,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver)
    {
        var dependencies = new[]
        {
            await CheckProductMetadataStoreAsync(configuration, tenantMetadataStore),
            CheckRequiredConfiguration("telemetry_backends", configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] ?? configuration["AzureMonitor:ConnectionString"]),
            CheckRequiredConfiguration("content_store", configuration["TOKENOBSERVABILITY_STORAGE_ACCOUNT_NAME"]),
            CheckRequiredConfiguration("recommendation_dependencies", configuration["TOKENOBSERVABILITY_RECOMMENDATION_DEPLOYMENT_COUNT"]),
            CheckAuthorizationEnforcement(authorizationContextResolver)
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
        IConfiguration configuration,
        ITenantMetadataStore tenantMetadataStore,
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

        return await GetReadiness(configuration, tenantMetadataStore, authorizationContextResolver);
    }

    private static async Task<ReadinessDependency> CheckProductMetadataStoreAsync(
        IConfiguration configuration,
        ITenantMetadataStore tenantMetadataStore)
    {
        var customerOrganizationSlug = configuration["CUSTOMER_ORGANIZATION_SLUG"];

        if (string.IsNullOrWhiteSpace(customerOrganizationSlug) ||
            string.IsNullOrWhiteSpace(ReadProductMetadataStoreConnectionString(configuration)))
        {
            return new ReadinessDependency("product_metadata_store", "not_configured");
        }

        try
        {
            await tenantMetadataStore.FindCustomerOrganizationBySlugAsync(customerOrganizationSlug);
            return new ReadinessDependency("product_metadata_store", "ready");
        }
        catch
        {
            return new ReadinessDependency("product_metadata_store", "not_ready");
        }
    }

    private static string? ReadProductMetadataStoreConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString("ProductMetadataStore") ??
            configuration["ProductMetadataStore:ConnectionString"];
    }

    private static ReadinessDependency CheckAuthorizationEnforcement(
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver)
    {
        return authorizationContextResolver is null
            ? new ReadinessDependency("authorization_enforcement", "not_ready")
            : new ReadinessDependency("authorization_enforcement", "ready");
    }

    private static ReadinessDependency CheckRequiredConfiguration(string name, string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? new ReadinessDependency(name, "not_configured")
            : new ReadinessDependency(name, "ready");
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
        ITenantMetadataStore tenantMetadataStore,
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
        ITenantMetadataStore tenantMetadataStore,
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
        ITenantMetadataStore tenantMetadataStore)
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

    private static async Task<IResult> GetSessionContentReferences(
        string sessionId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore)
    {
        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.SessionInvestigate,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

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

        if (!CanAccessPrivilegedSession(resolution.Context, session))
        {
            return CreateProblem(httpContext, "Session content reference access is denied.", StatusCodes.Status403Forbidden, "content_reference_access_denied");
        }

        var references = await tenantMetadataStore.ListContentReferencesForSessionAsync(
            customerOrganizationId,
            session.AgentSessionId);

        return Results.Ok(new
        {
            items = references.Select(reference => ToContentReferenceResponse(reference, includeApprovedExcerpt: false)).ToArray(),
            nextCursor = (string?)null,
            totalEstimate = references.Count
        });
    }

    private static async Task<IResult> GetSessionSummary(
        string sessionId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore)
    {
        var resolution = await ResolveSessionSummaryAuthorizationAsync(httpContext, authorizationContextResolver);

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

        if (!CanAccessPrivilegedSession(resolution.Context, session))
        {
            return CreateProblem(httpContext, "Session investigation access is denied.", StatusCodes.Status403Forbidden, "session_access_denied");
        }

        var observations = await tenantMetadataStore.ListTokenObservationsAsync(customerOrganizationId, session.AgentSessionId);
        var hotspots = await tenantMetadataStore.ListTokenHotspotsAsync(customerOrganizationId, session.AgentSessionId);
        var contentReferences = await tenantMetadataStore.ListContentReferencesForSessionAsync(customerOrganizationId, session.AgentSessionId);
        var recommendations = await tenantMetadataStore.ListRecommendationsForSessionAsync(customerOrganizationId, session.AgentSessionId);
        var costEstimates = (await tenantMetadataStore.ListCostEstimatesAsync(customerOrganizationId))
            .Where(estimate => StringComparer.Ordinal.Equals(estimate.AgentSessionId, session.AgentSessionId))
            .ToArray();

        return Results.Ok(new
        {
            session = ToSessionMetadataResponse(session),
            harnessContext = new
            {
                harness = session.Harness,
                harnessSetupProfileId = session.HarnessSetupProfileId
            },
            modelContext = new
            {
                providerNames = costEstimates
                    .Select(static estimate => estimate.ProviderName)
                    .Where(static providerName => !string.IsNullOrWhiteSpace(providerName))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                modelNames = costEstimates
                    .Select(static estimate => estimate.ModelName)
                    .Concat(hotspots.Select(static hotspot => hotspot.ModelName))
                    .Where(static modelName => !string.IsNullOrWhiteSpace(modelName))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            },
            tokenSummary = new
            {
                metricStatus = ToWireMetricStatus(session.TokenMetricStatus),
                metricConfidence = ToWireMetricConfidence(session.TokenMetricConfidence),
                split = CreateTokenSplit(observations),
                observations = observations.Select(ToTokenObservationResponse).ToArray()
            },
            costSummary = CreateCostSummary(costEstimates),
            repositoryContext = new
            {
                evidenceState = session.RepositoryEvidenceState
            },
            tokenHotspots = hotspots.Select(ToTokenHotspotResponse).ToArray(),
            cacheDiagnostics = CreateCacheDiagnostics(observations, hotspots),
            contentEvidence = new
            {
                summary = session.ContentCaptureSummary,
                items = contentReferences.Select(ToSessionContentEvidenceResponse).ToArray()
            },
            recommendations = new
            {
                status = session.RecommendationStatus,
                items = recommendations.Select(ToRecommendationResponse).ToArray()
            },
            auditContext = new
            {
                correlationId = resolution.Context.CorrelationId,
                contentAuditEventIds = contentReferences.Select(static reference => reference.AuditEventId).Distinct(StringComparer.Ordinal).ToArray(),
                recommendationAuditEventIds = recommendations.Select(static recommendation => recommendation.AuditEventId).Distinct(StringComparer.Ordinal).ToArray()
            }
        });
    }

    private static async Task<IResult> GetSessionTimeline(
        string sessionId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore)
    {
        var resolution = await ResolveSessionSummaryAuthorizationAsync(httpContext, authorizationContextResolver);

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

        if (!CanAccessPrivilegedSession(resolution.Context, session))
        {
            return CreateProblem(httpContext, "Session investigation access is denied.", StatusCodes.Status403Forbidden, "session_access_denied");
        }

        var observations = await tenantMetadataStore.ListTokenObservationsAsync(customerOrganizationId, session.AgentSessionId);
        var hotspots = await tenantMetadataStore.ListTokenHotspotsAsync(customerOrganizationId, session.AgentSessionId);
        var contentReferences = await tenantMetadataStore.ListContentReferencesForSessionAsync(customerOrganizationId, session.AgentSessionId);
        var recommendations = await tenantMetadataStore.ListRecommendationsForSessionAsync(customerOrganizationId, session.AgentSessionId);
        var items = CreateSessionTimeline(session, observations, hotspots, contentReferences, recommendations);

        return Results.Ok(new
        {
            items,
            nextCursor = (string?)null,
            totalEstimate = items.Length
        });
    }

    private static async Task<IResult> GetContentReviewItems(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore)
    {
        var resolution = await ResolveContentReviewAuthorizationAsync(
            httpContext,
            authorizationContextResolver,
            ProductAuthorizationAction.ContentReviewRead);

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        ContentReferenceCaptureState? requestedState;
        try
        {
            requestedState = ParseOptionalContentReviewState(httpContext.Request.Query["state"].ToString());
        }
        catch (ArgumentException)
        {
            return CreateProblem(httpContext, "Content review state is not supported.", StatusCodes.Status400BadRequest, "validation_failed");
        }
        if (requestedState == ContentReferenceCaptureState.Captured ||
            requestedState == ContentReferenceCaptureState.MetadataOnly ||
            requestedState == ContentReferenceCaptureState.NotAllowed)
        {
            return CreateProblem(httpContext, "Content review state is not supported.", StatusCodes.Status400BadRequest, "validation_failed");
        }

        var references = await tenantMetadataStore.ListContentReviewItemsAsync(
            resolution.Context.CustomerOrganization.CustomerOrganizationId,
            requestedState);

        return Results.Ok(new
        {
            items = references.Select(reference => ToContentReferenceResponse(reference, includeApprovedExcerpt: false)).ToArray(),
            nextCursor = (string?)null,
            totalEstimate = references.Count
        });
    }

    private static async Task<IResult> GetContentReviewItem(
        string contentReferenceId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore)
    {
        var parsedContentReferenceId = ParseContentReferenceId(contentReferenceId);
        if (parsedContentReferenceId is null)
        {
            return CreateProblem(httpContext, "Content reference id is invalid.", StatusCodes.Status400BadRequest, "validation_failed");
        }

        var resolution = await ResolveContentReviewAuthorizationAsync(
            httpContext,
            authorizationContextResolver,
            ProductAuthorizationAction.ContentReviewRead);

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var reference = await tenantMetadataStore.FindContentReferenceAsync(
            resolution.Context.CustomerOrganization.CustomerOrganizationId,
            parsedContentReferenceId.Value);
        if (reference is null)
        {
            return CreateProblem(httpContext, "Content reference was not found.", StatusCodes.Status404NotFound, "not_found");
        }

        return Results.Ok(ToContentReferenceResponse(reference, includeApprovedExcerpt: true));
    }

    private static Task<IResult> RetryContentRedaction(
        string contentReferenceId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
    {
        return ReviewContentReference(
            contentReferenceId,
            httpContext,
            authorizationContextResolver,
            tenantMetadataStore,
            idempotencyStore,
            RedactionReviewDecision.Retry);
    }

    private static Task<IResult> DiscardContentReference(
        string contentReferenceId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
    {
        return ReviewContentReference(
            contentReferenceId,
            httpContext,
            authorizationContextResolver,
            tenantMetadataStore,
            idempotencyStore,
            RedactionReviewDecision.Discard);
    }

    private static Task<IResult> ApproveContentReferenceExcerpt(
        string contentReferenceId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
    {
        return ReviewContentReference(
            contentReferenceId,
            httpContext,
            authorizationContextResolver,
            tenantMetadataStore,
            idempotencyStore,
            RedactionReviewDecision.ApproveExcerpt);
    }

    private static Task<IResult> MarkContentReferenceRecommendationIneligible(
        string contentReferenceId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
    {
        return ReviewContentReference(
            contentReferenceId,
            httpContext,
            authorizationContextResolver,
            tenantMetadataStore,
            idempotencyStore,
            RedactionReviewDecision.MarkRecommendationIneligible);
    }

    private static async Task<IResult> ReviewContentReference(
        string contentReferenceId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore,
        RedactionReviewDecision decision)
    {
        if (!HasIdempotencyKey(httpContext))
        {
            return CreateProblem(httpContext, "Idempotency key required.", StatusCodes.Status400BadRequest, "idempotency_key_required");
        }

        var parsedContentReferenceId = ParseContentReferenceId(contentReferenceId);
        if (parsedContentReferenceId is null)
        {
            return CreateProblem(httpContext, "Content reference id is invalid.", StatusCodes.Status400BadRequest, "validation_failed");
        }

        var resolution = await ResolveContentReviewAuthorizationAsync(
            httpContext,
            authorizationContextResolver,
            ProductAuthorizationAction.ContentReviewDecide);

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

        var decisionBody = decision == RedactionReviewDecision.ApproveExcerpt
            ? null
            : DeserializePricingMutationBody<ContentReviewDecisionBody>(rawBody);
        var approveExcerptBody = decision == RedactionReviewDecision.ApproveExcerpt
            ? DeserializePricingMutationBody<ApproveExcerptBody>(rawBody)
            : null;

        try
        {
            var review = await tenantMetadataStore.ReviewContentReferenceAsync(new ReviewContentReferenceRequest(
                resolution.Context.CustomerOrganization.CustomerOrganizationId,
                parsedContentReferenceId.Value,
                resolution.Context.ProductUser.ProductUserId,
                resolution.Context.EffectiveRoles.First(),
                decision,
                decisionBody?.DecisionReason ?? approveExcerptBody?.DecisionReason,
                resolution.Context.CorrelationId,
                $"audit-content-review-{Guid.NewGuid():N}",
                approveExcerptBody?.ApprovedExcerpt));

            var reference = await tenantMetadataStore.FindContentReferenceAsync(
                resolution.Context.CustomerOrganization.CustomerOrganizationId,
                parsedContentReferenceId.Value);

            var response = new
            {
                redactionReviewId = review.RedactionReviewId.ToString(),
                contentReference = reference is null ? null : ToContentReferenceResponse(reference, includeApprovedExcerpt: true),
                auditEventId = review.AuditEventId,
                decision = InMemoryTenantMetadataStore.ToWireRedactionReviewDecision(review.Decision),
                decidedAtUtc = review.DecidedAtUtc
            };

            return await StorePricingMutationIdempotencyResultAsync(
                idempotencyStore,
                resolution.Context,
                idempotency,
                operationId: review.RedactionReviewId.ToString(),
                StatusCodes.Status200OK,
                Location: null,
                response);
        }
        catch (ArgumentException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status400BadRequest, "validation_failed");
        }
        catch (InvalidOperationException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status409Conflict, "conflict");
        }
    }

    private static async Task<IResult> GetSessionRecommendations(
        string sessionId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore)
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
        ITenantMetadataStore tenantMetadataStore)
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
        ITenantMetadataStore tenantMetadataStore,
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
        ITenantMetadataStore tenantMetadataStore)
    {
        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.OverviewRead,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var customerOrganizationId = resolution.Context.CustomerOrganization.CustomerOrganizationId;
        var costMix = ApplyOverviewCostMixFilters(
            httpContext,
            await tenantMetadataStore.ListCostMixAsync(customerOrganizationId)).ToArray();
        var aggregatePoints = ApplyOverviewAggregateFilters(
            httpContext,
            await tenantMetadataStore.ListAggregateMetricPointsAsync(customerOrganizationId)).ToArray();
        var hotspotPoints = aggregatePoints
            .Where(static point => point.Name is
                "tokenobs_hotspots_detected_total" or
                "tokenobs_hotspots_open" or
                "tokenobs_hotspot_estimated_cost_impact_usd")
            .ToArray();
        var ingestionRequestPoints = aggregatePoints
            .Where(static point => point.Name == "tokenobs_ingestion_requests_total")
            .ToArray();

        return Results.Ok(new
        {
            tokenSummary = new
            {
                totalTokens = SumPointValues(aggregatePoints, "tokenobs_tokens_total"),
                metricStates = SummarizePointsByLabel(
                    aggregatePoints.Where(static point => point.Name is "tokenobs_tokens_total" or "tokenobs_token_metric_states_total"),
                    "metric_status"),
                tokenTypes = SummarizePointsByLabel(aggregatePoints.Where(static point => point.Name == "tokenobs_tokens_total"), "token_type")
            },
            costSummary = CreateOverviewCostSummary(aggregatePoints, costMix, HasOverviewCostMixUnsupportedFilters(httpContext)),
            cacheSummary = new
            {
                eventCount = SumPointValues(aggregatePoints, "tokenobs_cache_events_total"),
                tokenImpact = SumPointValues(aggregatePoints, "tokenobs_cache_bust_token_impact_total"),
                results = SummarizePointsByLabel(aggregatePoints.Where(static point => point.Name == "tokenobs_cache_events_total"), "cache_result"),
                bustCategories = SummarizePointsByLabel(
                    aggregatePoints.Where(static point => point.Name is "tokenobs_cache_events_total" or "tokenobs_cache_bust_token_impact_total"),
                    "cache_bust_category"),
                evidenceStates = SummarizePointsByLabel(
                    aggregatePoints.Where(static point => point.Name is "tokenobs_cache_events_total" or "tokenobs_cache_bust_token_impact_total"),
                    "cache_evidence_state")
            },
            hotspotSummary = new
            {
                totalHotspots = SumFirstAvailablePointValues(
                    hotspotPoints,
                    "tokenobs_hotspots_open",
                    "tokenobs_hotspots_detected_total"),
                estimatedCostImpact = SumPointValues(hotspotPoints, "tokenobs_hotspot_estimated_cost_impact_usd"),
                byType = SummarizePointsByLabel(hotspotPoints, "hotspot_type"),
                findingStates = SummarizePointsByLabel(hotspotPoints, "finding_state"),
                metricStates = Array.Empty<object>()
            },
            ingestionSummary = new
            {
                requestCount = SumPointValues(aggregatePoints, "tokenobs_ingestion_requests_total"),
                rejectedCount = SumPointValues(
                    ingestionRequestPoints.Where(static point =>
                        point.Labels.TryGetValue("result", out var result) &&
                        StringComparer.Ordinal.Equals(result, "rejected")),
                    "tokenobs_ingestion_requests_total"),
                results = SummarizePointsByLabel(ingestionRequestPoints, "result"),
                rejectionReasons = SummarizePointsByLabel(ingestionRequestPoints, "rejection_reason"),
                signalTypes = SummarizePointsByLabel(ingestionRequestPoints, "signal_type")
            },
            platformHealthSummary = new
            {
                signalCount = SumPointValues(
                    aggregatePoints,
                    "tokenobs_background_jobs_total",
                    "tokenobs_product_api_requests_total",
                    "tokenobs_container_app_replicas_ready",
                    "tokenobs_container_app_replicas_desired"),
                backgroundJobResults = SummarizePointsByLabel(
                    aggregatePoints.Where(static point => point.Name == "tokenobs_background_jobs_total"),
                    "result"),
                productApiStatusClasses = SummarizePointsByLabel(
                    aggregatePoints.Where(static point => point.Name == "tokenobs_product_api_requests_total"),
                    "status_class"),
                containerApps = SummarizePointsByLabel(
                    aggregatePoints.Where(static point => point.Name is "tokenobs_container_app_replicas_ready" or "tokenobs_container_app_replicas_desired"),
                    "container_app")
            },
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
            totalEstimate = costMix.Length
        });
    }

    private static IEnumerable<CostMixBucket> ApplyOverviewCostMixFilters(
        HttpContext httpContext,
        IEnumerable<CostMixBucket> costMix)
    {
        var filtered = costMix;

        if (HasOverviewCostMixUnsupportedFilters(httpContext))
        {
            return [];
        }

        if (TryReadOverviewFilter(httpContext, "modelProvider", out var modelProvider))
        {
            filtered = filtered.Where(bucket => DashboardValueEquals(bucket.ProviderName, modelProvider));
        }

        if (TryReadOverviewFilter(httpContext, "model", out var model))
        {
            filtered = filtered.Where(bucket => DashboardValueEquals(bucket.ModelName, model));
        }

        return filtered;
    }

    private static bool HasOverviewCostMixUnsupportedFilters(HttpContext httpContext)
    {
        foreach (var key in new[]
        {
            "from",
            "to",
            "environment",
            "region",
            "harness",
            "hotspotType",
            "cacheBustCategory",
            "findingState",
            "signalType",
            "result",
            "rejectionReason"
        })
        {
            if (TryReadOverviewFilter(httpContext, key, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static object CreateOverviewCostSummary(
        IEnumerable<AggregateMetricPointRecord> aggregatePoints,
        IReadOnlyCollection<CostMixBucket> costMix,
        bool costMixSuppressed)
    {
        var costPoints = aggregatePoints
            .Where(static point => point.Name == "tokenobs_estimated_cost_usd_total")
            .ToArray();

        if (costPoints.Length > 0 || costMixSuppressed)
        {
            var totalEstimatedCost = SumPointValues(costPoints, "tokenobs_estimated_cost_usd_total");
            return new
            {
                totalEstimatedCost,
                currency = totalEstimatedCost.HasValue ? "USD" : null,
                costStates = SummarizePointsByLabel(costPoints, "cost_status"),
                metricStates = Array.Empty<object>()
            };
        }

        return new
        {
            totalEstimatedCost = SumCostEstimates(costMix),
            currency = costMix.Select(static bucket => bucket.Currency).Distinct(StringComparer.Ordinal).FirstOrDefault(),
            costStates = SummarizeValues(costMix.Select(static bucket => bucket.CostStatus).Select(InMemoryTenantMetadataStore.ToWireCostStatus)),
            metricStates = SummarizeValues(costMix.Select(static bucket => bucket.TokenMetricStatus).Select(ToWireMetricStatus))
        };
    }

    private static IEnumerable<AggregateMetricPointRecord> ApplyOverviewAggregateFilters(
        HttpContext httpContext,
        IEnumerable<AggregateMetricPointRecord> aggregatePoints)
    {
        var filtered = aggregatePoints;

        filtered = ApplyOverviewTimeFilter(httpContext, filtered, static point => point.ExportedAtUtc);
        filtered = ApplyOverviewLabelFilter(httpContext, filtered, "environment", "environment");
        filtered = ApplyOverviewLabelFilter(httpContext, filtered, "region", "region");
        filtered = ApplyOverviewLabelFilter(httpContext, filtered, "harness", "harness");
        filtered = ApplyOverviewLabelFilter(httpContext, filtered, "model", "model");
        filtered = ApplyOverviewLabelFilter(httpContext, filtered, "modelProvider", "model_provider");
        filtered = ApplyOverviewLabelFilter(httpContext, filtered, "hotspotType", "hotspot_type");
        filtered = ApplyOverviewLabelFilter(httpContext, filtered, "cacheBustCategory", "cache_bust_category");
        filtered = ApplyOverviewLabelFilter(httpContext, filtered, "findingState", "finding_state");
        filtered = ApplyOverviewLabelFilter(httpContext, filtered, "signalType", "signal_type");
        filtered = ApplyOverviewLabelFilter(httpContext, filtered, "result", "result");
        filtered = ApplyOverviewLabelFilter(httpContext, filtered, "rejectionReason", "rejection_reason");

        return filtered;
    }

    private static IEnumerable<AgentSessionRecord> ApplyOverviewSessionFilters(
        HttpContext httpContext,
        IEnumerable<AgentSessionRecord> sessions)
    {
        var filtered = sessions;

        filtered = ApplyOverviewTimeFilter(httpContext, filtered, static session => session.StartedAtUtc ?? session.CreatedAtUtc);

        if (TryReadOverviewFilter(httpContext, "harness", out var harness))
        {
            filtered = filtered.Where(session => DashboardValueEquals(session.Harness, harness));
        }

        if (TryReadOverviewFilter(httpContext, "model", out var model))
        {
            filtered = filtered.Where(session => session.ModelNames.Any(modelName => DashboardValueEquals(modelName, model)));
        }

        if (TryReadOverviewFilter(httpContext, "modelProvider", out var modelProvider))
        {
            filtered = filtered.Where(session => session.ModelNames.Any(modelName =>
                DashboardValueEquals(ToDashboardModelProvider(modelName), modelProvider)));
        }

        return filtered;
    }

    private static IEnumerable<TokenHotspotRecord> ApplyOverviewHotspotFilters(
        HttpContext httpContext,
        IEnumerable<TokenHotspotRecord> hotspots)
    {
        var filtered = hotspots;

        filtered = ApplyOverviewTimeFilter(httpContext, filtered, static hotspot => hotspot.CreatedAtUtc);

        if (TryReadOverviewFilter(httpContext, "harness", out var harness))
        {
            filtered = filtered.Where(hotspot => DashboardValueEquals(hotspot.Harness, harness));
        }

        if (TryReadOverviewFilter(httpContext, "model", out var model))
        {
            filtered = filtered.Where(hotspot => hotspot.ModelName is not null && DashboardValueEquals(hotspot.ModelName, model));
        }

        if (TryReadOverviewFilter(httpContext, "modelProvider", out var modelProvider))
        {
            filtered = filtered.Where(hotspot => DashboardValueEquals(ToDashboardModelProvider(hotspot.ModelName), modelProvider));
        }

        if (TryReadOverviewFilter(httpContext, "hotspotType", out var hotspotType))
        {
            filtered = filtered.Where(hotspot => DashboardValueEquals(ToWireHotspotType(hotspot.HotspotType), hotspotType));
        }

        if (TryReadOverviewFilter(httpContext, "findingState", out var findingState))
        {
            filtered = filtered.Where(hotspot => DashboardValueEquals(ToWireHotspotFindingState(hotspot.FindingState), findingState));
        }

        return filtered;
    }

    private static IEnumerable<IngestionRejectionRecord> ApplyOverviewIngestionRejectionFilters(
        HttpContext httpContext,
        IEnumerable<IngestionRejectionRecord> rejections)
    {
        var filtered = rejections;

        filtered = ApplyOverviewTimeFilter(httpContext, filtered, static rejection => rejection.ReceivedAtUtc);

        if (TryReadOverviewFilter(httpContext, "signalType", out var signalType))
        {
            filtered = filtered.Where(rejection => DashboardValueEquals(rejection.SignalType, signalType));
        }

        if (TryReadOverviewFilter(httpContext, "result", out var result))
        {
            filtered = filtered.Where(_ => DashboardValueEquals("rejected", result));
        }

        if (TryReadOverviewFilter(httpContext, "rejectionReason", out var rejectionReason))
        {
            filtered = filtered.Where(rejection => DashboardValueEquals(rejection.ReasonCode, rejectionReason));
        }

        return filtered;
    }

    private static IEnumerable<AggregateMetricPointRecord> ApplyOverviewLabelFilter(
        HttpContext httpContext,
        IEnumerable<AggregateMetricPointRecord> values,
        string queryParameter,
        string labelName)
    {
        if (!TryReadOverviewFilter(httpContext, queryParameter, out var filter))
        {
            return values;
        }

        return values.Where(value =>
            value.Labels.TryGetValue(labelName, out var labelValue) &&
            DashboardValueEquals(labelValue, filter));
    }

    private static IEnumerable<T> ApplyOverviewTimeFilter<T>(
        HttpContext httpContext,
        IEnumerable<T> values,
        Func<T, DateTimeOffset> timestampSelector)
    {
        if (TryReadOverviewTimestampFilter(httpContext, "from", endOfDay: false, out var fromUtc))
        {
            values = values.Where(value => timestampSelector(value).ToUniversalTime() >= fromUtc);
        }

        if (TryReadOverviewTimestampFilter(httpContext, "to", endOfDay: true, out var toUtc))
        {
            values = values.Where(value => timestampSelector(value).ToUniversalTime() <= toUtc);
        }

        return values;
    }

    private static bool TryReadOverviewFilter(HttpContext httpContext, string key, out string value)
    {
        if (!TryReadQuery(httpContext, key, out value) ||
            ContainsAbsoluteUrl(value) ||
            value.Contains('/') ||
            value.Contains('\\') ||
            !IsAllowedOverviewFilterValue(key, value))
        {
            value = string.Empty;
            return false;
        }

        return true;
    }

    private static bool IsAllowedOverviewFilterValue(string key, string value)
    {
        return key switch
        {
            "environment" => value is "dv" or "qa" or "pp" or "pd",
            "harness" => value is "codex" or "copilot" or "claude",
            "modelProvider" => value is "openai" or "anthropic" or "github" or "unknown",
            "hotspotType" => value is
                "prompt_cache_breakage" or
                "large_context" or
                "tool_loop" or
                "model_retry" or
                "repo_context_bloat" or
                "generated_artifact_bloat" or
                "expensive_model_choice" or
                "error_rework" or
                "unknown",
            "cacheBustCategory" => value is
                "prompt_changed" or
                "system_instruction_changed" or
                "tool_context_changed" or
                "repository_context_changed" or
                "model_changed" or
                "unknown",
            "findingState" => value is "confirmed" or "llm_inferred_candidate",
            "signalType" => value is "metrics" or "traces" or "logs",
            "result" => value is "accepted" or "rejected" or "failed" or "succeeded",
            "rejectionReason" => value is
                "none" or
                "invalid_credential" or
                "out_of_scope" or
                "unsupported_schema" or
                "malformed_otlp" or
                "payload_too_large" or
                "rate_limited" or
                "residency_mismatch" or
                "content_classification_failed" or
                "transient_failure",
            _ => true
        };
    }

    private static bool TryReadOverviewTimestampFilter(
        HttpContext httpContext,
        string key,
        bool endOfDay,
        out DateTimeOffset value)
    {
        value = default;

        if (!TryReadOverviewFilter(httpContext, key, out var rawValue))
        {
            return false;
        }

        if (long.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var unixMilliseconds))
        {
            value = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
            return true;
        }

        if (DateOnly.TryParseExact(
                rawValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            value = new DateTimeOffset(
                date.ToDateTime(endOfDay ? TimeOnly.MaxValue : TimeOnly.MinValue),
                TimeSpan.Zero);
            return true;
        }

        return DateTimeOffset.TryParse(
            rawValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);
    }

    private static bool DashboardValueEquals(string? actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        return StringComparer.Ordinal.Equals(actual, expected) ||
            (StringComparer.Ordinal.Equals(actual, "codex-cli") && StringComparer.Ordinal.Equals(expected, "codex")) ||
            (StringComparer.Ordinal.Equals(actual, "codex") && StringComparer.Ordinal.Equals(expected, "codex-cli")) ||
            (StringComparer.Ordinal.Equals(actual, "candidate_llm_inferred") && StringComparer.Ordinal.Equals(expected, "llm_inferred_candidate")) ||
            (StringComparer.Ordinal.Equals(actual, "llm_inferred_candidate") && StringComparer.Ordinal.Equals(expected, "candidate_llm_inferred"));
    }

    private static string ToDashboardModelProvider(string? modelName)
    {
        return !string.IsNullOrWhiteSpace(modelName) &&
            (modelName.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
                modelName.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
                modelName.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
                modelName.StartsWith("o4", StringComparison.OrdinalIgnoreCase))
            ? "openai"
            : "unknown";
    }

    private static double? SumPointValues(
        IEnumerable<AggregateMetricPointRecord> aggregatePoints,
        params string[] names)
    {
        var matched = aggregatePoints
            .Where(point => names.Contains(point.Name, StringComparer.Ordinal))
            .ToArray();

        return matched.Length == 0 ? null : matched.Sum(static point => point.Value);
    }

    private static double? SumFirstAvailablePointValues(
        IEnumerable<AggregateMetricPointRecord> aggregatePoints,
        params string[] names)
    {
        foreach (var name in names)
        {
            var sum = SumPointValues(aggregatePoints, name);
            if (sum.HasValue)
            {
                return sum;
            }
        }

        return null;
    }

    private static decimal? SumCostEstimates(IEnumerable<CostMixBucket> costMix)
    {
        return SumNullableDecimals(costMix.Select(static bucket => bucket.EstimatedCost));
    }

    private static decimal? SumNullableDecimals(IEnumerable<decimal?> values)
    {
        var presentValues = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();

        return presentValues.Length == 0 ? null : presentValues.Sum();
    }

    private static object[] SummarizePointsByLabel(
        IEnumerable<AggregateMetricPointRecord> aggregatePoints,
        string labelName)
    {
        return aggregatePoints
            .Where(point => point.Labels.ContainsKey(labelName))
            .GroupBy(point => point.Labels[labelName], StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new
            {
                label = group.Key,
                value = group.Sum(static point => point.Value)
            })
            .Cast<object>()
            .ToArray();
    }

    private static object[] SummarizePointAndValueLabels(
        IEnumerable<AggregateMetricPointRecord> aggregatePoints,
        string labelName,
        IEnumerable<string> fallbackValues)
    {
        var grouped = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var point in aggregatePoints)
        {
            if (!point.Labels.TryGetValue(labelName, out var labelValue))
            {
                continue;
            }

            grouped[labelValue] = grouped.GetValueOrDefault(labelValue) + point.Value;
        }

        foreach (var fallbackValue in fallbackValues)
        {
            grouped[fallbackValue] = grouped.GetValueOrDefault(fallbackValue) + 1;
        }

        return grouped
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new
            {
                label = pair.Key,
                value = pair.Value
            })
            .Cast<object>()
            .ToArray();
    }

    private static object[] SummarizeValues(IEnumerable<string> values)
    {
        return values
            .GroupBy(static value => value, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new
            {
                label = group.Key,
                value = group.Count()
            })
            .Cast<object>()
            .ToArray();
    }

    private static async Task<IResult> GetPricingBasis(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore)
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
        ITenantMetadataStore tenantMetadataStore,
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
        ITenantMetadataStore tenantMetadataStore,
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
        ITenantMetadataStore tenantMetadataStore,
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

    private static async Task<IResult> SupersedePricingBasis(
        string pricingBasisId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
    {
        return await ReviewPricingBasis(
            pricingBasisId,
            httpContext,
            authorizationContextResolver,
            tenantMetadataStore,
            idempotencyStore,
            approve: null);
    }

    private static async Task<IResult> ReviewPricingBasis(
        string pricingBasisId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore,
        bool? approve)
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
            var record = approve == true
                ? await tenantMetadataStore.ApprovePricingBasisAsync(
                    request,
                    resolution.Context.ProductUser.ProductUserId,
                    resolution.Context.EffectiveRoles.First())
                : approve == false
                    ? await tenantMetadataStore.RejectPricingBasisAsync(
                        request,
                        resolution.Context.ProductUser.ProductUserId,
                        resolution.Context.EffectiveRoles.First())
                    : await tenantMetadataStore.SupersedePricingBasisAsync(
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

    private static async Task<IResult> GetBudgetPolicies(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore)
    {
        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.BudgetManage,
            new ProductScope(ProductScopeKind.Pricing, ScopeId: "pricing"));

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var records = await tenantMetadataStore.ListBudgetPoliciesAsync(
            resolution.Context.CustomerOrganization.CustomerOrganizationId);

        return Results.Ok(new
        {
            items = records.Select(ToBudgetPolicyResponse).ToArray(),
            nextCursor = (string?)null,
            totalEstimate = records.Count
        });
    }

    private static async Task<IResult> GetBudgetEvaluations(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore)
    {
        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.BudgetManage,
            new ProductScope(ProductScopeKind.Pricing, ScopeId: "pricing"));

        if (!resolution.IsAllowed || resolution.Context is null)
        {
            return CreateProblem(httpContext, resolution.Title, resolution.StatusCode, resolution.Code);
        }

        var customerOrganizationId = resolution.Context.CustomerOrganization.CustomerOrganizationId;
        var policies = await tenantMetadataStore.ListBudgetPoliciesAsync(customerOrganizationId);
        var aggregatePoints = await tenantMetadataStore.ListAggregateMetricPointsAsync(customerOrganizationId);
        var pricingBasis = await tenantMetadataStore.ListPricingBasisRecordsAsync(customerOrganizationId);
        var costEstimates = await tenantMetadataStore.ListCostEstimatesAsync(customerOrganizationId);
        var approvedPricingBasisIds = pricingBasis
            .Where(static basis => basis.ReviewState == PricingReviewState.Approved)
            .Select(static basis => basis.PricingBasisId)
            .ToHashSet(StringComparer.Ordinal);

        var evaluations = policies
            .Select(policy => ToBudgetEvaluationResponse(policy, aggregatePoints, costEstimates, approvedPricingBasisIds))
            .ToArray();

        return Results.Ok(new
        {
            items = evaluations,
            nextCursor = (string?)null,
            totalEstimate = evaluations.Length,
            attribution = "aggregate_only"
        });
    }

    private static async Task<IResult> CreateBudgetPolicy(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
    {
        if (!HasIdempotencyKey(httpContext))
        {
            return CreateProblem(httpContext, "Idempotency key required.", StatusCodes.Status400BadRequest, "idempotency_key_required");
        }

        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.BudgetManage,
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

        var body = DeserializePricingMutationBody<BudgetPolicyCreateBody>(rawBody);
        if (body is null)
        {
            return CreateProblem(httpContext, "Budget policy request body required.", StatusCodes.Status400BadRequest, "budget_policy_body_required");
        }

        try
        {
            if (body.ThresholdJson.ValueKind is JsonValueKind.Undefined)
            {
                return CreateProblem(httpContext, "Budget threshold JSON is required.", StatusCodes.Status400BadRequest, "invalid_budget_policy");
            }

            var record = await tenantMetadataStore.CreateBudgetPolicyAsync(
                new CreateBudgetPolicyRequest(
                    resolution.Context.CustomerOrganization.CustomerOrganizationId,
                    InMemoryTenantMetadataStore.ParseBudgetScopeKind(body.ScopeKind),
                    body.ScopeId,
                    InMemoryTenantMetadataStore.ParseBudgetMetricKind(body.MetricKind),
                    body.ThresholdJson.GetRawText(),
                    InMemoryTenantMetadataStore.ParseBudgetPolicyStatus(body.Status),
                    $"audit-budget-policy-{Guid.NewGuid():N}",
                    resolution.Context.CorrelationId),
                resolution.Context.ProductUser.ProductUserId,
                resolution.Context.EffectiveRoles.First());

            var location = $"/api/v1/budgets/policies/{record.BudgetPolicyId}";
            return await StorePricingMutationIdempotencyResultAsync(
                idempotencyStore,
                resolution.Context,
                idempotency,
                operationId: record.BudgetPolicyId,
                StatusCodes.Status201Created,
                location,
                ToBudgetPolicyResponse(record));
        }
        catch (ArgumentException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status400BadRequest, "invalid_budget_policy");
        }
        catch (InvalidOperationException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status409Conflict, "budget_policy_conflict");
        }
    }

    private static async Task<IResult> UpdateBudgetPolicy(
        string budgetPolicyId,
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore,
        IProductApiIdempotencyStore idempotencyStore)
    {
        if (!HasIdempotencyKey(httpContext))
        {
            return CreateProblem(httpContext, "Idempotency key required.", StatusCodes.Status400BadRequest, "idempotency_key_required");
        }

        var resolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.BudgetManage,
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

        var body = DeserializePricingMutationBody<BudgetPolicyUpdateBody>(rawBody) ?? new BudgetPolicyUpdateBody();

        try
        {
            var thresholdJson = body.ThresholdJson?.ValueKind is null or JsonValueKind.Undefined
                ? null
                : body.ThresholdJson.Value.GetRawText();
            var record = await tenantMetadataStore.UpdateBudgetPolicyAsync(
                new UpdateBudgetPolicyRequest(
                    resolution.Context.CustomerOrganization.CustomerOrganizationId,
                    budgetPolicyId,
                    body.ScopeKind is null ? null : InMemoryTenantMetadataStore.ParseBudgetScopeKind(body.ScopeKind),
                    body.ScopeId,
                    body.MetricKind is null ? null : InMemoryTenantMetadataStore.ParseBudgetMetricKind(body.MetricKind),
                    thresholdJson,
                    body.Status is null ? null : InMemoryTenantMetadataStore.ParseBudgetPolicyStatus(body.Status),
                    $"audit-budget-policy-{Guid.NewGuid():N}",
                    resolution.Context.CorrelationId),
                resolution.Context.ProductUser.ProductUserId,
                resolution.Context.EffectiveRoles.First());

            return await StorePricingMutationIdempotencyResultAsync(
                idempotencyStore,
                resolution.Context,
                idempotency,
                operationId: record.BudgetPolicyId,
                StatusCodes.Status200OK,
                Location: null,
                ToBudgetPolicyResponse(record));
        }
        catch (ArgumentException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status400BadRequest, "invalid_budget_policy");
        }
        catch (InvalidOperationException ex)
        {
            return CreateProblem(httpContext, ex.Message, StatusCodes.Status409Conflict, "budget_policy_conflict");
        }
    }

    private static async Task<IResult> GetOverviewTokenTimeline(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ITenantMetadataStore tenantMetadataStore)
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
        if (tenantMetadataStore is not InMemoryTenantMetadataStore inMemoryTenantMetadataStore)
        {
            return CreateProblem(
                httpContext,
                "Token timeline is not available for the configured metadata store.",
                StatusCodes.Status501NotImplemented,
                "metadata_store_not_supported");
        }

        var query = new AggregateTokenTimelineQuery(from, to, movingAverageWindowDays);
        var exporter = new AggregateMetricsExporter(
            inMemoryTenantMetadataStore,
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

            if (StringComparer.Ordinal.Equals(queryParameter.Key, "route"))
            {
                continue;
            }

            if (queryParameter.Value.Any(value =>
                    ContainsAbsoluteUrl(value) ||
                    value?.Contains('/') == true ||
                    value?.Contains('\\') == true ||
                    !IsAllowedOverviewFilterValue(queryParameter.Key, value ?? string.Empty)))
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

    private static async Task<TokenObservabilityAuthorizationResolution> ResolveContentReviewAuthorizationAsync(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver,
        ProductAuthorizationAction action)
    {
        var queueResolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            action,
            new ProductScope(ProductScopeKind.ContentReviewQueue, ScopeId: "content-review"));

        if (queueResolution.IsAllowed)
        {
            return queueResolution;
        }

        var organizationResolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            action,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        return organizationResolution.IsAllowed ? organizationResolution : queueResolution;
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

    private static async Task<TokenObservabilityAuthorizationResolution> ResolveSessionSummaryAuthorizationAsync(
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

        var ownResolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.SessionReadOwn,
            new ProductScope(ProductScopeKind.Self, ScopeId: null));
        if (ownResolution.IsAllowed)
        {
            return ownResolution;
        }

        var investigateResolution = await ResolveSessionInvestigateAuthorizationAsync(
            httpContext,
            authorizationContextResolver);

        return investigateResolution.IsAllowed ? investigateResolution : scopedResolution;
    }

    private static async Task<TokenObservabilityAuthorizationResolution> ResolveSessionInvestigateAuthorizationAsync(
        HttpContext httpContext,
        TokenObservabilityAuthorizationContextResolver authorizationContextResolver)
    {
        var organizationResolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.SessionInvestigate,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));
        if (organizationResolution.IsAllowed)
        {
            return organizationResolution;
        }

        var reviewQueueResolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.SessionInvestigate,
            new ProductScope(ProductScopeKind.ContentReviewQueue, ScopeId: null));
        if (reviewQueueResolution.IsAllowed)
        {
            return reviewQueueResolution;
        }

        var selfResolution = await authorizationContextResolver.ResolveAsync(
            httpContext,
            ProductAuthorizationAction.SessionInvestigate,
            new ProductScope(ProductScopeKind.Self, ScopeId: null));

        return selfResolution.IsAllowed ? selfResolution : organizationResolution;
    }

    private static async Task<AgentSessionRecord?> FindSessionAsync(
        ITenantMetadataStore tenantMetadataStore,
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
        ITenantMetadataStore tenantMetadataStore,
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

    private static bool CanAccessPrivilegedSession(
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

    private static object ToBudgetPolicyResponse(BudgetPolicyRecord record)
    {
        return new
        {
            budgetPolicyId = record.BudgetPolicyId,
            customerOrganizationId = record.CustomerOrganizationId.ToString(),
            scopeKind = InMemoryTenantMetadataStore.ToWireBudgetScopeKind(record.ScopeKind),
            scopeId = record.ScopeId,
            metricKind = InMemoryTenantMetadataStore.ToWireBudgetMetricKind(record.MetricKind),
            threshold = JsonSerializer.Deserialize<JsonElement>(record.ThresholdJson, JsonOptions),
            status = InMemoryTenantMetadataStore.ToWireBudgetPolicyStatus(record.Status),
            auditEventId = record.AuditEventId,
            createdAtUtc = record.CreatedAtUtc,
            updatedAtUtc = record.UpdatedAtUtc
        };
    }

    private static object ToBudgetEvaluationResponse(
        BudgetPolicyRecord policy,
        IReadOnlyList<AggregateMetricPointRecord> aggregatePoints,
        IReadOnlyList<CostEstimateRecord> costEstimates,
        IReadOnlySet<string> approvedPricingBasisIds)
    {
        var threshold = ReadBudgetThreshold(policy);
        var actualValue = policy.Status == BudgetPolicyStatus.Disabled
            ? null
            : EvaluateBudgetMetric(policy, aggregatePoints, costEstimates, approvedPricingBasisIds);
        var evaluationStatus = policy.Status == BudgetPolicyStatus.Disabled
            ? "disabled"
            : actualValue is null
                ? "unavailable"
                : actualValue > threshold.Value
                    ? "threshold_exceeded"
                    : "within_threshold";

        return new
        {
            budgetPolicyId = policy.BudgetPolicyId,
            scopeKind = InMemoryTenantMetadataStore.ToWireBudgetScopeKind(policy.ScopeKind),
            scopeId = policy.ScopeId,
            metricKind = InMemoryTenantMetadataStore.ToWireBudgetMetricKind(policy.MetricKind),
            period = threshold.Period,
            thresholdValue = threshold.Value,
            actualValue,
            unit = threshold.Unit,
            status = evaluationStatus,
            attribution = "aggregate_only",
            pricingBasisState = policy.MetricKind == BudgetMetricKind.EstimatedCost ? "approved_only" : "not_applicable"
        };
    }

    private static decimal? EvaluateBudgetMetric(
        BudgetPolicyRecord policy,
        IReadOnlyList<AggregateMetricPointRecord> aggregatePoints,
        IReadOnlyList<CostEstimateRecord> costEstimates,
        IReadOnlySet<string> approvedPricingBasisIds)
    {
        return policy.MetricKind switch
        {
            BudgetMetricKind.Tokens => SumAggregateMetricValue(policy, aggregatePoints, "tokenobs_tokens_total"),
            BudgetMetricKind.EstimatedCost => SumApprovedEstimatedCost(policy, costEstimates, approvedPricingBasisIds),
            BudgetMetricKind.CacheMissRate => CalculateCacheMissRate(policy, aggregatePoints),
            BudgetMetricKind.ErrorRework => CalculateErrorReworkRate(policy, aggregatePoints),
            _ => null
        };
    }

    private static decimal? SumAggregateMetricValue(
        BudgetPolicyRecord policy,
        IReadOnlyList<AggregateMetricPointRecord> aggregatePoints,
        string metricName)
    {
        var points = aggregatePoints
            .Where(point => point.Name == metricName && AggregatePointMatchesBudgetScope(policy, point))
            .ToArray();

        return points.Length == 0 ? null : (decimal)points.Sum(static point => point.Value);
    }

    private static decimal? SumApprovedEstimatedCost(
        BudgetPolicyRecord policy,
        IReadOnlyList<CostEstimateRecord> costEstimates,
        IReadOnlySet<string> approvedPricingBasisIds)
    {
        var estimates = costEstimates
            .Where(estimate => estimate.EstimatedCost is not null &&
                estimate.Currency == "USD" &&
                estimate.PricingBasisId is not null &&
                approvedPricingBasisIds.Contains(estimate.PricingBasisId) &&
                CostEstimateMatchesBudgetScope(policy, estimate))
            .ToArray();

        return estimates.Length == 0 ? null : estimates.Sum(static estimate => estimate.EstimatedCost!.Value);
    }

    private static decimal? CalculateCacheMissRate(
        BudgetPolicyRecord policy,
        IReadOnlyList<AggregateMetricPointRecord> aggregatePoints)
    {
        var tokenPoints = aggregatePoints
            .Where(point => point.Name == "tokenobs_tokens_total" && AggregatePointMatchesBudgetScope(policy, point))
            .ToArray();
        var total = tokenPoints.Sum(static point => point.Value);
        if (total <= 0)
        {
            return null;
        }

        var cached = tokenPoints
            .Where(static point => point.Labels.TryGetValue("token_type", out var tokenType) && tokenType == "cached_input")
            .Sum(static point => point.Value);

        return (decimal)Math.Clamp(1 - cached / total, 0, 1);
    }

    private static decimal? CalculateErrorReworkRate(
        BudgetPolicyRecord policy,
        IReadOnlyList<AggregateMetricPointRecord> aggregatePoints)
    {
        var turnPoints = aggregatePoints
            .Where(point => point.Name == "tokenobs_turns_total" && AggregatePointMatchesBudgetScope(policy, point))
            .ToArray();
        var total = turnPoints.Sum(static point => point.Value);
        if (total <= 0)
        {
            return null;
        }

        var errorRework = turnPoints
            .Where(static point => point.Labels.TryGetValue("result", out var result) &&
                result is "error" or "rework")
            .Sum(static point => point.Value);

        return (decimal)Math.Clamp(errorRework / total, 0, 1);
    }

    private static bool AggregatePointMatchesBudgetScope(BudgetPolicyRecord policy, AggregateMetricPointRecord point)
    {
        return policy.ScopeKind switch
        {
            BudgetPolicyScopeKind.CustomerOrganization => true,
            BudgetPolicyScopeKind.Harness => point.Labels.TryGetValue("harness", out var harness) &&
                StringComparer.Ordinal.Equals(harness, policy.ScopeId),
            BudgetPolicyScopeKind.Model => point.Labels.TryGetValue("model", out var model) &&
                StringComparer.Ordinal.Equals(model, policy.ScopeId),
            _ => false
        };
    }

    private static bool CostEstimateMatchesBudgetScope(BudgetPolicyRecord policy, CostEstimateRecord estimate)
    {
        return policy.ScopeKind switch
        {
            BudgetPolicyScopeKind.CustomerOrganization => true,
            BudgetPolicyScopeKind.Model => StringComparer.Ordinal.Equals(estimate.ModelName, policy.ScopeId),
            _ => false
        };
    }

    private static BudgetThreshold ReadBudgetThreshold(BudgetPolicyRecord policy)
    {
        using var document = JsonDocument.Parse(policy.ThresholdJson);
        var root = document.RootElement;
        var period = root.GetProperty("period").GetString()!;

        return policy.MetricKind switch
        {
            BudgetMetricKind.EstimatedCost => new BudgetThreshold(
                root.GetProperty("amount").GetDecimal(),
                "USD",
                period),
            BudgetMetricKind.Tokens => new BudgetThreshold(
                root.GetProperty("amount").GetDecimal(),
                "tokens",
                period),
            BudgetMetricKind.CacheMissRate or BudgetMetricKind.ErrorRework => new BudgetThreshold(
                root.GetProperty("rate").GetDecimal(),
                "ratio",
                period),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy.MetricKind, null)
        };
    }

    private static object ToContentReferenceResponse(ContentReferenceRecord record, bool includeApprovedExcerpt)
    {
        return new
        {
            contentReferenceId = record.ContentReferenceId.ToString(),
            customerOrganizationId = record.CustomerOrganizationId.ToString(),
            agentSessionId = record.AgentSessionId,
            telemetryEnvelopeId = record.TelemetryEnvelopeId,
            contentClass = InMemoryTenantMetadataStore.ToWireContentClass(record.ContentClass),
            captureState = InMemoryTenantMetadataStore.ToWireContentReferenceCaptureState(record.CaptureState),
            redactionStatus = InMemoryTenantMetadataStore.ToWireContentReferenceRedactionStatus(record.RedactionStatus),
            contentHash = record.ContentHash,
            blob = record.BlobPointer is null
                ? null
                : new
                {
                    container = record.BlobPointer.Container,
                    blobName = record.BlobPointer.BlobName,
                    blobUri = record.BlobPointer.BlobUri,
                    blobVersion = record.BlobPointer.BlobVersion
                },
            policyVersionId = record.PolicyVersionId,
            redactionPipelineVersion = record.RedactionPipelineVersion,
            productRuleVersion = record.ProductRuleVersion,
            retentionClass = InMemoryTenantMetadataStore.ToWireContentRetentionClass(record.RetentionClass),
            expiresAtUtc = record.ExpiresAtUtc,
            recommendationEligible = record.RecommendationEligible,
            auditEventId = record.AuditEventId,
            approvedExcerpt = includeApprovedExcerpt &&
                record.CaptureState == ContentReferenceCaptureState.ApprovedExcerpt
                    ? record.ApprovedExcerpt
                    : null,
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

    private static object ToSessionMetadataResponse(AgentSessionRecord session)
    {
        return new
        {
            agentSessionId = session.AgentSessionId,
            customerOrganizationId = session.CustomerOrganizationId.ToString(),
            productUserId = session.ProductUserId.ToString(),
            providerSessionIdHash = session.ProviderSessionIdHash,
            startedAtUtc = session.StartedAtUtc,
            endedAtUtc = session.EndedAtUtc,
            sessionStatus = session.SessionStatus,
            environment = session.Environment,
            sandboxSetting = session.SandboxSetting,
            approvalSetting = session.ApprovalSetting,
            createdAtUtc = session.CreatedAtUtc,
            updatedAtUtc = session.UpdatedAtUtc
        };
    }

    private static object ToTokenObservationResponse(TokenObservationRecord observation)
    {
        return new
        {
            tokenObservationId = observation.TokenObservationId.ToString(),
            modelInvocationId = observation.ModelInvocationId,
            metricName = ToWireMetricName(observation.MetricName),
            value = observation.Value,
            metricStatus = ToWireMetricStatus(observation.MetricStatus),
            metricConfidence = ToWireMetricConfidence(observation.MetricConfidence),
            sourceKind = ToWireSourceKind(observation.SourceKind),
            sourceTelemetryEnvelopeId = observation.SourceTelemetryEnvelopeId,
            createdAtUtc = observation.CreatedAtUtc
        };
    }

    private static object ToTokenHotspotResponse(TokenHotspotRecord hotspot)
    {
        return new
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
            createdAtUtc = hotspot.CreatedAtUtc,
            routeTarget = $"#hotspot-{hotspot.TokenHotspotId}"
        };
    }

    private static IReadOnlyList<SessionTokenSplitItem> CreateTokenSplit(
        IReadOnlyList<TokenObservationRecord> observations)
    {
        return observations
            .GroupBy(static observation => observation.MetricName)
            .Select(static group =>
            {
                var values = group.Select(static observation => observation.Value).ToArray();
                var knownValues = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
                long? totalValue = knownValues.Length == 0 ? null : knownValues.Sum();
                var metricStatus = group.Any(static observation => observation.MetricStatus == TokenMetricStatus.Mixed)
                    ? TokenMetricStatus.Mixed
                    : group.Select(static observation => observation.MetricStatus).Distinct().Count() == 1
                        ? group.First().MetricStatus
                        : TokenMetricStatus.Mixed;
                var metricConfidence = group.Any(static observation => observation.MetricConfidence == TokenMetricConfidence.Unavailable)
                    ? TokenMetricConfidence.Unavailable
                    : group.Select(static observation => observation.MetricConfidence).Distinct().Count() == 1
                        ? group.First().MetricConfidence
                        : TokenMetricConfidence.Estimated;

                return new SessionTokenSplitItem(
                    ToWireMetricName(group.Key),
                    totalValue,
                    ToWireMetricStatus(metricStatus),
                    ToWireMetricConfidence(metricConfidence));
            })
            .OrderBy(static item => item.MetricName, StringComparer.Ordinal)
            .ToArray();
    }

    private static object CreateCostSummary(IReadOnlyList<CostEstimateRecord> costEstimates)
    {
        var knownCosts = costEstimates
            .Where(static estimate => estimate.EstimatedCost.HasValue)
            .Select(static estimate => estimate.EstimatedCost!.Value)
            .ToArray();
        var statuses = costEstimates.Select(static estimate => estimate.CostStatus).Distinct().ToArray();
        var currencies = costEstimates.Select(static estimate => estimate.Currency).Distinct(StringComparer.Ordinal).ToArray();

        return new
        {
            estimatedTotal = knownCosts.Length == 0 ? (decimal?)null : knownCosts.Sum(),
            currency = currencies.Length == 1 ? currencies[0] : null,
            currencyState = currencies.Length switch
            {
                0 => "unavailable",
                1 => "single",
                _ => "mixed"
            },
            costStatus = statuses.Length switch
            {
                0 => "unavailable",
                1 => InMemoryTenantMetadataStore.ToWireCostStatus(statuses[0]),
                _ => "mixed"
            },
            estimates = costEstimates.Select(static estimate => new
            {
                costEstimateId = estimate.CostEstimateId,
                modelInvocationId = estimate.ModelInvocationId,
                pricingBasisId = estimate.PricingBasisId,
                pricingVersion = estimate.PricingVersion,
                currency = estimate.Currency,
                estimatedCost = estimate.EstimatedCost,
                costStatus = InMemoryTenantMetadataStore.ToWireCostStatus(estimate.CostStatus),
                sourceKind = ToWireCostEstimateSourceKind(estimate.SourceKind),
                metricStatus = ToWireMetricStatus(estimate.TokenMetricStatus),
                metricConfidence = ToWireMetricConfidence(estimate.TokenMetricConfidence),
                providerName = estimate.ProviderName,
                modelName = estimate.ModelName,
                billingRoute = estimate.BillingRoute,
                tokenType = InMemoryTenantMetadataStore.ToWirePricingTokenType(estimate.TokenType),
                createdAtUtc = estimate.CreatedAtUtc
            }).ToArray()
        };
    }

    private static IReadOnlyList<object> CreateCacheDiagnostics(
        IReadOnlyList<TokenObservationRecord> observations,
        IReadOnlyList<TokenHotspotRecord> hotspots)
    {
        var diagnostics = new List<object>();
        var cachedInput = observations
            .Where(static observation => observation.MetricName == TokenMetricName.CachedInputTokens)
            .Select(ToTokenObservationResponse)
            .ToArray();

        if (cachedInput.Length > 0)
        {
            diagnostics.Add(new
            {
                diagnosticType = "cached_input_tokens",
                evidenceState = "observed_metric",
                tokenObservations = cachedInput
            });
        }

        diagnostics.AddRange(hotspots
            .Where(static hotspot => hotspot.PromptCacheEvidenceState != PromptCacheEvidenceState.NotApplicable)
            .Select(static hotspot => new
            {
                diagnosticType = "prompt_cache_evidence",
                evidenceState = ToWirePromptCacheEvidenceState(hotspot.PromptCacheEvidenceState),
                tokenHotspotId = hotspot.TokenHotspotId.ToString(),
                hotspotType = ToWireHotspotType(hotspot.HotspotType),
                findingState = ToWireHotspotFindingState(hotspot.FindingState),
                evidenceSummary = hotspot.EvidenceSummary,
                routeTarget = $"#hotspot-{hotspot.TokenHotspotId}"
            }));

        return diagnostics;
    }

    private static object ToSessionContentEvidenceResponse(ContentReferenceRecord reference)
    {
        return new
        {
            contentReferenceId = reference.ContentReferenceId.ToString(),
            agentSessionId = reference.AgentSessionId,
            telemetryEnvelopeId = reference.TelemetryEnvelopeId,
            contentClass = InMemoryTenantMetadataStore.ToWireContentClass(reference.ContentClass),
            captureState = InMemoryTenantMetadataStore.ToWireContentReferenceCaptureState(reference.CaptureState),
            redactionStatus = InMemoryTenantMetadataStore.ToWireContentReferenceRedactionStatus(reference.RedactionStatus),
            evidenceState = ToSessionContentEvidenceState(reference),
            policyVersionId = reference.PolicyVersionId,
            redactionPipelineVersion = reference.RedactionPipelineVersion,
            productRuleVersion = reference.ProductRuleVersion,
            retentionClass = InMemoryTenantMetadataStore.ToWireContentRetentionClass(reference.RetentionClass),
            expiresAtUtc = reference.ExpiresAtUtc,
            recommendationEligible = reference.RecommendationEligible,
            auditEventId = reference.AuditEventId,
            approvedExcerpt = (string?)null,
            createdAtUtc = reference.CreatedAtUtc,
            updatedAtUtc = reference.UpdatedAtUtc,
            routeTarget = $"#content-{reference.ContentReferenceId}"
        };
    }

    private static string ToSessionContentEvidenceState(ContentReferenceRecord reference)
    {
        return reference.CaptureState switch
        {
            ContentReferenceCaptureState.NotAllowed => "policy_hidden",
            ContentReferenceCaptureState.MetadataOnly => "metadata_only",
            ContentReferenceCaptureState.Captured => "metadata_only",
            ContentReferenceCaptureState.RedactionFailed => "redaction_failed",
            ContentReferenceCaptureState.ReviewRequired => "review_required",
            ContentReferenceCaptureState.Discarded => "metadata_only",
            ContentReferenceCaptureState.ApprovedExcerpt => "approved_excerpt",
            _ => "unavailable"
        };
    }

    private static SessionTimelineItem[] CreateSessionTimeline(
        AgentSessionRecord session,
        IReadOnlyList<TokenObservationRecord> observations,
        IReadOnlyList<TokenHotspotRecord> hotspots,
        IReadOnlyList<ContentReferenceRecord> contentReferences,
        IReadOnlyList<RecommendationRecord> recommendations)
    {
        var items = new List<SessionTimelineItem>
        {
            new(
                $"session-started-{session.AgentSessionId}",
                session.StartedAtUtc ?? session.CreatedAtUtc,
                "session",
                "Session started",
                session.SessionStatus,
                session.AgentSessionId,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["harness"] = session.Harness,
                    ["harnessSetupProfileId"] = session.HarnessSetupProfileId,
                    ["providerSessionIdHash"] = session.ProviderSessionIdHash
                })
        };

        items.AddRange(observations.Select(observation => new SessionTimelineItem(
            $"token-observation-{observation.TokenObservationId}",
            observation.CreatedAtUtc,
            "token_observation",
            "Token observation recorded",
            ToWireMetricStatus(observation.MetricStatus),
            observation.TokenObservationId.ToString(),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["metricName"] = ToWireMetricName(observation.MetricName),
                ["value"] = observation.Value,
                ["metricConfidence"] = ToWireMetricConfidence(observation.MetricConfidence),
                ["sourceKind"] = ToWireSourceKind(observation.SourceKind),
                ["modelInvocationId"] = observation.ModelInvocationId
            })));

        items.AddRange(hotspots.Select(hotspot => new SessionTimelineItem(
            $"token-hotspot-{hotspot.TokenHotspotId}",
            hotspot.CreatedAtUtc,
            "token_hotspot",
            "Token hotspot recorded",
            ToWireHotspotFindingState(hotspot.FindingState),
            hotspot.TokenHotspotId.ToString(),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["hotspotType"] = ToWireHotspotType(hotspot.HotspotType),
                ["attributionType"] = ToWireHotspotAttributionType(hotspot.AttributionType),
                ["confidence"] = ToWireHotspotConfidence(hotspot.Confidence),
                ["promptCacheEvidenceState"] = ToWirePromptCacheEvidenceState(hotspot.PromptCacheEvidenceState),
                ["routeTarget"] = $"#hotspot-{hotspot.TokenHotspotId}"
            })));

        items.AddRange(contentReferences.Select(reference => new SessionTimelineItem(
            $"content-evidence-{reference.ContentReferenceId}",
            reference.CreatedAtUtc,
            "content_evidence",
            "Content evidence state recorded",
            ToSessionContentEvidenceState(reference),
            reference.ContentReferenceId.ToString(),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["captureState"] = InMemoryTenantMetadataStore.ToWireContentReferenceCaptureState(reference.CaptureState),
                ["redactionStatus"] = InMemoryTenantMetadataStore.ToWireContentReferenceRedactionStatus(reference.RedactionStatus),
                ["contentClass"] = InMemoryTenantMetadataStore.ToWireContentClass(reference.ContentClass),
                ["routeTarget"] = $"#content-{reference.ContentReferenceId}"
            })));

        items.AddRange(recommendations.Select(recommendation => new SessionTimelineItem(
            $"recommendation-{recommendation.RecommendationId}",
            recommendation.CreatedAtUtc,
            "recommendation",
            "Recommendation recorded",
            InMemoryTenantMetadataStore.ToWireRecommendationState(recommendation.State),
            recommendation.RecommendationId.ToString(),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = InMemoryTenantMetadataStore.ToWireRecommendationKind(recommendation.Kind),
                ["authorityState"] = InMemoryTenantMetadataStore.ToWireRecommendationAuthorityState(recommendation.AuthorityState),
                ["confidence"] = InMemoryTenantMetadataStore.ToWireRecommendationConfidence(recommendation.Confidence),
                ["routeTarget"] = $"#recommendation-{recommendation.RecommendationId}"
            })));

        if (session.EndedAtUtc.HasValue)
        {
            items.Add(new SessionTimelineItem(
                $"session-ended-{session.AgentSessionId}",
                session.EndedAtUtc,
                "session",
                "Session ended",
                session.SessionStatus,
                session.AgentSessionId,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["harness"] = session.Harness
                }));
        }

        return items
            .OrderBy(static item => item.EventTimestampUtc)
            .ThenBy(static item => item.TimelineItemId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ToWireCostEstimateSourceKind(CostEstimateSourceKind sourceKind)
    {
        return sourceKind switch
        {
            CostEstimateSourceKind.DerivedFromObservedTokens => "derived_from_observed_tokens",
            CostEstimateSourceKind.DerivedFromEstimatedTokens => "derived_from_estimated_tokens",
            CostEstimateSourceKind.ManualOverride => "manual_override",
            CostEstimateSourceKind.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, null)
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

    private static ContentReferenceId? ParseContentReferenceId(string? contentReferenceId)
    {
        return Guid.TryParse(contentReferenceId, out var parsed) && parsed != Guid.Empty
            ? new ContentReferenceId(parsed)
            : null;
    }

    private static ContentReferenceCaptureState? ParseOptionalContentReviewState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        return state.Trim().ToLowerInvariant() switch
        {
            "not_allowed" => ContentReferenceCaptureState.NotAllowed,
            "metadata_only" => ContentReferenceCaptureState.MetadataOnly,
            "captured" => ContentReferenceCaptureState.Captured,
            "redaction_failed" => ContentReferenceCaptureState.RedactionFailed,
            "review_required" => ContentReferenceCaptureState.ReviewRequired,
            "discarded" => ContentReferenceCaptureState.Discarded,
            "approved_excerpt" => ContentReferenceCaptureState.ApprovedExcerpt,
            _ => throw new ArgumentException("Content review state is not supported.", nameof(state))
        };
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

    private sealed record BudgetPolicyCreateBody(
        string ScopeKind,
        string? ScopeId,
        string MetricKind,
        JsonElement ThresholdJson,
        string Status);

    private sealed record BudgetPolicyUpdateBody(
        string? ScopeKind = null,
        string? ScopeId = null,
        string? MetricKind = null,
        JsonElement? ThresholdJson = null,
        string? Status = null);

    private sealed record BudgetThreshold(decimal Value, string Unit, string Period);

    private sealed record RecommendationRegenerationBody(
        string? AgentSessionId,
        string? TokenHotspotId,
        string? Reason);

    private sealed record SessionTokenSplitItem(
        string MetricName,
        long? Value,
        string MetricStatus,
        string MetricConfidence);

    private sealed record SessionTimelineItem(
        string TimelineItemId,
        DateTimeOffset? EventTimestampUtc,
        string ItemType,
        string Title,
        string State,
        string? RelatedResourceId,
        IReadOnlyDictionary<string, object?> Metadata);

    private sealed record ContentReviewDecisionBody(string? DecisionReason);

    private sealed record ApproveExcerptBody(
        string? DecisionReason,
        string? ApprovedExcerpt);

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
