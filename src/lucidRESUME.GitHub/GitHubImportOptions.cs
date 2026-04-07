namespace lucidRESUME.GitHub;

public sealed class GitHubImportOptions
{
    public bool IncludeForks { get; set; }
    public int MinRepoSizeKb { get; set; } = 5;
    public double MinLanguageFraction { get; set; } = 0.05;
    public string? PersonalAccessToken { get; set; }
}
