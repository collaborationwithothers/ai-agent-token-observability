using System.Text.Json;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

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

        output.WriteLine($"Token Observability job command '{command.Name}' is a placeholder.");
        return Task.FromResult(0);
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
