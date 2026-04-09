namespace lucidRESUME.Matching;

/// <summary>
/// Provides fast exact-match lookup for known entities: companies, locations, job titles, industries.
/// Built from 48K+ companies, 14K+ locations, 11K+ titles mined from LinkedIn + Adzuna datasets.
/// Used by the JD parser to validate NER entity candidates — if NER says "Amazon" is ORG,
/// and we know Amazon is a real company, confidence gets boosted.
/// </summary>
public sealed class EntityLookupService : Core.Interfaces.IEntityLookup
{
    private readonly Lazy<EntityData> _data = new(Load);

    public IReadOnlySet<string> Companies => _data.Value.Companies;
    public IReadOnlySet<string> Locations => _data.Value.Locations;
    public IReadOnlySet<string> JobTitles => _data.Value.JobTitles;
    public IReadOnlySet<string> Industries => _data.Value.Industries;

    /// <summary>Returns true if the text is a known company name (case-insensitive).</summary>
    public bool IsKnownCompany(string text) => _data.Value.CompaniesLower.Contains(text.ToLowerInvariant().Trim());

    /// <summary>Returns true if the text is a known location (case-insensitive).</summary>
    public bool IsKnownLocation(string text) => _data.Value.LocationsLower.Contains(text.ToLowerInvariant().Trim());

    /// <summary>Returns true if the text matches a known job title (case-insensitive).</summary>
    public bool IsKnownJobTitle(string text) => _data.Value.JobTitlesLower.Contains(text.ToLowerInvariant().Trim());

    /// <summary>Returns true if the text matches a known industry (case-insensitive).</summary>
    public bool IsKnownIndustry(string text) => _data.Value.IndustriesLower.Contains(text.ToLowerInvariant().Trim());

    /// <summary>
    /// Finds all known company names that appear in the given text.
    /// Uses word boundary matching to avoid false positives.
    /// Only checks companies with 3+ characters to avoid noise.
    /// </summary>
    public List<string> FindCompaniesInText(string text)
    {
        var lower = text.ToLowerInvariant();
        var found = new List<string>();

        foreach (var company in _data.Value.Companies)
        {
            if (company.Length < 3) continue;
            var companyLower = company.ToLowerInvariant();
            var idx = lower.IndexOf(companyLower, StringComparison.Ordinal);
            if (idx < 0) continue;

            // Word boundary check
            var before = idx > 0 ? lower[idx - 1] : ' ';
            var after = idx + companyLower.Length < lower.Length ? lower[idx + companyLower.Length] : ' ';
            if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after))
                found.Add(company);
        }

        return found;
    }

    /// <summary>
    /// Finds all known locations that appear in the given text.
    /// </summary>
    public List<string> FindLocationsInText(string text)
    {
        var lower = text.ToLowerInvariant();
        var found = new List<string>();

        foreach (var loc in _data.Value.Locations)
        {
            if (loc.Length < 3) continue;
            var locLower = loc.ToLowerInvariant();
            if (lower.Contains(locLower, StringComparison.Ordinal))
                found.Add(loc);
        }

        return found;
    }

    private static EntityData Load()
    {
        var companies = LoadFile("entities-companies.txt");
        var locations = LoadFile("entities-locations.txt");
        var titles = LoadFile("entities-titles.txt");
        var industries = LoadFile("entities-industries.txt");

        return new EntityData
        {
            Companies = companies,
            CompaniesLower = new HashSet<string>(companies.Select(c => c.ToLowerInvariant()), StringComparer.Ordinal),
            Locations = locations,
            LocationsLower = new HashSet<string>(locations.Select(l => l.ToLowerInvariant()), StringComparer.Ordinal),
            JobTitles = titles,
            JobTitlesLower = new HashSet<string>(titles.Select(t => t.ToLowerInvariant()), StringComparer.Ordinal),
            Industries = industries,
            IndustriesLower = new HashSet<string>(industries.Select(i => i.ToLowerInvariant()), StringComparer.Ordinal),
        };
    }

    private static HashSet<string> LoadFile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", fileName);
        if (!File.Exists(path))
        {
            var asmDir = Path.GetDirectoryName(typeof(EntityLookupService).Assembly.Location)!;
            path = Path.Combine(asmDir, "Resources", fileName);
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return set;

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var trimmed = line.Trim();
            if (trimmed.Length >= 2)
                set.Add(trimmed);
        }

        return set;
    }

    private sealed class EntityData
    {
        public HashSet<string> Companies { get; init; } = [];
        public HashSet<string> CompaniesLower { get; init; } = [];
        public HashSet<string> Locations { get; init; } = [];
        public HashSet<string> LocationsLower { get; init; } = [];
        public HashSet<string> JobTitles { get; init; } = [];
        public HashSet<string> JobTitlesLower { get; init; } = [];
        public HashSet<string> Industries { get; init; } = [];
        public HashSet<string> IndustriesLower { get; init; } = [];
    }
}
