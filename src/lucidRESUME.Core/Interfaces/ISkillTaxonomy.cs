namespace lucidRESUME.Core.Interfaces;

/// <summary>
/// Provides skill taxonomy lookups — finding known skills in text and role archetype matching.
/// Implemented by SkillTaxonomyService in Matching, consumed by JD parser in JobSpec.
/// </summary>
public interface ISkillTaxonomy
{
    /// <summary>All unique skill names in the taxonomy.</summary>
    IReadOnlySet<string> AllSkills { get; }

    /// <summary>All role archetype names (e.g. "Backend Developer", "DevOps Engineer").</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// Finds known skills in text using exact match against the taxonomy.
    /// Fast — no embeddings needed. Case-insensitive with word boundary checking.
    /// </summary>
    List<string> FindSkillsExact(string text);

    /// <summary>
    /// Computes how well a set of skills matches each role archetype.
    /// Returns roles sorted by match percentage (descending).
    /// </summary>
    List<(string Role, double MatchPercent, int Matched, int Total)> GetArchetypeMatches(
        IReadOnlyCollection<string> candidateSkills);
}
