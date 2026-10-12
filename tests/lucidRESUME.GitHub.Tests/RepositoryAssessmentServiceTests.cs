using lucidRESUME.GitHub;
using lucidRESUME.GitHub.Models;

namespace lucidRESUME.GitHub.Tests;

public sealed class RepositoryAssessmentServiceTests
{
    [Fact]
    public void Sustained_original_product_records_explainable_engineering_signals()
    {
        var repo = Repository("product", fork: false, size: 42_000,
            created: DateTimeOffset.UtcNow.AddYears(-2), pushed: DateTimeOffset.UtcNow.AddDays(-5));
        var tree = Tree(
            "src/Product/Product.csproj",
            "tests/Product.Tests/ProductTests.cs",
            ".github/workflows/ci.yml",
            ".github/workflows/release.yml",
            "docs/design.md",
            "README.md");

        var (signals, assessment, classification) =
            RepositoryAssessmentService.Assess(repo, tree, hasReadme: true, skillCount: 8);

        Assert.Equal("sustained-product", classification);
        Assert.True(signals.HasTests);
        Assert.True(signals.HasCi);
        Assert.True(signals.HasReleaseAutomation);
        Assert.True(signals.HasPackageManifests);
        Assert.True(signals.HasDocumentation);
        Assert.True(assessment.EvidenceStrength > .65);
        Assert.Contains(assessment.Reasons, reason => reason.Contains("activity spans"));
    }

    [Fact]
    public void Fork_is_observed_but_heavily_discounted_for_personal_evidence()
    {
        var original = Repository("original", fork: false, size: 20_000,
            created: DateTimeOffset.UtcNow.AddYears(-1), pushed: DateTimeOffset.UtcNow);
        var fork = Repository("fork", fork: true, size: 20_000,
            created: original.CreatedAt, pushed: original.PushedAt);
        var tree = Tree("src/App.csproj", "tests/AppTests.cs", ".github/workflows/release.yml", "README.md");

        var originalAssessment = RepositoryAssessmentService.Assess(original, tree, true, 5).Assessment;
        var (signals, forkAssessment, classification) = RepositoryAssessmentService.Assess(fork, tree, true, 5);

        Assert.Equal("fork", classification);
        Assert.True(signals.HasTests);
        Assert.True(forkAssessment.EvidenceStrength < originalAssessment.EvidenceStrength * .25);
        Assert.Contains(forkAssessment.Reasons, reason => reason.Contains("authorship"));
    }

    [Fact]
    public void Browser_extension_is_detected_from_manifest_and_runtime_structure()
    {
        var repo = Repository("extension", fork: false, size: 400,
            created: DateTimeOffset.UtcNow.AddMonths(-8), pushed: DateTimeOffset.UtcNow.AddDays(-2));
        var tree = Tree("extension/manifest.json", "extension/service-worker.js", "extension/sidepanel.ts");

        var (signals, _, classification) = RepositoryAssessmentService.Assess(repo, tree, true, 3);

        Assert.True(signals.HasBrowserExtension);
        Assert.Equal("browser-extension-product", classification);
    }

    [Fact]
    public void Archived_repository_remains_historical_evidence_and_records_its_state()
    {
        var observedAt = new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);
        var repo = Repository("old-product", fork: false, size: 20_000,
            created: observedAt.AddYears(-5), pushed: observedAt.AddYears(-2));
        repo.Archived = true;

        var (_, assessment, classification) = RepositoryAssessmentService.Assess(
            repo, Tree("src/App.csproj", "tests/AppTests.cs"), true, 3, observedAt);

        Assert.Equal("sustained-product", classification);
        Assert.Contains(assessment.Reasons, reason => reason.Contains("archived", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Main_contributor_ranks_above_a_small_contribution_to_the_same_project()
    {
        var repo = Repository("product", false, 20000, DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow);
        var tree = Tree("src/App.csproj", "tests/AppTests.cs", ".github/workflows/ci.yml");
        var main = RepositoryContribution.Assess("alex", [new GitHubContributor { Login = "alex", Contributions = 1000 },
            new GitHubContributor { Login = "other", Contributions = 5 }], true);
        var minor = RepositoryContribution.Assess("other", [new GitHubContributor { Login = "alex", Contributions = 1000 },
            new GitHubContributor { Login = "other", Contributions = 5 }], true);
        var primary = RepositoryAssessmentService.Assess(repo, tree, true, 8, contribution: main).Assessment;
        var secondary = RepositoryAssessmentService.Assess(repo, tree, true, 8, contribution: minor).Assessment;
        Assert.True(primary.EvidenceStrength > secondary.EvidenceStrength);
        Assert.Contains(primary.Reasons, reason => reason.Contains("not quality or years"));
    }

    private static GitHubRepo Repository(string name, bool fork, int size,
        DateTimeOffset created, DateTimeOffset pushed) => new()
        {
            Name = name,
            Fork = fork,
            Size = size,
            CreatedAt = created,
            PushedAt = pushed,
            Description = "Example repository"
        };

    private static GitHubTree Tree(params string[] paths) => new()
    {
        Sha = new string('a', 40),
        Entries = paths.Select(path => new GitHubTreeEntry { Path = path, Type = "blob" }).ToList()
    };
}
