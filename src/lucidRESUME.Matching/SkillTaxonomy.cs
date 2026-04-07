namespace lucidRESUME.Matching;

/// <summary>
/// Loads industry skill taxonomies from text files.
/// Each taxonomy maps canonical terms to aliases, enabling the matcher
/// to recognise "k8s" as "kubernetes", "AWS" as "amazon web services", etc.
///
/// Taxonomies are loaded lazily on first use and cached.
/// Auto-detects which taxonomies are relevant based on the skills present.
/// </summary>
public sealed class SkillTaxonomy
{
    private static readonly Lazy<Dictionary<string, TaxonomyFile>> AllTaxonomies = new(LoadAll);

    /// <summary>
    /// Given an alias or canonical term, returns the canonical form.
    /// Returns null if the term isn't in any loaded taxonomy.
    /// </summary>
    public static string? Canonicalize(string term)
    {
        var lower = term.ToLowerInvariant().Trim();
        foreach (var tax in AllTaxonomies.Value.Values)
        {
            if (tax.AliasToCanonical.TryGetValue(lower, out var canonical))
                return canonical;
        }
        return null;
    }

    /// <summary>
    /// Returns all known aliases for a canonical term (including the term itself).
    /// Used by the matcher to expand a JD requirement to all possible resume forms.
    /// </summary>
    public static IReadOnlyList<string> GetAliases(string term)
    {
        var lower = term.ToLowerInvariant().Trim();
        // First canonicalize
        var canonical = Canonicalize(lower) ?? lower;

        var result = new List<string> { canonical };
        foreach (var tax in AllTaxonomies.Value.Values)
        {
            if (tax.CanonicalToAliases.TryGetValue(canonical, out var aliases))
            {
                result.AddRange(aliases);
                break; // found in this taxonomy
            }
        }
        return result;
    }

    /// <summary>
    /// Check if two terms are equivalent according to any taxonomy.
    /// "kubernetes" and "k8s" → true, "python" and "java" → false.
    /// </summary>
    public static bool AreEquivalent(string a, string b)
    {
        var ca = Canonicalize(a.ToLowerInvariant()) ?? a.ToLowerInvariant();
        var cb = Canonicalize(b.ToLowerInvariant()) ?? b.ToLowerInvariant();
        return string.Equals(ca, cb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns all loaded taxonomy names (for diagnostics).</summary>
    public static IReadOnlyList<string> LoadedTaxonomies =>
        AllTaxonomies.Value.Keys.ToList();

    private static Dictionary<string, TaxonomyFile> LoadAll()
    {
        var result = new Dictionary<string, TaxonomyFile>(StringComparer.OrdinalIgnoreCase);

        var taxDir = Path.Combine(AppContext.BaseDirectory, "Resources", "taxonomies");
        if (!Directory.Exists(taxDir))
        {
            var asmDir = Path.GetDirectoryName(typeof(SkillTaxonomy).Assembly.Location)!;
            taxDir = Path.Combine(asmDir, "Resources", "taxonomies");
        }
        if (!Directory.Exists(taxDir)) return result;

        foreach (var file in Directory.GetFiles(taxDir, "*.txt"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var tax = ParseTaxonomyFile(file);
            if (tax.AliasToCanonical.Count > 0)
                result[name] = tax;
        }

        return result;
    }

    private static TaxonomyFile ParseTaxonomyFile(string path)
    {
        var aliasToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var canonicalToAliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;

            var canonical = line[..colonIdx].Trim().ToLowerInvariant();
            var aliasesPart = line[(colonIdx + 1)..];
            var aliases = aliasesPart.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim().ToLowerInvariant())
                .Where(a => a.Length > 0)
                .ToList();

            // Map canonical to itself
            aliasToCanonical.TryAdd(canonical, canonical);

            // Map each alias to the canonical
            foreach (var alias in aliases)
                aliasToCanonical.TryAdd(alias, canonical);

            // Store reverse mapping
            canonicalToAliases[canonical] = aliases;
        }

        return new TaxonomyFile(aliasToCanonical, canonicalToAliases);
    }

    private record TaxonomyFile(
        Dictionary<string, string> AliasToCanonical,
        Dictionary<string, List<string>> CanonicalToAliases);
}
