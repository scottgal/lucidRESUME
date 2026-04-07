using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.GitHub.Models;

public sealed class GitHubImportResult
{
    public string Username { get; init; } = "";
    public int ReposAnalysed { get; init; }
    public int ReposSkipped { get; init; }
    public List<SkillLedgerEntry> SkillEntries { get; init; } = [];
    public List<Project> Projects { get; init; } = [];
    public List<GitHubProjectProfile> ProjectProfiles { get; init; } = [];
    public GitHubProfile? Profile { get; init; }
    public List<string> Warnings { get; init; } = [];
}
