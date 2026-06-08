using System.Text.Json;

namespace AiAgentTokenObservability.Storage.Enrichment;

public sealed class RepoContextEnrichmentService(ITelemetryStore store)
{
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z",
        ".bin",
        ".bmp",
        ".dll",
        ".exe",
        ".gif",
        ".ico",
        ".jpg",
        ".jpeg",
        ".pdf",
        ".pdb",
        ".png",
        ".so",
        ".zip"
    };

    public async Task<RepoContextEnrichmentResult> EnrichAsync(
        RepoContextEnrichmentRequest request,
        CancellationToken cancellationToken)
    {
        var repoRoot = ResolveRepoRoot(request.RepoPath);
        var repoPathHash = PrivacyHash.RepoPath(repoRoot);
        var workspaceRepo = (await store.ListWorkspaceReposAsync(cancellationToken))
            .FirstOrDefault(repo => repo.RepoPathHash == repoPathHash);

        if (workspaceRepo is null)
        {
            throw new InvalidOperationException(
                "Repo Context Enrichment requires an existing workspace repo created from the same repo path.");
        }

        var contextSources = ScanContextSources(workspaceRepo.WorkspaceRepoId, repoRoot).ToArray();
        var (hotspots, recommendations) = CreateSpecBloatFindings(workspaceRepo, contextSources);

        await store.ReplaceRepoContextAsync(
            workspaceRepo.WorkspaceRepoId,
            contextSources,
            hotspots,
            recommendations,
            cancellationToken);

        return new RepoContextEnrichmentResult(
            workspaceRepo.WorkspaceRepoId,
            contextSources.Length,
            hotspots.Length,
            recommendations.Length);
    }

    private static string ResolveRepoRoot(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
        {
            throw new ArgumentException("Repo path is required.", nameof(repoPath));
        }

        var fullPath = Path.GetFullPath(repoPath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Repo path does not exist: {fullPath}");
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static IEnumerable<ContextSourceModel> ScanContextSources(Guid workspaceRepoId, string repoRoot)
    {
        foreach (var filePath in Directory.EnumerateFiles(repoRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repoRoot, filePath);
            if (ShouldSkip(relativePath))
            {
                continue;
            }

            var normalizedPath = NormalizePath(relativePath);
            var fileInfo = new FileInfo(filePath);
            var fileCategory = ClassifyFileCategory(normalizedPath);
            var specStatus = ClassifySpecArtifactStatus(normalizedPath, fileCategory);
            var sourceType = ClassifySourceType(normalizedPath, fileCategory, specStatus);
            int? lineCount = BinaryExtensions.Contains(Path.GetExtension(filePath))
                ? null
                : CountLines(filePath);

            yield return new ContextSourceModel
            {
                WorkspaceRepoId = workspaceRepoId,
                SourceType = sourceType,
                PathHash = PrivacyHash.OpaqueIdentifier(normalizedPath),
                DisplayPath = null,
                FileCategory = fileCategory,
                SpecArtifactStatus = specStatus,
                EligibleForInferredHotspot = IsEligibleForInferredHotspot(fileCategory),
                SizeBytes = fileInfo.Length,
                LineCount = lineCount
            };
        }
    }

    private static (HotspotModel[] Hotspots, RecommendationModel[] Recommendations) CreateSpecBloatFindings(
        WorkspaceRepoModel workspaceRepo,
        IReadOnlyList<ContextSourceModel> contextSources)
    {
        var bloatSpecSources = contextSources
            .Where(source => source is
            {
                FileCategory: "spec",
                SpecArtifactStatus: "bloat",
                EligibleForInferredHotspot: true
            })
            .ToArray();

        if (bloatSpecSources.Length == 0)
        {
            return ([], []);
        }

        var evidenceRefsJson = JsonSerializer.Serialize(new
        {
            context_source_ids = bloatSpecSources.Select(source => source.ContextSourceId).ToArray()
        });
        var hotspot = new HotspotModel
        {
            AgentSessionId = workspaceRepo.AgentSessionId,
            WorkspaceRepoId = workspaceRepo.WorkspaceRepoId,
            SourceType = "spec",
            SourceRef = "stale spec artifact set",
            AttributionType = "inferred",
            Confidence = "medium",
            SuspectedCause = "Superseded or old specification artifacts remain visible to the coding-agent harness.",
            EvidenceRefsJson = evidenceRefsJson,
            TokenBurnScore = bloatSpecSources.Sum(source => source.SizeBytes ?? 0)
        };
        var recommendation = new RecommendationModel
        {
            HotspotId = hotspot.HotspotId,
            RecommendationType = "deterministic",
            RuleId = "rule-2-superseded-spec-bloat",
            TriggerCondition = "One or more spec artifacts are classified as bloat and remain eligible for inferred hotspot attribution.",
            RecommendedAction = "Create active-specs.md, move superseded specs under specs/archive/, and configure agent instructions to load only active specs by default.",
            ExpectedBenefit = "Reduces repeated context loading from stale specification artifacts while preserving archived project history.",
            Confidence = "medium",
            EvidenceRefsJson = evidenceRefsJson
        };

        return ([hotspot], [recommendation]);
    }

    private static bool ShouldSkip(string relativePath)
    {
        var normalizedPath = NormalizePath(relativePath);
        return normalizedPath.StartsWith(".git/", StringComparison.Ordinal) ||
            normalizedPath.StartsWith(".vs/", StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string ClassifySourceType(string normalizedPath, string fileCategory, string? specStatus)
    {
        if (specStatus is not null)
        {
            return "spec";
        }

        return fileCategory switch
        {
            "generated" or "build_artifact" => "generated_artifact",
            "instruction" => "instruction",
            _ => "file"
        };
    }

    private static string ClassifyFileCategory(string normalizedPath)
    {
        var fileName = Path.GetFileName(normalizedPath);
        var extension = Path.GetExtension(normalizedPath);

        if (BinaryExtensions.Contains(extension))
        {
            return "binary";
        }

        if (IsInPathSegment(normalizedPath, "bin") ||
            IsInPathSegment(normalizedPath, "obj") ||
            IsInPathSegment(normalizedPath, "dist") ||
            IsInPathSegment(normalizedPath, "build"))
        {
            return "build_artifact";
        }

        if (IsInPathSegment(normalizedPath, "node_modules") ||
            IsInPathSegment(normalizedPath, "vendor"))
        {
            return "vendor";
        }

        if (fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase))
        {
            return "lockfile";
        }

        if (fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "generated";
        }

        if (fileName.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("CLAUDE.md", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("GEMINI.md", StringComparison.OrdinalIgnoreCase))
        {
            return "instruction";
        }

        if (IsSpecLikePath(normalizedPath))
        {
            return "spec";
        }

        return extension is ".cs" or ".razor" or ".css" or ".json" or ".md" ? "source" : "unknown";
    }

    private static string? ClassifySpecArtifactStatus(string normalizedPath, string fileCategory)
    {
        if (fileCategory != "spec")
        {
            return null;
        }

        var lowerPath = normalizedPath.ToLowerInvariant();
        if (lowerPath.Contains("/active/", StringComparison.Ordinal) ||
            lowerPath.Contains("active-specs.md", StringComparison.Ordinal) ||
            lowerPath.Contains("current", StringComparison.Ordinal))
        {
            return "active";
        }

        if (lowerPath.Contains("/archive/", StringComparison.Ordinal) ||
            lowerPath.Contains("old", StringComparison.Ordinal) ||
            lowerPath.Contains("superseded", StringComparison.Ordinal) ||
            lowerPath.Contains("stale", StringComparison.Ordinal) ||
            lowerPath.Contains("completed", StringComparison.Ordinal))
        {
            return "bloat";
        }

        return "neutral";
    }

    private static bool IsSpecLikePath(string normalizedPath)
    {
        var lowerPath = normalizedPath.ToLowerInvariant();
        return lowerPath.StartsWith("specs/", StringComparison.Ordinal) ||
            lowerPath.Contains("/specs/", StringComparison.Ordinal) ||
            lowerPath.StartsWith("docs/prd/", StringComparison.Ordinal) ||
            lowerPath.StartsWith("docs/scenarios/", StringComparison.Ordinal) ||
            lowerPath.Contains("spec", StringComparison.Ordinal);
    }

    private static bool IsInPathSegment(string normalizedPath, string segment)
    {
        return normalizedPath.Equals(segment, StringComparison.Ordinal) ||
            normalizedPath.StartsWith($"{segment}/", StringComparison.Ordinal) ||
            normalizedPath.Contains($"/{segment}/", StringComparison.Ordinal);
    }

    private static bool IsEligibleForInferredHotspot(string fileCategory)
    {
        return fileCategory is not ("generated" or "lockfile" or "vendor" or "binary" or "build_artifact");
    }

    private static int CountLines(string filePath)
    {
        try
        {
            return File.ReadLines(filePath).Count();
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
