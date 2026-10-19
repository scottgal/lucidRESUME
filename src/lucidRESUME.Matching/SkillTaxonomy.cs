namespace lucidRESUME.Matching;

/// <summary>
/// Loads industry skill taxonomies from text files.
/// Legacy domain files retain related terms for discovery; only equivalences.txt enables the matcher
/// to recognise "k8s" as "kubernetes", "AWS" as "amazon web services", etc.
///
/// Taxonomies are loaded lazily on first use and cached.
/// Auto-detects which taxonomies are relevant based on the skills present.
/// </summary>
public sealed class SkillTaxonomy
{
    private static readonly Lazy<TaxonomyFile> Equivalences = new(() =>
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Resources", "taxonomies");
        if (!Directory.Exists(directory))
            directory = Path.Combine(Path.GetDirectoryName(typeof(SkillTaxonomy).Assembly.Location)!, "Resources", "taxonomies");
        var path = Path.Combine(directory, "equivalences.txt");
        if (!File.Exists(path))
            throw new FileNotFoundException("The audited skill equivalences asset is missing.", path);
        return ParseTaxonomyFile(path, strict: true);
    });

    private static readonly Lazy<Dictionary<string, TaxonomyFile>> AllTaxonomies = new(LoadAll);

    /// <summary>
    /// Given an alias or canonical term, returns the canonical form.
    /// Returns null if the term isn't in any loaded taxonomy.
    /// </summary>
    public static string? Canonicalize(string term)
    {
        var lower = term.ToLowerInvariant().Trim();
        // A canonical name takes precedence over the same word used as an alias
        // in another taxonomy (for example, Salesforce is also a CRM alias).
        if (Equivalences.Value.AliasToCanonical.TryGetValue(lower, out var equivalent))
            return equivalent;
        foreach (var tax in AllTaxonomies.Value.Values)
            if (tax.CanonicalToAliases.ContainsKey(lower)) return lower;
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
        if (Equivalences.Value.CanonicalToAliases.TryGetValue(canonical, out var aliases))
            result.AddRange(aliases);
        return result;
    }

    /// <summary>
    /// Check if two terms are identical or audited synonyms.
    /// "kubernetes" and "k8s" → true, "python" and "java" → false.
    /// </summary>
    public static bool AreEquivalent(string a, string b)
    {
        var ca = Canonicalize(a) ?? a.Trim().ToLowerInvariant();
        var cb = Canonicalize(b) ?? b.Trim().ToLowerInvariant();
        return string.Equals(ca, cb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds canonical concepts whose canonical name or curated alias occurs as a
    /// complete phrase in evidence text. This is deterministic vocabulary lookup,
    /// not semantic inference.
    /// </summary>
    public static IReadOnlySet<string> FindCanonicalMentions(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var phrases = AllTaxonomies.Value.Values.SelectMany(t => t.CanonicalToAliases.Keys)
            .Concat(Equivalences.Value.AliasToCanonical.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var phrase in phrases)
            if (ContainsPhrase(text, phrase)) result.Add(Canonicalize(phrase) ?? phrase);
        return result;
    }

    /// <summary>Associations are suggestions only; they never establish equivalence.</summary>
    public static bool AreRelated(string a, string b)
    {
        if (AreEquivalent(a, b)) return false;
        var left = Canonicalize(a) ?? a.Trim().ToLowerInvariant();
        var right = Canonicalize(b) ?? b.Trim().ToLowerInvariant();
        return AllTaxonomies.Value.Values.Any(t =>
            t.CanonicalToAliases.Any(pair =>
                (pair.Key == left && pair.Value.Contains(right, StringComparer.OrdinalIgnoreCase)) ||
                (pair.Key == right && pair.Value.Contains(left, StringComparer.OrdinalIgnoreCase))));
    }

    public static bool ContainsPhrase(string text, string phrase) =>
        !string.IsNullOrWhiteSpace(phrase) && System.Text.RegularExpressions.Regex.IsMatch(text,
            @"(?<![\p{L}\p{N}+#])" + System.Text.RegularExpressions.Regex.Escape(phrase.Trim()) + @"(?![\p{L}\p{N}+#])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool MentionsEquivalent(string text, string skill) =>
        GetAliases(skill).Any(alias => Core.Models.Evidence.EvidenceTerms.HasPositiveMention(text, alias)) ||
        Core.Models.Evidence.EvidenceTerms.Mentions(text, skill);

    /// <summary>Returns all loaded taxonomy names (for diagnostics).</summary>
    public static IReadOnlyList<string> LoadedTaxonomies =>
        AllTaxonomies.Value.Keys.ToList();

    /// <summary>Return every taxonomy containing a canonical skill.</summary>
    public static IReadOnlyList<string> FindDomains(string canonical) => AllTaxonomies.Value
        .Where(pair => pair.Value.CanonicalToAliases.ContainsKey(canonical.ToLowerInvariant().Trim()))
        .Select(pair => pair.Key)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();

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

        foreach (var file in Directory.GetFiles(taxDir, "*.txt").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name == "equivalences") continue;
            var tax = ParseTaxonomyFile(file);
            if (tax.AliasToCanonical.Count > 0)
                result[name] = tax;
        }

        return result;
    }

    private static TaxonomyFile ParseTaxonomyFile(string path, bool strict = false)
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

            void Register(string term)
            {
                if (strict && aliasToCanonical.TryGetValue(term, out var existing) && existing != canonical)
                    throw new InvalidDataException($"Ambiguous skill equivalence '{term}': '{existing}' and '{canonical}'.");
                aliasToCanonical.TryAdd(term, canonical);
            }
            Register(canonical);

            // Map each alias to the canonical
            foreach (var alias in aliases)
                Register(alias);

            // Store reverse mapping
            canonicalToAliases[canonical] = aliases;
        }

        return new TaxonomyFile(aliasToCanonical, canonicalToAliases);
    }

    private record TaxonomyFile(
        Dictionary<string, string> AliasToCanonical,
        Dictionary<string, List<string>> CanonicalToAliases);
}
