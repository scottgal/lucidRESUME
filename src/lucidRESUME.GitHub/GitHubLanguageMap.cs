using lucidRESUME.Matching;

namespace lucidRESUME.GitHub;

/// <summary>
/// Maps GitHub API language names to canonical skill taxonomy forms.
/// GitHub uses display names ("C#", "Jupyter Notebook") while the taxonomy uses
/// lowercase canonical forms ("c#", "python").
/// </summary>
internal static class GitHubLanguageMap
{
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C#"] = "c#",
        ["C++"] = "c++",
        ["Jupyter Notebook"] = "python",
        ["Shell"] = "bash",
        ["Dockerfile"] = "docker",
        ["HCL"] = "terraform",
        ["SCSS"] = "css",
        ["Vue"] = "vue.js",
        ["Makefile"] = "make",
        ["PLpgSQL"] = "postgresql",
        ["TSQL"] = "sql server",
        ["Gherkin"] = "bdd",
    };

    public static string ToCanonical(string githubLanguage)
    {
        if (Overrides.TryGetValue(githubLanguage, out var mapped))
            return mapped;
        return SkillTaxonomy.Canonicalize(githubLanguage) ?? githubLanguage.ToLowerInvariant();
    }
}
