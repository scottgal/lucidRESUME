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
    public int Stars { get; init; }
    public int SizeKb { get; init; }
    public bool IsFork { get; init; }

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
    public double ActiveYears => (LastActive.DayNumber - Created.DayNumber) / 365.25;

    /// <summary>Whether the repo was active in the last year.</summary>
    public bool IsRecent => (DateOnly.FromDateTime(DateTime.Today).DayNumber - LastActive.DayNumber) < 365;
}

public sealed record LanguageWeight(string Language, string Canonical, double Fraction);
