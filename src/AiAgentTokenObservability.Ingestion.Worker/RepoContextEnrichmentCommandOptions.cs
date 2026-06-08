using Microsoft.Extensions.Configuration;

namespace AiAgentTokenObservability.Ingestion.Worker;

public sealed record RepoContextEnrichmentCommandOptions(string RepoPath)
{
    public static RepoContextEnrichmentCommandOptions? FromArgs(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--enrich-repo" && TryReadValue(args, ref index, out var repoPath))
            {
                return string.IsNullOrWhiteSpace(repoPath)
                    ? null
                    : new RepoContextEnrichmentCommandOptions(repoPath);
            }
        }

        return null;
    }

    public static RepoContextEnrichmentCommandOptions? FromConfiguration(IConfiguration configuration)
    {
        var repoPath = configuration["RepoContextEnrichment:RepoPath"];
        return string.IsNullOrWhiteSpace(repoPath)
            ? null
            : new RepoContextEnrichmentCommandOptions(repoPath);
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        index++;
        value = args[index];
        return true;
    }
}
