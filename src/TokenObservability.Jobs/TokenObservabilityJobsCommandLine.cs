using System.Text.Json;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Recommendations;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;
using TokenObservability.Infrastructure.Recommendations;

namespace TokenObservability.Jobs;

public static class TokenObservabilityJobsCommandLine
{
    public static Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        InMemoryTenantMetadataStore? tenantMetadataStore = null,
        ProviderPricingRefreshService? pricingRefreshService = null)
    {
        if (args.Count is 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("--list-commands", StringComparer.Ordinal))
        {
            WriteCommandList(output);
            return Task.FromResult(0);
        }

        var commandName = args[0];
        var command = JobCommandCatalog.Commands.SingleOrDefault(candidate => candidate.Name == commandName);
        if (command is null)
        {
            output.WriteLine($"Unknown Token Observability job command: {commandName}");
            WriteCommandList(output);
            return Task.FromResult(2);
        }

        if (StringComparer.Ordinal.Equals(command.Name, "refresh-pricing"))
        {
            return RunRefreshPricingAsync(args, output, tenantMetadataStore, pricingRefreshService);
        }

        if (StringComparer.Ordinal.Equals(command.Name, "generate-recommendations"))
        {
            return RunGenerateRecommendationsAsync(args, output, tenantMetadataStore);
        }

        if (StringComparer.Ordinal.Equals(command.Name, "redact-content"))
        {
            return RunRedactContentAsync(args, output, tenantMetadataStore);
        }

        if (StringComparer.Ordinal.Equals(command.Name, "retention-cleanup"))
        {
            return RunRetentionCleanupAsync(args, output, tenantMetadataStore);
        }

        output.WriteLine($"Token Observability job command '{command.Name}' is a placeholder.");
        return Task.FromResult(0);
    }

    private static async Task<int> RunRedactContentAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        InMemoryTenantMetadataStore? tenantMetadataStore)
    {
        var customerOrganizationId = ReadContentCustomerOrganizationId(args, output, "redact-content");
        if (customerOrganizationId is null)
        {
            output.WriteLine("No raw failed content was read or emitted.");
            return 2;
        }

        if (tenantMetadataStore is null)
        {
            output.WriteLine("redact-content requires a loaded tenant metadata store.");
            output.WriteLine("No raw failed content was read or emitted.");
            return 2;
        }

        if (await tenantMetadataStore.FindCustomerOrganizationAsync(customerOrganizationId.Value) is null)
        {
            output.WriteLine("redact-content requires a tenant metadata store with the target customer organization loaded.");
            output.WriteLine("No raw failed content was read or emitted.");
            return 2;
        }

        output.WriteLine("redact-content processed metadata-only content review work.");
        output.WriteLine("Retry-redaction requests remain audit-backed metadata decisions unless a sanitized approved artifact exists.");
        output.WriteLine("Approved bounded excerpts are stored through Product API review decisions.");
        output.WriteLine("No raw failed content was read or emitted.");
        return 0;
    }

    private static async Task<int> RunRetentionCleanupAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        InMemoryTenantMetadataStore? tenantMetadataStore)
    {
        var customerOrganizationId = ReadContentCustomerOrganizationId(args, output, "retention-cleanup");
        if (customerOrganizationId is null)
        {
            return 2;
        }

        if (tenantMetadataStore is null)
        {
            output.WriteLine("retention-cleanup requires a loaded tenant metadata store.");
            output.WriteLine("No content references were changed.");
            return 2;
        }

        try
        {
            if (await tenantMetadataStore.FindCustomerOrganizationAsync(customerOrganizationId.Value) is null)
            {
                output.WriteLine("retention-cleanup requires a tenant metadata store with the target customer organization loaded.");
                output.WriteLine("No content references were changed.");
                return 2;
            }

            var asOfUtc = ReadDateTimeOffsetOption(args, "--as-of-utc") ?? DateTimeOffset.UtcNow;
            var result = await tenantMetadataStore.CleanupExpiredContentReferencesAsync(
                customerOrganizationId.Value,
                asOfUtc,
                actorProductUserId: null,
                effectiveRole: null,
                ReadOption(args, "--correlation-id") ?? $"retention-cleanup-{Guid.NewGuid():N}");

            output.WriteLine($"Expired {result.ExpiredContentReferenceCount} captured content reference blob pointer(s).");
            output.WriteLine("Content reference metadata and audit evidence were retained.");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
        {
            output.WriteLine($"retention-cleanup failed: {ex.Message}");
            output.WriteLine("No raw failed content was read or emitted.");
            return 2;
        }
    }

    private static async Task<int> RunGenerateRecommendationsAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        InMemoryTenantMetadataStore? tenantMetadataStore)
    {
        var customerOrganizationId = ReadRecommendationCustomerOrganizationId(args, output);
        var agentSessionId = ReadRecommendationAgentSessionId(args, output);
        if (customerOrganizationId is null || string.IsNullOrWhiteSpace(agentSessionId))
        {
            output.WriteLine("No raw prompt text, code content, command output, or tool results were read or emitted.");
            return 2;
        }

        if (tenantMetadataStore is null)
        {
            output.WriteLine("generate-recommendations requires a loaded tenant metadata store.");
            output.WriteLine("No recommendation records were changed.");
            return 2;
        }

        try
        {
            if (await tenantMetadataStore.FindCustomerOrganizationAsync(customerOrganizationId.Value) is null)
            {
                output.WriteLine("generate-recommendations requires a tenant metadata store with the target customer organization loaded.");
                output.WriteLine("No recommendation records were changed.");
                return 2;
            }

            var generator = new DeterministicRecommendationGenerator(tenantMetadataStore);
            var recommendations = await generator.GenerateForSessionAsync(
                new GenerateRecommendationsRequest(
                    customerOrganizationId.Value,
                    agentSessionId!,
                    ReadOption(args, "--correlation-id") ?? $"generate-recommendations-{Guid.NewGuid():N}"));

            output.WriteLine($"Created {recommendations.Count} deterministic recommendation record(s).");
            output.WriteLine("Recommendation evidence packets contain metadata and hashes only.");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
        {
            output.WriteLine($"generate-recommendations failed: {ex.Message}");
            output.WriteLine("No raw prompt text, code content, command output, or tool results were emitted.");
            return 2;
        }
    }

    private static async Task<int> RunRefreshPricingAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        InMemoryTenantMetadataStore? tenantMetadataStore,
        ProviderPricingRefreshService? pricingRefreshService)
    {
        var provider = ReadOption(args, "--provider") ?? "openai";
        var customerOrganizationId = ReadCustomerOrganizationId(args, output);
        if (customerOrganizationId is null)
        {
            return 2;
        }

        if (tenantMetadataStore is null)
        {
            output.WriteLine("Pricing refresh requires a tenant metadata store with the target customer organization loaded.");
            output.WriteLine("No active pricing basis or cost estimate was changed.");
            return 2;
        }

        using var ownedHttpClient = pricingRefreshService is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) }
            : null;
        var service = pricingRefreshService ?? new ProviderPricingRefreshService(ownedHttpClient!);

        try
        {
            var customerLoadResult = await EnsureCustomerOrganizationLoadedAsync(
                tenantMetadataStore,
                customerOrganizationId.Value,
                args,
                output);
            if (!customerLoadResult)
            {
                return 2;
            }

            var candidates = await service.FetchCandidatesAsync(provider, CancellationToken.None);
            output.WriteLine($"Fetched {candidates.Count} pricing seed candidate(s) for provider '{provider}'.");
            var created = await service.CreateCandidateRecordsAsync(
                tenantMetadataStore,
                customerOrganizationId.Value,
                candidates,
                ReadOption(args, "--correlation-id") ?? $"pricing-refresh-{Guid.NewGuid():N}");
            output.WriteLine($"Created {created.Count} tenant-scoped pricing basis candidate record(s).");
            output.WriteLine("Active pricing basis records remain unchanged until a candidate is approved.");
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException or JsonException)
        {
            output.WriteLine($"Pricing refresh failed for provider '{provider}': {ex.Message}");
            output.WriteLine("No active pricing basis or cost estimate was changed.");
            return 2;
        }
    }

    private static async Task<bool> EnsureCustomerOrganizationLoadedAsync(
        InMemoryTenantMetadataStore tenantMetadataStore,
        CustomerOrganizationId customerOrganizationId,
        IReadOnlyList<string> args,
        TextWriter output)
    {
        if (await tenantMetadataStore.FindCustomerOrganizationAsync(customerOrganizationId) is not null)
        {
            return true;
        }

        var slug = ReadOption(args, "--customer-organization-slug");
        if (string.IsNullOrWhiteSpace(slug))
        {
            output.WriteLine("Pricing refresh requires --customer-organization-slug when the customer organization is not already loaded.");
            output.WriteLine("No active pricing basis or cost estimate was changed.");
            return false;
        }

        await tenantMetadataStore.EnsureCustomerOrganizationLoadedAsync(
            new EnsureCustomerOrganizationLoadedRequest(
                customerOrganizationId,
                slug,
                ReadOption(args, "--customer-organization-display-name") ?? slug,
                ReadOption(args, "--data-residency-region") ?? "unknown",
                CustomerOrganizationIsolationTier.Shared));
        return true;
    }

    private static CustomerOrganizationId? ReadCustomerOrganizationId(IReadOnlyList<string> args, TextWriter output)
    {
        var rawCustomerOrganizationId = ReadOption(args, "--customer-organization-id");
        if (string.IsNullOrWhiteSpace(rawCustomerOrganizationId))
        {
            output.WriteLine("Pricing refresh requires --customer-organization-id.");
            output.WriteLine("No active pricing basis or cost estimate was changed.");
            return null;
        }

        if (!Guid.TryParse(rawCustomerOrganizationId, out var parsed) || parsed == Guid.Empty)
        {
            output.WriteLine("Pricing refresh requires a valid non-empty --customer-organization-id.");
            output.WriteLine("No active pricing basis or cost estimate was changed.");
            return null;
        }

        return new CustomerOrganizationId(parsed);
    }

    private static CustomerOrganizationId? ReadRecommendationCustomerOrganizationId(IReadOnlyList<string> args, TextWriter output)
    {
        var rawCustomerOrganizationId = ReadOption(args, "--customer-organization-id");
        if (string.IsNullOrWhiteSpace(rawCustomerOrganizationId))
        {
            output.WriteLine("generate-recommendations requires --customer-organization-id.");
            return null;
        }

        if (!Guid.TryParse(rawCustomerOrganizationId, out var parsed) || parsed == Guid.Empty)
        {
            output.WriteLine("generate-recommendations requires a valid non-empty --customer-organization-id.");
            return null;
        }

        return new CustomerOrganizationId(parsed);
    }

    private static CustomerOrganizationId? ReadContentCustomerOrganizationId(
        IReadOnlyList<string> args,
        TextWriter output,
        string commandName)
    {
        var rawCustomerOrganizationId = ReadOption(args, "--customer-organization-id");
        if (string.IsNullOrWhiteSpace(rawCustomerOrganizationId))
        {
            output.WriteLine($"{commandName} requires --customer-organization-id.");
            return null;
        }

        if (!Guid.TryParse(rawCustomerOrganizationId, out var parsed) || parsed == Guid.Empty)
        {
            output.WriteLine($"{commandName} requires a valid non-empty --customer-organization-id.");
            return null;
        }

        return new CustomerOrganizationId(parsed);
    }

    private static DateTimeOffset? ReadDateTimeOffsetOption(IReadOnlyList<string> args, string name)
    {
        var value = ReadOption(args, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed.ToUniversalTime()
            : throw new ArgumentException($"{name} must be a valid timestamp.");
    }

    private static string? ReadRecommendationAgentSessionId(IReadOnlyList<string> args, TextWriter output)
    {
        var agentSessionId = ReadOption(args, "--agent-session-id");
        if (string.IsNullOrWhiteSpace(agentSessionId))
        {
            output.WriteLine("generate-recommendations requires --agent-session-id.");
            return null;
        }

        return agentSessionId.Trim();
    }

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (StringComparer.Ordinal.Equals(args[index], name))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void WriteCommandList(TextWriter output)
    {
        output.WriteLine("Available Token Observability job commands:");
        foreach (var command in JobCommandCatalog.Commands)
        {
            output.WriteLine($"  {command.Name} - {command.Description}");
        }
    }
}
