namespace lucidRESUME.Matching;

/// <summary>
/// Audits immutable product data required by import and matching. User databases, resumes,
/// credentials and generated ledgers are deliberately outside this inventory.
/// </summary>
public static class ProductAssetInventory
{
    public static readonly string[] RequiredRelativePaths =
    [
        "Resources/taxonomies/skill-taxonomy.txt",
        "Resources/taxonomies/skill-priorities.txt",
        "Resources/taxonomies/role-archetypes.txt",
        "Resources/github-language-map.txt",
        "Resources/entities-companies.txt",
        "Resources/entities-industries.txt",
        "Resources/entities-locations.txt",
        "Resources/entities-titles.txt",
        "Resources/stopwords.txt",
        "Resources/strong-verbs.txt",
        "Resources/weak-verbs.txt",
        "Resources/verb-replacements.txt",
        "Resources/buzzwords.txt",
        "Resources/fillers.txt",
        "Resources/dictionaries/en_US.aff",
        "Resources/dictionaries/en_US.dic"
    ];

    public static ProductAssetAudit Audit(string baseDirectory)
    {
        var roots = CandidateRoots(baseDirectory).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in RequiredRelativePaths)
        {
            var path = roots.Select(root => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)))
                .FirstOrDefault(File.Exists);
            if (path is not null) resolved[relative] = path;
        }

        return new ProductAssetAudit(
            RequiredRelativePaths.Where(x => !resolved.ContainsKey(x)).ToList(),
            resolved,
            resolved.Values.Sum(path => new FileInfo(path).Length));
    }

    internal static IReadOnlyList<string> ResolveTaxonomyFiles(string baseDirectory)
    {
        var audit = Audit(baseDirectory);
        var files = new[] { "Resources/taxonomies/skill-taxonomy.txt", "Resources/taxonomies/role-archetypes.txt" }
            .Where(audit.Resolved.ContainsKey).Select(x => audit.Resolved[x]).ToList();
        if (files.Count != 2)
            throw new FileNotFoundException("The shipped skill taxonomy or role-archetype preload is missing. " +
                                            string.Join(", ", audit.Missing));
        return files;
    }

    private static IEnumerable<string> CandidateRoots(string baseDirectory)
    {
        yield return baseDirectory;
        var assemblyDirectory = Path.GetDirectoryName(typeof(ProductAssetInventory).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory)) yield return assemblyDirectory;
    }
}

public sealed record ProductAssetAudit(
    IReadOnlyList<string> Missing,
    IReadOnlyDictionary<string, string> Resolved,
    long TotalBytes)
{
    public bool IsComplete => Missing.Count == 0;
}
