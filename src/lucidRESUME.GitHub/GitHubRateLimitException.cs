namespace lucidRESUME.GitHub;

public sealed class GitHubRateLimitException : Exception
{
    public DateTimeOffset ResetsAt { get; }

    public GitHubRateLimitException(DateTimeOffset resetsAt)
        : base($"GitHub API rate limit exceeded. Resets at {resetsAt:HH:mm:ss UTC}")
    {
        ResetsAt = resetsAt;
    }
}
