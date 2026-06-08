using Microsoft.Extensions.Configuration;

namespace AiAgentTokenObservability.Ingestion.Worker;

public sealed record ImportCommandOptions(
    string SourceFilePath,
    string? RepoPath,
    string? RepoFriendlyName,
    string? DeveloperIdentity)
{
    public static ImportCommandOptions? FromArgs(string[] args)
    {
        string? sourceFilePath = null;
        string? repoPath = null;
        string? repoFriendlyName = null;
        string? developerIdentity = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];

            if (arg == "--import" && TryReadValue(args, ref index, out var importPath))
            {
                sourceFilePath = importPath;
                continue;
            }

            if (arg == "--repo-path" && TryReadValue(args, ref index, out var parsedRepoPath))
            {
                repoPath = parsedRepoPath;
                continue;
            }

            if ((arg == "--repo-label" || arg == "--repo-friendly-name") && TryReadValue(args, ref index, out var label))
            {
                repoFriendlyName = label;
                continue;
            }

            if (arg == "--developer-identity" && TryReadValue(args, ref index, out var parsedDeveloperIdentity))
            {
                developerIdentity = parsedDeveloperIdentity;
            }
        }

        return string.IsNullOrWhiteSpace(sourceFilePath)
            ? null
            : new ImportCommandOptions(sourceFilePath, repoPath, repoFriendlyName, developerIdentity);
    }

    public static ImportCommandOptions? FromConfiguration(IConfiguration configuration)
    {
        var sourceFilePath = configuration["DirectFileImport:SourceFilePath"];
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return null;
        }

        return new ImportCommandOptions(
            sourceFilePath,
            configuration["DirectFileImport:RepoPath"],
            configuration["DirectFileImport:RepoFriendlyName"],
            configuration["DirectFileImport:DeveloperIdentity"]);
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
