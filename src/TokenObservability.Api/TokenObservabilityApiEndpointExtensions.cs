using Microsoft.Extensions.Options;
using TokenObservability.Domain.Authorization;
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

    private static IResult CreateProblem(HttpContext httpContext, string title, int statusCode, string code)
    {
        return Results.Problem(
            type: $"https://docs.product.local/problems/{code.Replace('_', '-')}",
            title: title,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = httpContext.TraceIdentifier
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

    private static bool TryReadQuery(HttpContext httpContext, string key, out string value)
    {
        value = httpContext.Request.Query[key].ToString().Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ToContractAction(ProductAuthorizationAction action)
    {
        return action.ToString();
    }

    private sealed record ReadinessDependency(string Name, string Status);
}
