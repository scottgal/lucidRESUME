using lucidRESUME.Matching;

namespace lucidRESUME.GitHub;

/// <summary>
/// Maps GitHub API language names to canonical skill taxonomy forms.
/// Loaded from Resources/github-language-map.txt — no hardcoded word lists.
/// </summary>
internal static class GitHubLanguageMap
{
    private static readonly Lazy<Dictionary<string, string>> Overrides = new(LoadMap);

    public static string ToCanonical(string githubLanguage)
    {
        if (Overrides.Value.TryGetValue(githubLanguage, out var mapped))
            return mapped;
        return SkillTaxonomy.Canonicalize(githubLanguage) ?? githubLanguage.ToLowerInvariant();
    }

    private static Dictionary<string, string> LoadMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Try to find the resource file
        foreach (var dir in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(GitHubLanguageMap).Assembly.Location) ?? "" })
        {
            var path = Path.Combine(dir, "Resources", "github-language-map.txt");
            if (!File.Exists(path)) continue;

            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0) continue;
                var github = trimmed[..colonIdx].Trim();
                var canonical = trimmed[(colonIdx + 1)..].Trim();
                map.TryAdd(github, canonical);
            }
            return map;
        }

        return map; // empty if file not found — taxonomy will handle it
    }
}
