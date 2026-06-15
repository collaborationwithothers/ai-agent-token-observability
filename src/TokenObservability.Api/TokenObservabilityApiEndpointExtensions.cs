using Microsoft.Extensions.Options;

namespace TokenObservability.Api;

internal static class TokenObservabilityApiEndpointExtensions
{
    public static void AddTokenObservabilityApiServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<TokenObservabilityApiReadinessOptions>(
            builder.Configuration.GetSection("ProductApi:Readiness"));
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

    private static IResult GetProtectedReadiness(HttpContext httpContext)
    {
        if (!HasBearerAuthorization(httpContext))
        {
            return CreateProblem(
                httpContext,
                "Authentication required",
                StatusCodes.Status401Unauthorized,
                "authentication_required");
        }

        return CreateProblem(
            httpContext,
            "Authentication required",
            StatusCodes.Status401Unauthorized,
            "authentication_required");
    }

    private static IResult GetCurrentUser(HttpContext httpContext)
    {
        if (!HasBearerAuthorization(httpContext))
        {
            return CreateProblem(
                httpContext,
                "Authentication required",
                StatusCodes.Status401Unauthorized,
                "authentication_required");
        }

        if (!httpContext.Request.Headers.ContainsKey("X-Customer-Organization-Id"))
        {
            return CreateProblem(
                httpContext,
                "Customer organization context is required",
                StatusCodes.Status403Forbidden,
                "tenant_context_required");
        }

        return CreateProblem(
            httpContext,
            "Authentication required",
            StatusCodes.Status401Unauthorized,
            "authentication_required");
    }

    private static bool HasBearerAuthorization(HttpContext httpContext)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();

        return authorization.StartsWith("Bearer ", StringComparison.Ordinal);
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

    private sealed record ReadinessDependency(string Name, string Status);
}
