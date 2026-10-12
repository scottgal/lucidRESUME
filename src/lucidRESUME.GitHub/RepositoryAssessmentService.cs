using lucidRESUME.GitHub.Models;

namespace lucidRESUME.GitHub;

/// <summary>
/// Produces an explainable retrieval assessment from repository observations.
/// It ranks evidence sources; it does not assess a person's proficiency or assert
/// that the implementation is correct.
/// </summary>
public static class RepositoryAssessmentService
{
    private static readonly string[] ManifestNames =
    [
        ".csproj", ".fsproj", ".vbproj", ".sln", "package.json", "pyproject.toml",
        "requirements.txt", "go.mod", "cargo.toml", "pom.xml", "build.gradle",
        "dockerfile", "compose.yml", "compose.yaml", "chart.yaml", ".nuspec"
    ];

    public static (RepositoryEngineeringSignals Signals, RepositoryAssessment Assessment, string Classification)
        Assess(GitHubRepo repository, GitHubTree? tree, bool hasReadme, int skillCount,
            DateTimeOffset? observedAt = null, RepositoryContribution? contribution = null)
    {
        var paths = tree?.Entries
            .Where(entry => entry.Type.Equals("blob", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Path.Replace('\\', '/'))
            .ToList() ?? [];
        var workflowCount = paths.Count(IsWorkflow);
        var testFileCount = paths.Count(IsTestPath);
        var manifestCount = paths.Count(IsManifest);
        var hasDocs = paths.Any(path => path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
                                       Path.GetFileName(path).StartsWith("readme", StringComparison.OrdinalIgnoreCase));
        var hasRelease = paths.Any(path => IsWorkflow(path) &&
                                           ContainsAny(path, "release", "publish", "deploy"));
        var hasExtension = paths.Any(path =>
            Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase) &&
            (path.Contains("extension", StringComparison.OrdinalIgnoreCase) ||
             paths.Any(candidate => candidate.EndsWith("background.js", StringComparison.OrdinalIgnoreCase) ||
                                    candidate.EndsWith("service-worker.js", StringComparison.OrdinalIgnoreCase))));
        var signals = new RepositoryEngineeringSignals
        {
            TreeObserved = tree is not null,
            HasReadme = hasReadme,
            HasTests = testFileCount > 0,
            HasCi = workflowCount > 0,
            HasReleaseAutomation = hasRelease,
            HasPackageManifests = manifestCount > 0,
            HasDocumentation = hasDocs,
            HasBrowserExtension = hasExtension,
            ManifestCount = manifestCount,
            WorkflowCount = workflowCount,
            TestFileCount = testFileCount
        };

        var spanDays = Math.Max(0, (repository.PushedAt - repository.CreatedAt).TotalDays);
        var observed = observedAt ?? DateTimeOffset.UtcNow;
        var ageDays = Math.Max(0, (observed - repository.CreatedAt).TotalDays);
        var inactiveDays = Math.Max(0, (observed - repository.PushedAt).TotalDays);
        var originality = repository.Fork ? 0.15 : 1.0;
        var longevity = Math.Clamp(Math.Log10(1 + spanDays) / Math.Log10(1 + 730), 0, 1);
        var recency = inactiveDays <= 180 ? 1.0 : inactiveDays <= 365 ? .8 : inactiveDays <= 730 ? .55 : .25;
        var processSignals = Count(signals.HasTests, signals.HasCi, signals.HasPackageManifests);
        var deliverySignals = Count(signals.HasReleaseAutomation, signals.HasPackageManifests,
            repository.Size >= 1_000);
        var process = processSignals / 3d;
        var documentation = Count(signals.HasReadme, signals.HasDocumentation,
            !string.IsNullOrWhiteSpace(repository.Description)) / 3d;
        var delivery = deliverySignals / 3d;
        var depth = Math.Clamp(Math.Log10(1 + repository.Size) / 5d, 0, 1);
        var semantic = Math.Clamp(skillCount / 12d, 0, 1);
        var strength = originality * (.23 * longevity + .17 * recency + .20 * process +
                                      .12 * documentation + .13 * delivery + .10 * depth + .05 * semantic);

        var classification = Classify(repository, signals, spanDays, ageDays);
        var reasons = new List<string>
        {
            repository.Fork ? "Repository is a fork; authorship requires separate attribution." : "Original repository under the imported account.",
            $"Observed activity spans {spanDays / 365.25:F1} years; last push was {inactiveDays:F0} days ago."
        };
        if (repository.Archived) reasons.Add("Repository is archived; it remains historical evidence, not current activity.");
        if (signals.HasTests) reasons.Add($"Test structure observed ({signals.TestFileCount} matching files).");
        if (signals.HasCi) reasons.Add($"CI/workflow structure observed ({signals.WorkflowCount} workflows).");
        if (signals.HasReleaseAutomation) reasons.Add("Release or publishing workflow observed.");
        if (signals.HasBrowserExtension) reasons.Add("Browser extension manifest and runtime structure observed.");
        if (tree?.Truncated == true) reasons.Add("GitHub tree response was truncated; file observations are incomplete.");
        if (tree is null) reasons.Add("Deep repository tree was not inspected; structural signals are unknown.");

        if (contribution is not null)
        {
            var attribution = !contribution.Observed ? .15 : contribution.CandidateCommits == 0 ? .05
                : contribution.IsMainContributor == true ? 1d
                : .35 + .65 * Math.Sqrt(contribution.ObservedCommitShare ?? 0);
            strength *= attribution;
            reasons.Add(!contribution.Observed ? "Candidate contribution evidence is unavailable; personal attribution is unknown."
                : $"Observed {contribution.CandidateCommits} linked candidate commits; rank {contribution.CandidateRank?.ToString() ?? "unknown"}; " +
                    $"main contributor: {contribution.IsMainContributor?.ToString() ?? "unknown"}. Commit counts indicate attribution, not quality or years of experience.");
        }
        return (signals, new RepositoryAssessment
        {
            Originality = originality,
            Longevity = longevity,
            EngineeringProcess = process,
            Documentation = documentation,
            Delivery = delivery,
            EvidenceStrength = Math.Clamp(strength, 0, .95),
            Reasons = reasons
        }, classification);
    }

