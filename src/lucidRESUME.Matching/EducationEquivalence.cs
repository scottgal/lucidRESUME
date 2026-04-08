using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching;

/// <summary>
/// Cross-cultural education equivalence engine.
/// Maps qualifications from any country to ISCED levels.
/// Maps universities to tiers based on prestige group membership.
/// Prevents Leiden communities from clustering by culture instead of actual qualification.
/// </summary>
public sealed class EducationEquivalence
{
    private static readonly Lazy<EducationEquivalence> Instance = new(Load);

    // ISCED level → list of (country, qualification name)
    private readonly Dictionary<int, List<(string Country, string Qualification)>> _qualifications = [];

    // University name (lowered) → (tier, group)
    private readonly Dictionary<string, (int Tier, string Group)> _universityTiers = new(StringComparer.OrdinalIgnoreCase);

    public static EducationEquivalence Default => Instance.Value;

    /// <summary>
    /// Get the ISCED level for a qualification string by matching against known patterns.
    /// Returns -1 if unknown.
    /// </summary>
    public int GetIscedLevel(string? qualification)
    {
        if (string.IsNullOrWhiteSpace(qualification)) return -1;
        var lower = qualification.ToLowerInvariant();

        foreach (var (level, entries) in _qualifications)
        {
            if (entries.Any(e => lower.Contains(e.Qualification.ToLowerInvariant())))
                return level;
        }

        // Fall back to the enum-based classifier
        var enumLevel = EducationLevelClassifier.Classify(qualification);
        return enumLevel switch
        {
            EducationLevel.SecondarySchool => 3,
            EducationLevel.PostSecondary => 4,
            EducationLevel.Vocational => 5,
            EducationLevel.Associate => 5,
            EducationLevel.Bachelors => 6,
            EducationLevel.PostGradDiploma => 7,
            EducationLevel.Masters => 7,
            EducationLevel.Doctoral => 8,
            EducationLevel.PostDoctoral => 8,
            _ => -1,
        };
    }

    /// <summary>
    /// Get the university tier (1=globally elite, 2=nationally top, 3=well-regarded).
    /// Returns (tier, group name) or (3, "Accredited") if not in a known prestige group.
    /// </summary>
    public (int Tier, string Group) GetUniversityTier(string? institutionName)
    {
        if (string.IsNullOrWhiteSpace(institutionName)) return (3, "Unknown");

        if (_universityTiers.TryGetValue(institutionName, out var result))
            return result;

        // Try partial match
        var lower = institutionName.ToLowerInvariant();
        foreach (var (name, tier) in _universityTiers)
        {
            if (lower.Contains(name.ToLowerInvariant()) || name.ToLowerInvariant().Contains(lower))
                return tier;
        }

        return (3, "Accredited");
    }

    /// <summary>
    /// Check if a candidate's education meets a JD's requirement.
    /// Uses ISCED levels for cross-cultural comparison.
    /// </summary>
    public bool MeetsRequirement(Education candidate, string jdRequirement)
    {
        var candidateLevel = GetIscedLevel(candidate.Degree);
        var requiredLevel = GetIscedLevel(jdRequirement);
        if (candidateLevel < 0 || requiredLevel < 0) return true; // can't determine, assume OK
        return candidateLevel >= requiredLevel;
    }

    private static EducationEquivalence Load()
    {
        var eq = new EducationEquivalence();

        // Load qualification equivalence
        var qualPath = FindResource("education/qualification-equivalence.txt");
        if (qualPath != null)
        {
            foreach (var line in File.ReadLines(qualPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                var parts = trimmed.Split(':', 3);
                if (parts.Length < 3 || !int.TryParse(parts[0], out var level)) continue;
                var country = parts[1].Trim();
                var qual = parts[2].Trim();
                if (!eq._qualifications.TryGetValue(level, out var list))
                    eq._qualifications[level] = list = [];
                list.Add((country, qual));
            }
        }

        // Load university tiers
        var tierPath = FindResource("education/university-tiers.txt");
        if (tierPath != null)
        {
            foreach (var line in File.ReadLines(tierPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                var parts = trimmed.Split(':', 3);
                if (parts.Length < 3 || !int.TryParse(parts[0], out var tier)) continue;
                var group = parts[1].Trim();
                var name = parts[2].Trim();
                eq._universityTiers.TryAdd(name, (tier, group));
            }
        }

        return eq;
    }

    private static string? FindResource(string relativePath)
    {
        var baseDir = Path.Combine(AppContext.BaseDirectory, "Resources", relativePath);
        if (File.Exists(baseDir)) return baseDir;
        var asmDir = Path.GetDirectoryName(typeof(EducationEquivalence).Assembly.Location);
        if (asmDir != null)
        {
            var asmPath = Path.Combine(asmDir, "Resources", relativePath);
            if (File.Exists(asmPath)) return asmPath;
        }
        return null;
    }
}
