using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;
using lucidRESUME.GitHub.Models;
using lucidRESUME.Matching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mostlylucid.DocSummarizer;

namespace lucidRESUME.GitHub;

/// <summary>
/// Imports skills from a GitHub user's public repositories.
/// Extracts evidence from repo languages (weighted by bytes), topics, and descriptions.
/// </summary>
public sealed class GitHubSkillImporter
{
    private readonly GitHubApiClient _client;
    private readonly GitHubImportOptions _options;
    private readonly ReadmeSkillExtractor _readmeExtractor;
    private readonly ILogger<GitHubSkillImporter> _logger;

    public GitHubSkillImporter(GitHubApiClient client, IOptions<GitHubImportOptions> options,
        IDocumentSummarizer summarizer, ILogger<GitHubSkillImporter> logger)
    {
        _client = client;
        _options = options.Value;
        _readmeExtractor = new ReadmeSkillExtractor(summarizer);
        _logger = logger;
    }

    public async Task<GitHubImportResult> ImportAsync(string username, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var skillMap = new Dictionary<string, SkillLedgerEntry>(StringComparer.OrdinalIgnoreCase);
        var projects = new List<Project>();
        var profiles = new List<GitHubProjectProfile>();

        // Fetch profile
        GitHubProfile? profile = null;
        try
        {
            profile = await _client.GetUserProfileAsync(username, ct);
        }
        catch (Exception ex) when (ex is not GitHubRateLimitException)
        {
            warnings.Add($"Could not fetch profile: {ex.Message}");
        }

        // Fetch repos
        var allRepos = await _client.GetUserReposAsync(username, ct);
        var skipped = 0;

        foreach (var repo in allRepos)
        {
            if (repo.Fork && !_options.IncludeForks) { skipped++; continue; }
            if (repo.Size < _options.MinRepoSizeKb) { skipped++; continue; }
            if (repo.Archived) { skipped++; continue; }

            // Check rate budget: need 2 calls per repo (languages + readme)
            Dictionary<string, long>? languages = null;
            try
            {
                _client.EnsureBudget(2);
                languages = await _client.GetRepoLanguagesAsync(username, repo.Name, ct);
            }
            catch (GitHubRateLimitException ex)
            {
                warnings.Add($"Rate limit: {_client.RemainingRequests} requests left. Stopped at '{repo.Name}'. Resets at {ex.ResetsAt:HH:mm UTC}.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch languages for {Repo}", repo.Name);
            }

            // Extract skills from languages
            if (languages is { Count: > 0 })
            {
                var totalBytes = languages.Values.Sum();
                foreach (var (lang, bytes) in languages)
                {
                    var fraction = (double)bytes / totalBytes;
                    if (fraction < _options.MinLanguageFraction) continue;

                    var canonical = GitHubLanguageMap.ToCanonical(lang);
                    var entry = GetOrCreate(skillMap, canonical);
                    entry.Evidence.Add(new SkillEvidence
                    {
                        Company = repo.Name,
                        SourceText = $"{lang} ({fraction:P0} of {repo.Name})",
                        StartDate = DateOnly.FromDateTime(repo.CreatedAt.DateTime),
                        EndDate = DateOnly.FromDateTime(repo.PushedAt.DateTime),
                        Source = EvidenceSource.GitHubRepository,
                        Confidence = ComputeConfidence(fraction, repo),
                    });
                }
            }

            // Extract skills from topics
            foreach (var topic in repo.Topics)
            {
                var canonical = SkillTaxonomy.Canonicalize(topic) ?? topic.ToLowerInvariant();
                var entry = GetOrCreate(skillMap, canonical);
                if (!entry.Evidence.Any(e => e.Company == repo.Name && e.Source == EvidenceSource.GitHubRepository))
                {
                    entry.Evidence.Add(new SkillEvidence
                    {
                        Company = repo.Name,
                        SourceText = $"Topic '{topic}' on {repo.Name}",
                        StartDate = DateOnly.FromDateTime(repo.CreatedAt.DateTime),
                        EndDate = DateOnly.FromDateTime(repo.PushedAt.DateTime),
                        Source = EvidenceSource.GitHubRepository,
                        Confidence = 0.7,
                    });
                }
            }

            // Extract skills from README via lucidRAG DocSummarizer
            string? readme = null;
            ReadmeExtractionResult? readmeResult = null;
            try
            {
                readme = await _client.GetRepoReadmeAsync(username, repo.Name, ct);
                if (readme is { Length: > 100 })
                {
                    readmeResult = await _readmeExtractor.ExtractAsync(readme, repo.Name, ct);
                    foreach (var (skill, confidence) in readmeResult.Skills)
                    {
                        var entry = GetOrCreate(skillMap, skill);
                        if (!entry.Evidence.Any(e => e.Company == repo.Name && e.SourceText.Contains("README")))
                        {
                            entry.Evidence.Add(new SkillEvidence
                            {
                                Company = repo.Name,
                                SourceText = $"README of {repo.Name}",
                                StartDate = DateOnly.FromDateTime(repo.CreatedAt.DateTime),
                                EndDate = DateOnly.FromDateTime(repo.PushedAt.DateTime),
                                Source = EvidenceSource.GitHubRepository,
                                Confidence = confidence,
                            });
                        }
                    }
                }
            }
            catch (GitHubRateLimitException ex)
            {
                warnings.Add($"Rate limited fetching README for '{repo.Name}'. Resets at {ex.ResetsAt:HH:mm UTC}.");
                // Continue to next repo — we already got languages
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to process README for {Repo}", repo.Name);
            }

            // Build Project entry
            var technologies = new List<string>();
            if (languages != null)
                technologies.AddRange(languages.Keys.Select(GitHubLanguageMap.ToCanonical).Distinct());
            technologies.AddRange(repo.Topics);

            // Use lucidRAG summary or first paragraph of README as description
            var description = repo.Description;
            if (string.IsNullOrEmpty(description) && readmeResult?.Summary != null)
            {
                var summary = readmeResult.Summary;
                description = summary.Length > 300 ? summary[..300] + "..." : summary;
            }

            var distinctTechs = technologies.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            projects.Add(new Project
            {
                Name = repo.Name,
                Description = description,
                Technologies = distinctTechs,
                Url = repo.HtmlUrl,
                Date = DateOnly.FromDateTime(repo.PushedAt.DateTime),
            });

            // Build per-project profile
            var languageWeights = new List<LanguageWeight>();
            if (languages is { Count: > 0 })
            {
                var totalBytes = languages.Values.Sum();
                languageWeights = languages
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => new LanguageWeight(kv.Key, GitHubLanguageMap.ToCanonical(kv.Key), (double)kv.Value / totalBytes))
                    .ToList();
            }

            // Collect all skills this repo contributes to the ledger
            var repoSkills = skillMap.Values
                .Where(e => e.Evidence.Any(ev => ev.Company == repo.Name))
                .Select(e => e.SkillName)
                .ToList();

            var readmeSkillNames = readmeResult?.Skills.Select(s => s.Skill).ToList() ?? [];

            profiles.Add(new GitHubProjectProfile
            {
                Name = repo.Name,
                Description = description,
                Summary = readmeResult?.Summary,
                Url = repo.HtmlUrl,
                Stars = repo.StargazersCount,
                SizeKb = repo.Size,
                IsFork = repo.Fork,
                Created = DateOnly.FromDateTime(repo.CreatedAt.DateTime),
                LastActive = DateOnly.FromDateTime(repo.PushedAt.DateTime),
                PrimaryLanguage = repo.Language,
                Languages = languageWeights,
                Topics = repo.Topics.ToList(),
                Skills = repoSkills,
                ReadmeSkills = readmeSkillNames,
                EvidenceStrength = ComputeRepoStrength(repo, languageWeights.Count, repoSkills.Count),
            });
        }

