namespace lucidRESUME.GitHub.Models;

/// <summary>
/// Structured profile for a single GitHub repository.
/// Captures technologies, skills extracted, time range, summary, and evidence strength.
/// </summary>
public sealed class GitHubProjectProfile
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? Summary { get; init; }
    public string Url { get; init; } = "";
    public RepositoryContribution Contribution { get; init; } = new();
    public int Stars { get; init; }
    public int SizeKb { get; init; }
    public bool IsFork { get; init; }
    public bool IsArchived { get; init; }
    public bool EligibleForPersonalEvidence { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
    public string RepositoryClass { get; init; } = "repository";
    public string? Revision { get; init; }
    public bool TreeTruncated { get; init; }
    public int FileCount { get; init; }
    public int Forks { get; init; }
    public int OpenIssues { get; init; }
    public string? LicenseSpdx { get; init; }
    public RepositoryEngineeringSignals Engineering { get; init; } = new();
    public RepositoryAssessment Assessment { get; init; } = new();

    /// <summary>When the repo was created.</summary>
    public DateOnly Created { get; init; }

    /// <summary>Last push date (proxy for "last worked on").</summary>
    public DateOnly LastActive { get; init; }

    /// <summary>Primary language by bytes.</summary>
    public string? PrimaryLanguage { get; init; }

    /// <summary>All languages with their byte percentage.</summary>
    public List<LanguageWeight> Languages { get; init; } = [];

    /// <summary>GitHub topics applied to the repo.</summary>
    public List<string> Topics { get; init; } = [];

    /// <summary>Skills extracted from this repo (languages + topics + README).</summary>
    public List<string> Skills { get; init; } = [];

    /// <summary>Skills found specifically in the README via lucidRAG analysis.</summary>
    public List<string> ReadmeSkills { get; init; } = [];

    /// <summary>Composite evidence strength: how strongly does this repo demonstrate skills?</summary>
    public double EvidenceStrength { get; init; }

    /// <summary>Duration in years from created to last active.</summary>
    public double ActiveYears => Math.Max(0, (LastActive.DayNumber - Created.DayNumber) / 365.25);

    /// <summary>Age at the time of observation, kept reproducible after import.</summary>
    public double AgeYears
    {
        get
        {
            var observed = ObservedAt == default ? DateTimeOffset.UtcNow : ObservedAt;
            return Math.Max(0, (DateOnly.FromDateTime(observed.DateTime).DayNumber - Created.DayNumber) / 365.25);
        }
    }

    /// <summary>Whether the repo was active in the last year.</summary>
    public bool IsRecent
    {
        get
        {
            var observed = ObservedAt == default ? DateTimeOffset.UtcNow : ObservedAt;
            return DateOnly.FromDateTime(observed.DateTime).DayNumber - LastActive.DayNumber < 365;
        }
    }
}

public sealed record LanguageWeight(string Language, string Canonical, double Fraction);

public sealed class RepositoryEngineeringSignals
{
    public bool TreeObserved { get; init; }
    public bool HasReadme { get; init; }
    public bool HasTests { get; init; }
    public bool HasCi { get; init; }
    public bool HasReleaseAutomation { get; init; }
    public bool HasPackageManifests { get; init; }
    public bool HasDocumentation { get; init; }
    public bool HasBrowserExtension { get; init; }
    public int ManifestCount { get; init; }
    public int WorkflowCount { get; init; }
    public int TestFileCount { get; init; }
}

public sealed class RepositoryAssessment
{
    public string Method { get; init; } = "lucidresume-repository-assessment";
    public string MethodVersion { get; init; } = "0.1";
    public double Originality { get; init; }
    public double Longevity { get; init; }
    public double EngineeringProcess { get; init; }
    public double Documentation { get; init; }
    public double Delivery { get; init; }
    public double EvidenceStrength { get; init; }
    public List<string> Reasons { get; init; } = [];
}