    public static string SearchableSummary(GitHubProjectProfile profile)
    {
        var signals = new List<string>();
        if (profile.Engineering.HasTests) signals.Add("automated tests");
        if (profile.Engineering.HasCi) signals.Add("CI");
        if (profile.Engineering.HasReleaseAutomation) signals.Add("release automation");
        if (profile.Engineering.HasPackageManifests) signals.Add("package/build manifests");
        if (profile.Engineering.HasDocumentation) signals.Add("documentation");
        if (profile.Engineering.HasBrowserExtension) signals.Add("browser extension");
        var observed = !profile.Engineering.TreeObserved
            ? "deep repository structure not inspected"
            : signals.Count == 0 ? "no matching engineering structure observed" : string.Join(", ", signals);
        var state = profile.IsArchived ? "archived; " : "";
        return $"{profile.RepositoryClass}; {state}{profile.AgeYears:F1} years old; {profile.ActiveYears:F1}-year observed activity span; {observed}.";
    }

    private static string Classify(GitHubRepo repository, RepositoryEngineeringSignals signals,
        double spanDays, double ageDays)
    {
        if (repository.Fork) return "fork";
        if (spanDays >= 180 && (signals.HasTests || signals.HasCi || signals.HasReleaseAutomation))
            return "sustained-product";
        if (signals.HasBrowserExtension) return "browser-extension-product";
        if (spanDays >= 30 || repository.Size >= 5_000) return "substantial-project";
        if (spanDays <= 14 && ageDays <= 90 && repository.Size < 1_000) return "recent-experiment";
        if (spanDays <= 14 && repository.Size < 1_000) return "small-experiment";
        return "repository";
    }

    private static bool IsWorkflow(string path) =>
        path.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase) &&
        (path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

    private static bool IsTestPath(string path) =>
        path.Contains("/test", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("test", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase);

    private static bool IsManifest(string path)
    {
        var file = Path.GetFileName(path);
        return ManifestNames.Any(name => name.StartsWith('.')
            ? file.EndsWith(name, StringComparison.OrdinalIgnoreCase)
            : file.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static int Count(params bool[] values) => values.Count(value => value);
}
