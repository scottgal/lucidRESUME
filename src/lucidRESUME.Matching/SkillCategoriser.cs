using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching;

/// <summary>
/// Categorises skills using loaded taxonomies.
/// Sets Skill.Category based on which taxonomy file contains the skill.
/// Also detects the resume's primary domain.
/// </summary>
public static class SkillCategoriser
{
    // Map taxonomy file names to human-readable category labels
    private static readonly Dictionary<string, string> DomainToCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["information-technology"] = "Technology",
        ["engineering"] = "Engineering",
        ["healthcare"] = "Healthcare",
        ["finance"] = "Finance",
        ["accounting"] = "Accounting",
        ["sales"] = "Sales",
        ["education"] = "Education",
        ["hr"] = "Human Resources",
        ["construction"] = "Construction",
        ["design"] = "Design",
        ["digital-media"] = "Digital Media",
        ["aviation"] = "Aviation",
        ["hospitality"] = "Hospitality",
        ["legal"] = "Legal",
        ["banking"] = "Banking",
    };

    // Sub-categories within IT taxonomy (detected from the taxonomy comments)
    private static readonly (string[] Keywords, string SubCategory)[] ItSubCategories =
    [
        (["python", "javascript", "typescript", "java", "c#", "c++", "go", "rust", "ruby", "php", "swift", "kotlin", "r", "scala", "sql", "html", "css"], "Language"),
        (["aws", "azure", "gcp", "docker", "kubernetes", "terraform", "ansible", "jenkins", "github actions", "gitlab ci"], "Cloud & DevOps"),
        (["mongodb", "redis", "elasticsearch", "cassandra", "neo4j", "sqlite", "oracle", "postgresql", "mysql"], "Database"),
        (["react", "angular", "vue", "django", "flask", "spring", "express", "fastapi", ".net", "asp.net"], "Framework"),
        (["git", "linux", "nginx", "apache", "grafana", "prometheus", "jira", "confluence"], "Tool"),
        (["oauth", "jwt", "ssl", "penetration testing", "soc2", "gdpr"], "Security"),
        (["machine learning", "deep learning", "nlp", "computer vision", "tensorflow", "pytorch", "scikit-learn"], "AI/ML"),
        (["agile", "scrum", "devops", "microservices", "rest", "graphql", "grpc"], "Methodology"),
    ];

    /// <summary>
    /// Categorise all skills on a resume document.
    /// Sets Skill.Category for each skill that matches a taxonomy entry.
    /// </summary>
    public static void Categorise(ResumeDocument resume)
    {
        var domain = DomainDetector.DetectPrimary(resume);

        foreach (var skill in resume.Skills)
        {
            if (!string.IsNullOrEmpty(skill.Category)) continue; // already set

            var lower = skill.Name.ToLowerInvariant();

            // Try IT sub-categories first (most specific)
            var subCat = FindItSubCategory(lower);
            if (subCat != null)
            {
                skill.Category = subCat;
                continue;
            }

            // Try taxonomy lookup — which domain file contains this skill?
            var canonical = SkillTaxonomy.Canonicalize(lower);
            if (canonical != null)
            {
                // Find which domain this canonical term belongs to
                var skillDomain = FindDomainForSkill(canonical);
                if (skillDomain != null && DomainToCategory.TryGetValue(skillDomain, out var cat))
                {
                    skill.Category = cat;
                    continue;
                }
            }

            // Fallback: use the resume's detected domain as a general category
            if (DomainToCategory.TryGetValue(domain, out var domainCat))
                skill.Category = domainCat;
        }
    }

    /// <summary>
    /// Categorise a single skill name, returning its category string or null.
    /// </summary>
    public static string? CategoriseSkill(string skillName)
    {
        var lower = skillName.ToLowerInvariant();
        var subCat = FindItSubCategory(lower);
        if (subCat != null) return subCat;

        var canonical = SkillTaxonomy.Canonicalize(lower);
        if (canonical != null)
        {
            var skillDomain = FindDomainForSkill(canonical);
            if (skillDomain != null && DomainToCategory.TryGetValue(skillDomain, out var cat))
                return cat;
        }
        return null;
    }

    private static string? FindItSubCategory(string skillLower)
    {
        foreach (var (keywords, subCat) in ItSubCategories)
        {
            if (keywords.Any(k => skillLower.Contains(k) || k.Contains(skillLower)))
                return subCat;
        }
        return null;
    }

    private static string? FindDomainForSkill(string canonical)
    {
        // Check each taxonomy — which one contains this canonical term?
        // SkillTaxonomy.LoadedTaxonomies returns the file names
        foreach (var taxName in SkillTaxonomy.LoadedTaxonomies)
        {
            var aliases = SkillTaxonomy.GetAliases(canonical);
            if (aliases.Count > 1) // has aliases = found in a taxonomy
                return taxName;
        }
        return null;
    }
}
