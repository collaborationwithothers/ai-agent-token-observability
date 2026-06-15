using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Api;

internal static class TokenObservabilityAuthorizationContextServiceCollectionExtensions
{
    public static void AddTokenObservabilityAuthorizationContext(this IServiceCollection services)
    {
        services.TryAddSingleton<ITenantMetadataClock, SystemTenantMetadataClock>();
        services.TryAddSingleton<InMemoryTenantMetadataStore>();
        services.TryAddScoped<TokenObservabilityAuthorizationContextAccessor>();
        services.TryAddScoped<TokenObservabilityAuthorizationContextResolver>();
    }
}

internal sealed class TokenObservabilityAuthorizationContextAccessor
{
    public TokenObservabilityAuthorizationContext? Current { get; set; }
}

internal sealed class TokenObservabilityAuthorizationContextResolver(
    InMemoryTenantMetadataStore tenantMetadataStore,
    TokenObservabilityAuthorizationContextAccessor authorizationContextAccessor)
{
    public async Task<TokenObservabilityAuthorizationResolution> ResolveAsync(
        HttpContext httpContext,
        ProductAuthorizationAction action,
        ProductScope requestedScope)
    {
        var claims = TryReadAuthenticatedClaims(httpContext);
        if (claims is null)
        {
            return TokenObservabilityAuthorizationResolution.Denied(
                StatusCodes.Status401Unauthorized,
                "authentication_required",
                "Authentication required");
        }

        var organizationResolution = await ResolveCustomerOrganizationAsync(httpContext);
        if (!organizationResolution.IsAllowed || organizationResolution.CustomerOrganization is null)
        {
            return TokenObservabilityAuthorizationResolution.Denied(
                organizationResolution.StatusCode,
                organizationResolution.Code,
                organizationResolution.Title);
        }

        var organization = organizationResolution.CustomerOrganization;
        var identityTenant = await tenantMetadataStore.FindIdentityTenantForClaimsAsync(
            organization.CustomerOrganizationId,
            claims);

        if (identityTenant is null)
        {
            await tenantMetadataStore.RecordAuthorizationDenialAsync(
                organization.CustomerOrganizationId,
                action,
                requestedScope,
                ProductAuthorizationDenialReason.InvalidTenant,
                GetCorrelationId(httpContext));

            return TokenObservabilityAuthorizationResolution.Denied(
                StatusCodes.Status403Forbidden,
                "authorization_denied",
                "Authorization denied");
        }

        var decision = await tenantMetadataStore.AuthorizeProductActionAsync(
            organization.CustomerOrganizationId,
            identityTenant.IdentityTenantId,
            claims,
            action,
            requestedScope,
            GetCorrelationId(httpContext));

        if (!decision.IsAllowed || decision.ProductUser is null)
        {
            return TokenObservabilityAuthorizationResolution.Denied(
                StatusCodes.Status403Forbidden,
                "authorization_denied",
                "Authorization denied");
        }

        var context = new TokenObservabilityAuthorizationContext(
            organization,
            identityTenant,
            decision.ProductUser,
            decision.EffectiveRoles,
            decision.MatchedMappings,
            GetCorrelationId(httpContext));

        authorizationContextAccessor.Current = context;

        return TokenObservabilityAuthorizationResolution.Allowed(context);
    }

    private async Task<CustomerOrganizationResolution> ResolveCustomerOrganizationAsync(HttpContext httpContext)
    {
        if (!TryReadSingleHeader(httpContext.Request.Headers, "X-Customer-Organization-Slug", out var organizationSlug, out var isAmbiguous))
        {
            return isAmbiguous
                ? CustomerOrganizationResolution.Denied(
                    StatusCodes.Status403Forbidden,
                    "tenant_context_ambiguous",
                    "Customer organization context is ambiguous")
                : CustomerOrganizationResolution.Denied(
                    StatusCodes.Status403Forbidden,
                    "tenant_context_required",
                    "Customer organization context is required");
        }

        var organization = await tenantMetadataStore.FindCustomerOrganizationBySlugAsync(organizationSlug);
        return organization is null
            ? CustomerOrganizationResolution.Denied(
                StatusCodes.Status403Forbidden,
                "tenant_context_required",
                "Customer organization context is required")
            : CustomerOrganizationResolution.Allowed(organization);
    }

    private static AuthenticatedTokenClaims? TryReadAuthenticatedClaims(HttpContext httpContext)
    {
        if (!TryReadSingleHeader(httpContext.Request.Headers, "X-MS-CLIENT-PRINCIPAL", out var encodedPrincipal, out _) ||
            string.IsNullOrWhiteSpace(encodedPrincipal))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPrincipal));
            var principal = JsonSerializer.Deserialize<ClientPrincipal>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (principal?.Claims is null || principal.Claims.Count == 0)
            {
                return null;
            }

            var claims = principal.Claims
                .Where(static claim => !string.IsNullOrWhiteSpace(claim.Type) && !string.IsNullOrWhiteSpace(claim.Value))
                .ToArray();
            var subject = FindClaimValue(claims, "sub", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
            var displayLabel = FindClaimValue(claims, "name", "preferred_username", "upn", "email") ??
                httpContext.Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"].ToString();

            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(displayLabel))
            {
                return null;
            }

            return new AuthenticatedTokenClaims(
                Issuer: RequiredClaim(claims, "iss"),
                ExternalTenantId: RequiredClaim(claims, "tid", "http://schemas.microsoft.com/identity/claims/tenantid"),
                Audience: RequiredClaim(claims, "aud"),
                Subject: subject,
                DisplayLabel: displayLabel,
                Email: FindClaimValue(claims, "preferred_username", "email", "upn"),
                GroupObjectIds: FindClaimValues(claims, "groups"),
                AppRoles: FindClaimValues(claims, "roles", "role"));
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string RequiredClaim(IReadOnlyList<ClientPrincipalClaim> claims, params string[] claimTypes)
    {
        return FindClaimValue(claims, claimTypes) ??
            throw new ArgumentException("Required authenticated claim is missing.", nameof(claims));
    }

    private static string? FindClaimValue(IReadOnlyList<ClientPrincipalClaim> claims, params string[] claimTypes)
    {
        return claims
            .Where(claim => claimTypes.Contains(claim.Type, StringComparer.Ordinal))
            .Select(static claim => claim.Value.Trim())
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
    }

    private static string[] FindClaimValues(IReadOnlyList<ClientPrincipalClaim> claims, params string[] claimTypes)
    {
        return claims
            .Where(claim => claimTypes.Contains(claim.Type, StringComparer.Ordinal))
            .Select(static claim => claim.Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryReadSingleHeader(
        IHeaderDictionary headers,
        string headerName,
        out string value,
        out bool isAmbiguous)
    {
        value = string.Empty;
        isAmbiguous = false;

        if (!headers.TryGetValue(headerName, out var values) || StringValues.IsNullOrEmpty(values))
        {
            return false;
        }

        if (values.Count != 1 || values[0]?.Contains(',', StringComparison.Ordinal) == true)
        {
            isAmbiguous = true;
            return false;
        }

        value = values[0]?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetCorrelationId(HttpContext httpContext)
    {
        return TokenObservabilityCorrelationId.Resolve(httpContext);
    }

    private sealed class ClientPrincipal
    {
        [JsonPropertyName("claims")]
        public IReadOnlyList<ClientPrincipalClaim> Claims { get; set; } = [];
    }

    private sealed class ClientPrincipalClaim
    {
        [JsonPropertyName("typ")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("val")]
        public string Value { get; set; } = string.Empty;
    }
}

internal sealed record TokenObservabilityAuthorizationContext(
    CustomerOrganization CustomerOrganization,
    IdentityTenant IdentityTenant,
    ProductUser ProductUser,
    IReadOnlyList<ProductRole> EffectiveRoles,
    IReadOnlyList<ProductRoleMapping> MatchedMappings,
    string CorrelationId);

internal sealed record TokenObservabilityAuthorizationResolution(
    bool IsAllowed,
    TokenObservabilityAuthorizationContext? Context,
    int StatusCode,
    string Code,
    string Title)
{
    public static TokenObservabilityAuthorizationResolution Allowed(TokenObservabilityAuthorizationContext context)
    {
        return new TokenObservabilityAuthorizationResolution(
            IsAllowed: true,
            context,
            StatusCodes.Status200OK,
            string.Empty,
            string.Empty);
    }

    public static TokenObservabilityAuthorizationResolution Denied(int statusCode, string code, string title)
    {
        return new TokenObservabilityAuthorizationResolution(
            IsAllowed: false,
            Context: null,
            statusCode,
            code,
            title);
    }
}

internal sealed record CustomerOrganizationResolution(
    bool IsAllowed,
    CustomerOrganization? CustomerOrganization,
    int StatusCode,
    string Code,
    string Title)
{
    public static CustomerOrganizationResolution Allowed(CustomerOrganization organization)
    {
        return new CustomerOrganizationResolution(
            IsAllowed: true,
            organization,
            StatusCodes.Status200OK,
            string.Empty,
            string.Empty);
    }

    public static CustomerOrganizationResolution Denied(int statusCode, string code, string title)
    {
        return new CustomerOrganizationResolution(
            IsAllowed: false,
            CustomerOrganization: null,
            statusCode,
            code,
            title);
    }
}
