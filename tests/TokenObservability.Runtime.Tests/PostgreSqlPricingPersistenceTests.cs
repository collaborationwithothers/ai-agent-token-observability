using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;
using TokenObservability.Api;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Pricing;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;
using TokenObservability.Jobs;

namespace TokenObservability.Runtime.Tests;

public sealed class PostgreSqlPricingPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer postgreSql = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tokenobs")
        .WithUsername("tokenobs")
        .WithPassword("tokenobs")
        .Build();

    private NpgsqlDataSource? dataSource;

    public async Task InitializeAsync()
    {
        await postgreSql.StartAsync();
        dataSource = NpgsqlDataSource.Create(postgreSql.GetConnectionString());
        await ApplyMigrationAsync(dataSource);
    }

    public async Task DisposeAsync()
    {
        if (dataSource is not null)
        {
            await dataSource.DisposeAsync();
        }

        await postgreSql.DisposeAsync();
    }

    [Fact]
    public async Task PostgreSqlStorePersistsPricingSeedReviewSupersedeAndUnavailableCost()
    {
        var customerOrganizationId = new CustomerOrganizationId(Guid.Parse("10000000-0000-0000-0000-000000000071"));
        var productUserId = new ProductUserId(Guid.Parse("20000000-0000-0000-0000-000000000071"));
        await SeedTenantGraphAsync(dataSource!, customerOrganizationId, productUserId);
        var store = new PostgreSqlTenantMetadataStore(dataSource!, new StaticTenantMetadataClock(Now));
        var refresh = new ProviderPricingRefreshService(new HttpClient());

        var created = await refresh.CreateCandidateRecordsAsync(
            store,
            customerOrganizationId,
            [
                CreateCandidate("gpt-5", PricingTokenType.Input, "openai-20260615", 1.25m),
                CreateCandidate("gpt-5", PricingTokenType.Output, "openai-20260615", 10.00m),
                CreateCandidate("gpt-5", PricingTokenType.Input, "openai-20260616", 1.50m)
            ],
            "pricing-refresh-postgres");

        Assert.Equal(3, created.Count);
        Assert.All(created, record =>
        {
            Assert.Equal(PricingSourceKind.AutomatedSeed, record.SourceKind);
            Assert.Equal(PricingReviewState.Candidate, record.ReviewState);
        });

        var unavailable = await store.EstimateAndRecordTokenObservationCostAsync(
            new EstimateTokenObservationCostRequest(
                customerOrganizationId,
                "session-postgres-pricing",
                ModelInvocationId: null,
                TokenMetricName.InputTokens,
                TokenCount: 1000,
                TokenMetricStatus.Observed,
                TokenMetricConfidence.Observed,
                "openai",
                "gpt-5",
                "standard",
                created[0].EffectiveFromUtc.AddSeconds(1)));
        Assert.Equal(CostEstimateStatus.Unavailable, unavailable.CostStatus);
        Assert.Null(unavailable.EstimatedCost);
        Assert.Null(unavailable.PricingBasisId);

        var approvedFirst = await store.ApprovePricingBasisAsync(
            new PricingBasisReviewRequest(
                customerOrganizationId,
                created[0].PricingBasisId,
                "audit-pricing-approve-postgres-1",
                "pricing-approve-postgres-1",
                "reviewed"),
            productUserId,
            ProductRole.PlatformAdmin);
        Assert.Equal(PricingReviewState.Approved, approvedFirst.ReviewState);

        var approvedSecond = await store.ApprovePricingBasisAsync(
            new PricingBasisReviewRequest(
                customerOrganizationId,
                created[2].PricingBasisId,
                "audit-pricing-approve-postgres-2",
                "pricing-approve-postgres-2",
                "reviewed"),
            productUserId,
            ProductRole.PlatformAdmin);
        Assert.Equal(PricingReviewState.Approved, approvedSecond.ReviewState);

        var records = await store.ListPricingBasisRecordsAsync(customerOrganizationId);
        var autoSupersededFirst = records.Single(record => record.PricingBasisId == created[0].PricingBasisId);
        Assert.Equal(PricingReviewState.Superseded, autoSupersededFirst.ReviewState);
        Assert.Equal(approvedSecond.EffectiveFromUtc, autoSupersededFirst.EffectiveToUtc);
        Assert.Equal(PricingReviewState.Approved, records.Single(record => record.PricingBasisId == created[2].PricingBasisId).ReviewState);

        var historicalCandidate = await store.CreatePricingSeedCandidateRecordAsync(
            new CreatePricingBasisRecordRequest(
                customerOrganizationId,
                "codex-cli",
                "openai",
                "gpt-5",
                PricingTokenType.Input,
                "standard",
                "USD",
                1.10m,
                "openai-20260614",
                PricingSourceKind.AutomatedSeed,
                PricingReviewState.Candidate,
                approvedSecond.EffectiveFromUtc.AddDays(-1),
                approvedSecond.EffectiveFromUtc,
                "audit-pricing-seed-historical-postgres",
                CreateSourceMetadata("gpt-5")),
            "pricing-refresh-historical-postgres");
        var approvedHistorical = await store.ApprovePricingBasisAsync(
            new PricingBasisReviewRequest(
                customerOrganizationId,
                historicalCandidate.PricingBasisId,
                "audit-pricing-approve-historical-postgres",
                "pricing-approve-historical-postgres",
                "historical_review"),
            productUserId,
            ProductRole.PlatformAdmin);
        Assert.Equal(PricingReviewState.Approved, approvedHistorical.ReviewState);
        records = await store.ListPricingBasisRecordsAsync(customerOrganizationId);
        Assert.Equal(PricingReviewState.Approved, records.Single(record => record.PricingBasisId == created[2].PricingBasisId).ReviewState);

        var estimated = await store.EstimateAndRecordTokenObservationCostAsync(
            new EstimateTokenObservationCostRequest(
                customerOrganizationId,
                "session-postgres-pricing",
                ModelInvocationId: null,
                TokenMetricName.InputTokens,
                TokenCount: 1_000_000,
                TokenMetricStatus.Observed,
                TokenMetricConfidence.Observed,
                "openai",
                "gpt-5",
                "standard",
                approvedSecond.EffectiveFromUtc.AddSeconds(1)));
        Assert.Equal(1.50m, estimated.EstimatedCost);
        Assert.Equal(created[2].PricingBasisId, estimated.PricingBasisId);

        var explicitlySupersededApproved = await store.SupersedePricingBasisAsync(
            new PricingBasisReviewRequest(
                customerOrganizationId,
                created[2].PricingBasisId,
                "audit-pricing-supersede-approved-postgres",
                "pricing-supersede-approved-postgres",
                "contract_replaced"),
            productUserId,
            ProductRole.PlatformAdmin);
        Assert.Equal(PricingReviewState.Superseded, explicitlySupersededApproved.ReviewState);
        Assert.NotNull(explicitlySupersededApproved.EffectiveToUtc);

        var supersededCandidate = await store.SupersedePricingBasisAsync(
            new PricingBasisReviewRequest(
                customerOrganizationId,
                created[1].PricingBasisId,
                "audit-pricing-supersede-postgres",
                "pricing-supersede-postgres",
                "newer_rate_approved"),
            productUserId,
            ProductRole.PlatformAdmin);
        Assert.Equal(PricingReviewState.Superseded, supersededCandidate.ReviewState);

        var unavailableAfterSupersede = await store.EstimateAndRecordTokenObservationCostAsync(
            new EstimateTokenObservationCostRequest(
                customerOrganizationId,
                "session-postgres-pricing",
                ModelInvocationId: null,
                TokenMetricName.InputTokens,
                TokenCount: 1_000_000,
                TokenMetricStatus.Observed,
                TokenMetricConfidence.Observed,
                "openai",
                "gpt-5",
                "standard",
                approvedSecond.EffectiveFromUtc.AddSeconds(1)));
        Assert.Equal(CostEstimateStatus.Unavailable, unavailableAfterSupersede.CostStatus);
        Assert.Null(unavailableAfterSupersede.EstimatedCost);
        Assert.Null(unavailableAfterSupersede.PricingBasisId);

        var auditEvents = await store.ListGovernanceAuditEventsAsync(customerOrganizationId);
        Assert.Contains(auditEvents, audit => audit.Decision == "created" && audit.EvidenceMetadata["operation"] == "pricing_seed_refresh");
        Assert.Contains(auditEvents, audit => audit.EvidenceMetadata["operation"] == "pricing_basis_approved");
        Assert.Contains(auditEvents, audit => audit.EvidenceMetadata["operation"] == "pricing_basis_superseded");
    }

    [Fact]
    public async Task PostgreSqlPricingRefreshFailsClosedWhenCustomerOrganizationIsMissing()
    {
        var store = new PostgreSqlTenantMetadataStore(dataSource!, new StaticTenantMetadataClock(Now));
        var refresh = new ProviderPricingRefreshService(new HttpClient());

        await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.CreateCandidateRecordsAsync(
            store,
            new CustomerOrganizationId(Guid.Parse("30000000-0000-0000-0000-000000000071")),
            [CreateCandidate("gpt-5", PricingTokenType.Input, "openai-20260615", 1.25m)],
            "pricing-refresh-missing-tenant"));
    }

    [Fact]
    public async Task PostgreSqlPricingRefreshFailsClosedWhenCustomerOrganizationIsInactive()
    {
        var customerOrganizationId = new CustomerOrganizationId(Guid.Parse("90000000-0000-0000-0000-000000000071"));
        var productUserId = new ProductUserId(Guid.Parse("91000000-0000-0000-0000-000000000071"));
        await SeedTenantGraphAsync(dataSource!, customerOrganizationId, productUserId, customerOrganizationStatus: "suspended");
        var store = new PostgreSqlTenantMetadataStore(dataSource!, new StaticTenantMetadataClock(Now));
        var refresh = new ProviderPricingRefreshService(new HttpClient());

        await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.CreateCandidateRecordsAsync(
            store,
            customerOrganizationId,
            [CreateCandidate("gpt-5", PricingTokenType.Input, "openai-20260615", 1.25m)],
            "pricing-refresh-inactive-tenant"));
    }

    [Fact]
    public async Task ProductApiReturnsNotImplementedForUnsupportedPostgreSqlMetadataSurface()
    {
        var customerOrganizationId = new CustomerOrganizationId(Guid.Parse("92000000-0000-0000-0000-000000000071"));
        var productUserId = new ProductUserId(Guid.Parse("93000000-0000-0000-0000-000000000071"));
        await SeedTenantGraphAsync(dataSource!, customerOrganizationId, productUserId);
        using var factory = CreatePostgreSqlApiFactory(postgreSql.GetConnectionString());
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/sessions",
            "contoso",
            CreateClaims("admin-subject", ["admin-group"]));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("metadata_store_not_supported", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostgreSqlApprovalDoesNotExtendExpiredApprovedPricingWindow()
    {
        var customerOrganizationId = new CustomerOrganizationId(Guid.Parse("70000000-0000-0000-0000-000000000071"));
        var productUserId = new ProductUserId(Guid.Parse("80000000-0000-0000-0000-000000000071"));
        await SeedTenantGraphAsync(dataSource!, customerOrganizationId, productUserId);
        var store = new PostgreSqlTenantMetadataStore(dataSource!, new StaticTenantMetadataClock(Now));
        var expiredWindowEnd = Now.AddDays(-5);

        var expiredApproved = await CreateApprovedSeedPricingBasisAsync(
            store,
            customerOrganizationId,
            productUserId,
            "audit-pricing-approved-expired-postgres",
            "openai-20260601",
            Now.AddDays(-10),
            expiredWindowEnd);
        var candidate = await store.CreatePricingSeedCandidateRecordAsync(
            new CreatePricingBasisRecordRequest(
                customerOrganizationId,
                "codex-cli",
                "openai",
                "gpt-5",
                PricingTokenType.Input,
                "standard",
                "USD",
                1.50m,
                "openai-20260615",
                PricingSourceKind.AutomatedSeed,
                PricingReviewState.Candidate,
                Now,
                EffectiveToUtc: null,
                "audit-pricing-seed-newer-expired-window-postgres",
                CreateSourceMetadata("gpt-5")),
            "pricing-refresh-newer-expired-window-postgres");

        await store.ApprovePricingBasisAsync(
            new PricingBasisReviewRequest(
                customerOrganizationId,
                candidate.PricingBasisId,
                "audit-pricing-approve-newer-expired-window-postgres",
                "pricing-approve-newer-expired-window-postgres",
                "reviewed"),
            productUserId,
            ProductRole.PlatformAdmin);

        var records = await store.ListPricingBasisRecordsAsync(customerOrganizationId);
        var unchangedExpired = records.Single(record => record.PricingBasisId == expiredApproved.PricingBasisId);
        Assert.Equal(PricingReviewState.Approved, unchangedExpired.ReviewState);
        Assert.Equal(expiredWindowEnd, unchangedExpired.EffectiveToUtc);
    }

    [Fact]
    public async Task PostgreSqlPricingOverrideRejectsUnsafePricingFields()
    {
        var customerOrganizationId = new CustomerOrganizationId(Guid.Parse("50000000-0000-0000-0000-000000000071"));
        var productUserId = new ProductUserId(Guid.Parse("60000000-0000-0000-0000-000000000071"));
        await SeedTenantGraphAsync(dataSource!, customerOrganizationId, productUserId);
        var store = new PostgreSqlTenantMetadataStore(dataSource!, new StaticTenantMetadataClock(Now));

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateCustomerPricingOverrideAsync(
            new CreatePricingBasisRecordRequest(
                customerOrganizationId,
                "codex-cli",
                "raw prompt",
                "gpt-5",
                PricingTokenType.Input,
                "standard",
                "USD",
                1.25m,
                "openai-20260615",
                PricingSourceKind.AdminOverride,
                PricingReviewState.Approved,
                Now,
                EffectiveToUtc: null,
                "audit-pricing-override-unsafe-postgres",
                CreateSourceMetadata("gpt-5")),
            productUserId,
            ProductRole.PlatformAdmin,
            "pricing-override-unsafe-postgres"));
    }

    private static ProviderPricingSeedCandidate CreateCandidate(
        string modelName,
        PricingTokenType tokenType,
        string pricingVersion,
        decimal pricePerMillionTokens)
    {
        return new ProviderPricingSeedCandidate(
            "codex-cli",
            "openai",
            modelName,
            tokenType,
            "standard",
            "USD",
            pricePerMillionTokens,
            pricingVersion,
            CreateSourceMetadata(modelName));
    }

    private static IReadOnlyDictionary<string, string> CreateSourceMetadata(string modelName)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source_url"] = "https://developers.openai.com/api/docs/pricing",
            ["source_retrieved_at_utc"] = Now.ToString("O"),
            ["provider_document_version"] = "openai-api-pricing",
            ["provider_sku_name"] = modelName,
            ["billing_route"] = "standard"
        };
    }

    private static async Task<PricingBasisRecord> CreateApprovedSeedPricingBasisAsync(
        PostgreSqlTenantMetadataStore store,
        CustomerOrganizationId customerOrganizationId,
        ProductUserId productUserId,
        string auditEventId,
        string pricingVersion,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset effectiveToUtc)
    {
        await store.RecordGovernanceAuditEventAsync(
            customerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                auditEventId,
                productUserId,
                ProductRole.PlatformAdmin,
                ProductAuthorizationAction.PricingManage,
                new ProductScope(ProductScopeKind.Pricing, ScopeId: null),
                "created",
                DenialReason: null,
                $"correlation-{auditEventId}",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["evidence_kind"] = "pricing_basis",
                    ["operation"] = "pricing_seed_refresh",
                    ["result"] = "approved"
                }));

        return await store.CreatePricingBasisRecordAsync(
            new CreatePricingBasisRecordRequest(
                customerOrganizationId,
                "codex-cli",
                "openai",
                "gpt-5",
                PricingTokenType.Input,
                "standard",
                "USD",
                1.00m,
                pricingVersion,
                PricingSourceKind.AutomatedSeed,
                PricingReviewState.Approved,
                effectiveFromUtc,
                effectiveToUtc,
                auditEventId,
                CreateSourceMetadata("gpt-5")));
    }

    private static WebApplicationFactory<TokenObservabilityApiAssemblyMarker> CreatePostgreSqlApiFactory(
        string connectionString)
    {
        return new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:ProductMetadataStore"] = connectionString
                    });
                });
            });
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string tenantSlug,
        AuthenticatedTokenClaims claims)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", EncodePrincipal(claims));
        request.Headers.Add("X-Customer-Organization-Slug", tenantSlug);
        return request;
    }

    private static string EncodePrincipal(AuthenticatedTokenClaims claims)
    {
        var principal = new
        {
            claims = BuildClaims(claims).Select(static claim => new
            {
                typ = claim.Type,
                val = claim.Value
            })
        };
        var json = JsonSerializer.Serialize(principal);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static IEnumerable<(string Type, string Value)> BuildClaims(AuthenticatedTokenClaims claims)
    {
        yield return ("iss", claims.Issuer);
        yield return ("tid", claims.ExternalTenantId);
        yield return ("aud", claims.Audience);
        yield return ("sub", claims.Subject);
        yield return ("name", claims.DisplayLabel);

        if (!string.IsNullOrWhiteSpace(claims.Email))
        {
            yield return ("preferred_username", claims.Email);
        }

        foreach (var groupObjectId in claims.GroupObjectIds)
        {
            yield return ("groups", groupObjectId);
        }

        foreach (var appRole in claims.AppRoles)
        {
            yield return ("roles", appRole);
        }
    }

    private static AuthenticatedTokenClaims CreateClaims(
        string subject,
        IReadOnlyList<string> groupObjectIds)
    {
        return new AuthenticatedTokenClaims(
            Issuer: "https://sts.windows.net/contoso-tenant/",
            ExternalTenantId: "contoso-tenant",
            Audience: "api://token-observability",
            Subject: subject,
            DisplayLabel: subject,
            Email: $"{subject}@example.test",
            GroupObjectIds: groupObjectIds,
            AppRoles: []);
    }

    private static async Task ApplyMigrationAsync(NpgsqlDataSource dataSource)
    {
        var migration = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "TokenObservability.Infrastructure",
            "Persistence",
            "PostgreSql",
            "Migrations",
            "0001_tenant_metadata.sql"));
        await using var command = dataSource.CreateCommand(migration);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedTenantGraphAsync(
        NpgsqlDataSource dataSource,
        CustomerOrganizationId customerOrganizationId,
        ProductUserId productUserId,
        string customerOrganizationStatus = "active")
    {
        const string sql = """
            INSERT INTO customer_organization (
                customer_organization_id, slug, display_name, data_residency_region, isolation_tier, status, created_at_utc, updated_at_utc)
            VALUES (
                @customer_organization_id, 'contoso', 'Contoso', 'eastus2', 'shared', @customer_organization_status, @now, @now);

            INSERT INTO identity_tenant (
                identity_tenant_id, customer_organization_id, provider, issuer, external_tenant_id, allowed_audiences_json, jwks_uri, display_name, status, created_at_utc, updated_at_utc)
            VALUES (
                @identity_tenant_id, @customer_organization_id, 'microsoft_entra', 'https://sts.windows.net/contoso-tenant/', 'contoso-tenant', '["api://token-observability"]'::jsonb, NULL, 'Contoso Entra ID', 'active', @now, @now);

            INSERT INTO product_user (
                product_user_id, customer_organization_id, identity_tenant_id, external_subject_id, display_label, email, status, first_seen_at_utc, created_at_utc, updated_at_utc)
            VALUES (
                @product_user_id, @customer_organization_id, @identity_tenant_id, 'admin-subject', 'admin', 'admin@example.test', 'active', @now, @now, @now);

            INSERT INTO governance_audit_event (
                audit_event_id, customer_organization_id, actor_product_user_id, effective_role, action, target_resource_kind,
                target_resource_id, decision, denial_reason, correlation_id, evidence_metadata_json, created_at_utc)
            VALUES (
                'audit-role-mapping-seed', @customer_organization_id, @product_user_id, 'PlatformAdmin', 'IdentityManage', 'organization',
                @customer_organization_id::text, 'created', NULL, 'role-mapping-seed', '{"evidence_kind":"admin_operation","operation":"role_mapping_seed","result":"created"}'::jsonb, @now);

            INSERT INTO product_role_mapping (
                product_role_mapping_id, customer_organization_id, identity_tenant_id, external_principal_type, external_principal_id,
                product_role, scope_kind, scope_id, status, effective_from_utc, effective_to_utc,
                created_by_product_user_id, changed_by_product_user_id, audit_event_id, created_at_utc, updated_at_utc)
            VALUES (
                @product_role_mapping_id, @customer_organization_id, @identity_tenant_id, 'group_object_id', 'admin-group',
                'PlatformAdmin', 'organization', NULL, 'active', @now, NULL,
                @product_user_id, @product_user_id, 'audit-role-mapping-seed', @now, @now);

            INSERT INTO agent_session (
                agent_session_id, customer_organization_id, product_user_id, harness_setup_profile_id, harness, started_at_utc, ended_at_utc, session_status,
                repository_evidence_state, content_capture_summary, recommendation_status, token_metric_status, token_metric_confidence,
                model_names_json, source_telemetry_envelope_ids_json, created_at_utc, updated_at_utc)
            VALUES (
                'session-postgres-pricing', @customer_organization_id, @product_user_id, 'profile-contoso-codex', 'codex-cli', @now, NULL, 'active',
                'unavailable', 'metadata_only', 'not_started', 'observed', 'observed', '[]'::jsonb, '[]'::jsonb, @now, @now);
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        command.Parameters.AddWithValue("customer_organization_status", customerOrganizationStatus);
        command.Parameters.AddWithValue("identity_tenant_id", Guid.Parse("40000000-0000-0000-0000-000000000071"));
        command.Parameters.AddWithValue("product_user_id", productUserId.Value);
        command.Parameters.AddWithValue("product_role_mapping_id", Guid.Parse("94000000-0000-0000-0000-000000000071"));
        command.Parameters.AddWithValue("now", Now);
        await command.ExecuteNonQueryAsync();
    }
}
