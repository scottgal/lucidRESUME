using System.Text.Json.Serialization;

namespace lucidRESUME.GitHub.Models;

public sealed class GitHubContributor
{
    [JsonPropertyName("login")] public string? Login { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("contributions")] public long Contributions { get; set; }
}

public sealed class RepositoryContribution
{
    public bool Observed { get; init; }
    public bool Complete { get; init; }
    public long CandidateCommits { get; init; }
    public long ObservedCommits { get; init; }
    public int? CandidateRank { get; init; }
    public bool? IsMainContributor { get; init; }
    public double? ObservedCommitShare => ObservedCommits > 0 ? (double)CandidateCommits / ObservedCommits : null;

    public static RepositoryContribution Assess(string username, IReadOnlyList<GitHubContributor> contributors, bool complete)
    {
        var humans = contributors.Where(c => c.Type != "Bot" && c.Login?.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) != true).ToList();
        var candidate = humans.Where(c => string.Equals(c.Login, username, StringComparison.OrdinalIgnoreCase)).Sum(c => c.Contributions);
        var highest = humans.Count == 0 ? 0 : humans.Max(c => c.Contributions);
        return new RepositoryContribution
        {
            Observed = true,
            Complete = complete,
            CandidateCommits = candidate,
            ObservedCommits = humans.Sum(c => c.Contributions),
            CandidateRank = candidate > 0 ? 1 + humans.Count(c => c.Contributions > candidate) : null,
            IsMainContributor = complete ? candidate > 0 && candidate == highest : null
        };
    }
}
