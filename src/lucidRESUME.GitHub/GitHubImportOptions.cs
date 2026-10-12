namespace lucidRESUME.GitHub;

public sealed class GitHubImportOptions
{
    /// <summary>
    /// Allow fork contents to nominate personal skill and project evidence. Forks are
    /// always visible in the audit, but are observation-only by default.
    /// </summary>
    public bool IncludeForks { get; set; }
    public int MinRepoSizeKb { get; set; } = 5;
    public double MinLanguageFraction { get; set; } = 0.05;
    /// <summary>
    /// Maximum number of repositories for which a revision-pinned recursive tree is
    /// inspected. Basic metadata, languages, and README analysis still apply to the rest.
    /// </summary>
    public int DeepAnalysisMaxRepositories { get; set; } = 20;
    /// <summary>
    /// Maximum number of recent repositories whose README is locally summarised.
    /// All eligible repositories still receive metadata and language observations.
    /// </summary>
    public int ReadmeAnalysisMaxRepositories { get; set; } = 20;
    public int ContributionAnalysisMaxRepositories { get; set; } = 50;
    public string? PersonalAccessToken { get; set; }
}
