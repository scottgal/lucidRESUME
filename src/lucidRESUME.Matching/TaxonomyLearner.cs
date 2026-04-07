using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching;

/// <summary>
/// Learns skill relationships from ingested resumes and job descriptions.
/// Builds a "learned" taxonomy file that supplements the static ones.
///
/// Learning signals:
/// 1. Skills that co-occur in the same role/JD → likely related
/// 2. Skills listed in the same skills section group → likely in same domain
/// 3. NER entities classified as the same type → related terms
/// 4. LLM extraction: when the LLM maps a JD requirement to a resume skill,
///    that mapping is a learned alias
///
/// The learned taxonomy is persisted to disk and grows over time.
/// </summary>
public sealed class TaxonomyLearner
{
    private readonly string _learnedPath;
    private readonly Dictionary<string, HashSet<string>> _learned = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;

    public TaxonomyLearner(string? appDataDir = null)
    {
        var dir = appDataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "lucidRESUME");
        Directory.CreateDirectory(dir);
        _learnedPath = Path.Combine(dir, "learned-taxonomy.txt");
        Load();
    }

    /// <summary>
    /// Learn skill relationships from a resume's experience.
    /// Skills that appear in the same role are likely related.
    /// </summary>
    public void LearnFromResume(ResumeDocument resume)
    {
        // Co-occurrence: technologies used in the same role
        foreach (var exp in resume.Experience)
        {
            if (exp.Technologies.Count < 2) continue;
            var techs = exp.Technologies.Take(20).ToList();
            for (int i = 0; i < techs.Count; i++)
            {
                for (int j = i + 1; j < techs.Count; j++)
                {
                    LearnRelation(techs[i], techs[j]);
                }
            }
        }

        // Skills section groups — skills listed together are related
        var skillNames = resume.Skills.Select(s => s.Name).Take(30).ToList();
        for (int i = 0; i < skillNames.Count; i++)
        {
            for (int j = i + 1; j < Math.Min(i + 5, skillNames.Count); j++)
            {
                LearnRelation(skillNames[i], skillNames[j]);
            }
        }
    }

    /// <summary>
    /// Learn from a JD's extracted requirements.
    /// Skills listed together in the same JD are domain-related.
    /// </summary>
    public void LearnFromJdRequirements(IReadOnlyList<JdSkillRequirement> requirements)
    {
        var skills = requirements.Select(r => r.SkillName).Take(20).ToList();
        for (int i = 0; i < skills.Count; i++)
        {
            for (int j = i + 1; j < Math.Min(i + 5, skills.Count); j++)
            {
                LearnRelation(skills[i], skills[j]);
            }
        }
    }

    /// <summary>
    /// Learn that a match was found between a JD skill and a resume skill.
    /// This is the strongest signal — "kubernetes" matched "k8s" is a direct alias.
    /// </summary>
    public void LearnMatchAlias(string jdSkill, string resumeSkill)
    {
        if (string.Equals(jdSkill, resumeSkill, StringComparison.OrdinalIgnoreCase)) return;
        LearnRelation(jdSkill, resumeSkill);
    }

    /// <summary>Get all learned relations for a term.</summary>
    public IReadOnlyList<string> GetLearnedRelations(string term)
    {
        return _learned.TryGetValue(term.ToLowerInvariant(), out var set)
            ? set.ToList()
            : [];
    }

    /// <summary>Persist if dirty.</summary>
    public void Save()
    {
        if (!_dirty) return;
        var lines = new List<string> { "# Learned skill taxonomy — auto-generated from ingested resumes and JDs" };
        foreach (var (canonical, aliases) in _learned.OrderBy(kv => kv.Key))
        {
            if (aliases.Count > 0)
                lines.Add($"{canonical}: {string.Join(", ", aliases.OrderBy(a => a))}");
        }
        File.WriteAllLines(_learnedPath, lines);
        _dirty = false;
    }

    private void LearnRelation(string a, string b)
    {
        var la = a.ToLowerInvariant().Trim();
        var lb = b.ToLowerInvariant().Trim();
        if (la.Length < 2 || lb.Length < 2 || la == lb) return;

        // Shorter term is canonical (heuristic: abbreviations are short)
        var (canonical, alias) = la.Length <= lb.Length ? (la, lb) : (lb, la);

        if (!_learned.TryGetValue(canonical, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _learned[canonical] = set;
        }
        if (set.Add(alias))
            _dirty = true;
    }

    private void Load()
    {
        if (!File.Exists(_learnedPath)) return;
        foreach (var line in File.ReadLines(_learnedPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0) continue;

            var canonical = trimmed[..colonIdx].Trim().ToLowerInvariant();
            var aliases = trimmed[(colonIdx + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim().ToLowerInvariant())
                .Where(a => a.Length > 0);

            if (!_learned.TryGetValue(canonical, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _learned[canonical] = set;
            }
            foreach (var alias in aliases)
                set.Add(alias);
        }
    }
}
