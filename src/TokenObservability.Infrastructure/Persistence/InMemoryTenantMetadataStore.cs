using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Infrastructure.Persistence;

public sealed class InMemoryTenantMetadataStore(ITenantMetadataClock clock)
{
    private readonly Dictionary<CustomerOrganizationId, CustomerOrganization> customerOrganizations = [];
    private readonly Dictionary<string, CustomerOrganizationId> organizationSlugIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<IdentityTenantId, IdentityTenant> identityTenants = [];
    private readonly Dictionary<ProductUserId, ProductUser> productUsers = [];
    private readonly Dictionary<ProductUserLookupKey, ProductUserId> productUserExternalSubjectIndex = [];
    private readonly object gate = new();

    public Task<CustomerOrganization> CreateCustomerOrganizationAsync(CreateCustomerOrganizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slug = NormalizeRequiredText(request.Slug, nameof(request.Slug)).ToLowerInvariant();
        var displayName = NormalizeRequiredText(request.DisplayName, nameof(request.DisplayName));
        var dataResidencyRegion = NormalizeRequiredText(request.DataResidencyRegion, nameof(request.DataResidencyRegion)).ToLowerInvariant();
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            if (organizationSlugIndex.ContainsKey(slug))
            {
                throw new InvalidOperationException($"Customer organization slug already exists: {slug}");
            }

            var organization = new CustomerOrganization(
                CustomerOrganizationId.NewId(),
                slug,
                displayName,
                dataResidencyRegion,
                request.IsolationTier,
                CustomerOrganizationStatus.Active,
                now,
                now);

            customerOrganizations.Add(organization.CustomerOrganizationId, organization);
            organizationSlugIndex.Add(slug, organization.CustomerOrganizationId);

            return Task.FromResult(organization);
        }
    }

    public Task<CustomerOrganization?> FindCustomerOrganizationBySlugAsync(string slug)
    {
        var normalizedSlug = NormalizeRequiredText(slug, nameof(slug)).ToLowerInvariant();

        lock (gate)
        {
            return Task.FromResult(
                organizationSlugIndex.TryGetValue(normalizedSlug, out var organizationId)
                    ? customerOrganizations[organizationId]
                    : null);
        }
    }

    public Task<IdentityTenant> CreateIdentityTenantAsync(
        CustomerOrganizationId customerOrganizationId,
        CreateIdentityTenantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issuer = NormalizeRequiredText(request.Issuer, nameof(request.Issuer));
        var externalTenantId = NormalizeRequiredText(request.ExternalTenantId, nameof(request.ExternalTenantId));
        var displayName = NormalizeRequiredText(request.DisplayName, nameof(request.DisplayName));
        var allowedAudiences = request.AllowedAudiences
            .Select(audience => NormalizeRequiredText(audience, nameof(request.AllowedAudiences)))
            .ToArray();
        var now = clock.UtcNow.ToUniversalTime();

        if (allowedAudiences.Length == 0)
        {
            throw new ArgumentException("At least one allowed audience is required.", nameof(request));
        }

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);

            if (identityTenants.Values.Any(identityTenant =>
                    identityTenant.CustomerOrganizationId == customerOrganizationId &&
                    identityTenant.Provider == request.Provider &&
                    StringComparer.Ordinal.Equals(identityTenant.ExternalTenantId, externalTenantId)))
            {
                throw new InvalidOperationException("Identity tenant already exists for the customer organization.");
            }

            var identityTenant = new IdentityTenant(
                IdentityTenantId.NewId(),
                customerOrganizationId,
                request.Provider,
                issuer,
                externalTenantId,
                allowedAudiences,
                request.JwksUri,
                displayName,
                IdentityTenantStatus.Active,
                LastValidatedAtUtc: null,
                now,
                now);

            identityTenants.Add(identityTenant.IdentityTenantId, identityTenant);

            return Task.FromResult(identityTenant);
        }
    }

    public Task<ProductUser> CreateProductUserAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        CreateProductUserRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var externalSubjectId = NormalizeRequiredText(request.ExternalSubjectId, nameof(request.ExternalSubjectId));
        var displayLabel = NormalizeRequiredText(request.DisplayLabel, nameof(request.DisplayLabel));
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            RequireIdentityTenantForCustomerOrganization(customerOrganizationId, identityTenantId);

            var lookupKey = new ProductUserLookupKey(customerOrganizationId, identityTenantId, externalSubjectId);

            if (productUserExternalSubjectIndex.ContainsKey(lookupKey))
            {
                throw new InvalidOperationException("Product user external subject already exists for the customer organization and identity tenant.");
            }

            var productUser = new ProductUser(
                ProductUserId.NewId(),
                customerOrganizationId,
                identityTenantId,
                externalSubjectId,
                displayLabel,
                email,
                ProductUserStatus.Active,
                FirstSeenAtUtc: now,
                LastSeenAtUtc: null,
                CreatedAtUtc: now,
                UpdatedAtUtc: now);

            productUsers.Add(productUser.ProductUserId, productUser);
            productUserExternalSubjectIndex.Add(lookupKey, productUser.ProductUserId);

            return Task.FromResult(productUser);
        }
    }

    public Task<ProductUser?> FindProductUserByExternalSubjectAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        string externalSubjectId)
    {
        var normalizedExternalSubjectId = NormalizeRequiredText(externalSubjectId, nameof(externalSubjectId));

        lock (gate)
        {
            var lookupKey = new ProductUserLookupKey(customerOrganizationId, identityTenantId, normalizedExternalSubjectId);

            return Task.FromResult(
                productUserExternalSubjectIndex.TryGetValue(lookupKey, out var productUserId)
                    ? productUsers[productUserId]
                    : null);
        }
    }

    private static string NormalizeRequiredText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }

    private void RequireCustomerOrganization(CustomerOrganizationId customerOrganizationId)
    {
        if (customerOrganizationId == CustomerOrganizationId.Empty ||
            !customerOrganizations.ContainsKey(customerOrganizationId))
        {
            throw new InvalidOperationException("Customer organization does not exist.");
        }
    }

    private void RequireIdentityTenantForCustomerOrganization(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId)
    {
        if (identityTenantId == IdentityTenantId.Empty ||
            !identityTenants.TryGetValue(identityTenantId, out var identityTenant) ||
            identityTenant.CustomerOrganizationId != customerOrganizationId)
        {
            throw new InvalidOperationException("Identity tenant does not belong to the customer organization.");
        }
    }

    private readonly record struct ProductUserLookupKey(
        CustomerOrganizationId CustomerOrganizationId,
        IdentityTenantId IdentityTenantId,
        string ExternalSubjectId);
}
