using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.JobSearch;

public sealed class RoleSuggestionService
{
    private static readonly HashSet<string> SkillCategories =
        new(StringComparer.OrdinalIgnoreCase) { "Language", "Framework" };

    public IReadOnlyList<JobSearchQuery> GenerateQueries(ResumeDocument resume, UserProfile profile)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queries = new List<JobSearchQuery>();

        bool remoteOnly = profile.Preferences.OpenToRemote && !profile.Preferences.OpenToOnsite;

        JobSearchQuery MakeQuery(string keywords) =>
            new(keywords, RemoteOnly: remoteOnly ? true : null);

        void TryAdd(string keywords)
        {
            var key = keywords.ToLowerInvariant();
            if (seen.Add(key))
                queries.Add(MakeQuery(keywords));
        }

        // 1. Target roles from profile preferences
        foreach (var role in profile.Preferences.TargetRoles)
        {
            if (!string.IsNullOrWhiteSpace(role))
                TryAdd(role.Trim());
        }

        // 2. Most recent job title from resume
        var recentTitle = resume.Experience.FirstOrDefault()?.Title;
        if (!string.IsNullOrWhiteSpace(recentTitle))
            TryAdd(recentTitle.Trim());

        // 3. Top 3 skills where Category is "Language", "Framework", or null
        var skillQueries = resume.Skills
            .Where(s => s.Category is null ||
                        SkillCategories.Contains(s.Category))
            .Take(3)
            .Select(s => $"{s.Name} developer");

        foreach (var sq in skillQueries)
            TryAdd(sq);

        // Return at most 5
        return queries.Take(5).ToList();
    }
}