        // Categorise and calculate years
        foreach (var entry in skillMap.Values)
        {
            CalculateYears(entry);
            entry.Category ??= SkillCategoriser.CategoriseSkill(entry.SkillName);
        }

        var entries = skillMap.Values
            .OrderByDescending(e => e.Strength)
            .ToList();

        return new GitHubImportResult
        {
            Username = username,
            ReposAnalysed = allRepos.Count - skipped,
            ProjectProfiles = profiles.OrderByDescending(p => p.EvidenceStrength).ToList(),
            ReposSkipped = skipped,
            SkillEntries = entries,
            Projects = projects,
            Profile = profile,
            Warnings = warnings,
        };
    }

    private static SkillLedgerEntry GetOrCreate(Dictionary<string, SkillLedgerEntry> map, string skillName)
    {
        if (!map.TryGetValue(skillName, out var entry))
        {
            entry = new SkillLedgerEntry { SkillName = skillName };
            map[skillName] = entry;
        }
        return entry;
    }

    private static double ComputeRepoStrength(GitHubRepo repo, int languageCount, int skillCount)
    {
        var sizeFactor = Math.Min(repo.Size / 10_000.0, 1.0);
        var skillFactor = Math.Min(skillCount / 10.0, 1.0);
        var starFactor = Math.Min(repo.StargazersCount / 20.0, 1.0);
        var daysSincePush = (DateTimeOffset.UtcNow - repo.PushedAt).TotalDays;
        var recencyFactor = daysSincePush < 365 ? 1.0 : daysSincePush < 730 ? 0.7 : 0.4;

        return (sizeFactor * 0.25 + skillFactor * 0.3 + starFactor * 0.2 + recencyFactor * 0.25);
    }

    private static double ComputeConfidence(double languageFraction, GitHubRepo repo)
    {
        // Base: how dominant is this language in the repo?
        var fractionScore = Math.Min(languageFraction * 1.5, 1.0);

        // Bonus for recent activity
        var daysSincePush = (DateTimeOffset.UtcNow - repo.PushedAt).TotalDays;
        var recencyBonus = daysSincePush < 365 ? 0.1 : 0.0;

        // Bonus for repo size (proxy for effort)
        var sizeBonus = repo.Size > 10_000 ? 0.1 : repo.Size > 1_000 ? 0.05 : 0.0;

        return Math.Min(fractionScore + recencyBonus + sizeBonus, 0.95);
    }

    private static void CalculateYears(SkillLedgerEntry entry)
    {
        var datedEvidence = entry.Evidence
            .Where(e => e.StartDate.HasValue)
            .OrderBy(e => e.StartDate)
            .ToList();

        if (datedEvidence.Count == 0) return;

        entry.FirstSeen = datedEvidence.First().StartDate;
        entry.LastSeen = datedEvidence.Last().EndDate;
        entry.IsCurrent = datedEvidence.Any(e =>
            e.EndDate.HasValue &&
            (DateOnly.FromDateTime(DateTime.Today).DayNumber - e.EndDate.Value.DayNumber) < 180);

        var ranges = datedEvidence
            .Select(e => (
                start: e.StartDate!.Value.DayNumber,
                end: (e.EndDate ?? DateOnly.FromDateTime(DateTime.Today)).DayNumber))
            .OrderBy(r => r.start)
            .ToList();

        var merged = new List<(int start, int end)>();
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && range.start <= merged[^1].end)
                merged[^1] = (merged[^1].start, Math.Max(merged[^1].end, range.end));
            else
                merged.Add(range);
        }

        entry.CalculatedYears = merged.Sum(r => (r.end - r.start) / 365.25);
    }
}
