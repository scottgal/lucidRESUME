using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Extraction;
using lucidRESUME.JobML;
using System.Text.RegularExpressions;

namespace lucidRESUME.Matching;

/// <summary>
/// Categorises skills using loaded taxonomies.
/// Sets Skill.Category based on which taxonomy file contains the skill.
/// Also detects the resume's primary domain.
/// </summary>
public static class SkillCategoriser
{
    public const string SkillContractVersion = "resume-skill-category-v1";
    private static readonly IReadOnlyDictionary<string, string> DecisionCategories =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["language"] = "Programming or query language.",
            ["cloud_devops"] = "Cloud platform, infrastructure, deployment, or DevOps tool.",
            ["database"] = "Database or data store.",
            ["framework"] = "Software framework or library.",
            ["tool"] = "Other software development or operations tool.",
            ["security"] = "Security method, standard, or tool.",
            ["ai_ml"] = "Artificial intelligence or machine learning technique or tool.",
            ["methodology"] = "Software delivery method or architecture practice.",
            ["technology"] = "Other information technology skill.",
            ["engineering"] = "Engineering outside software development.",
            ["healthcare"] = "Healthcare practice or knowledge.",
            ["finance"] = "Finance or banking practice or knowledge.",
            ["accounting"] = "Accounting practice or knowledge.",
            ["sales"] = "Sales practice or platform.",
            ["education"] = "Teaching or education practice.",
            ["human_resources"] = "Human resources practice.",
            ["construction"] = "Construction practice or tool.",
            ["design"] = "Design practice or tool.",
            ["digital_media"] = "Digital media production skill.",
            ["aviation"] = "Aviation practice or knowledge.",
            ["hospitality"] = "Hospitality practice or knowledge.",
            ["legal"] = "Legal practice or knowledge.",
            ["other"] = "The passage does not establish a clear category for this skill."
        };
    private static readonly IReadOnlyDictionary<string, string> DecisionLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["language"] = "Language", ["cloud_devops"] = "Cloud & DevOps",
            ["database"] = "Database", ["framework"] = "Framework", ["tool"] = "Tool",
            ["security"] = "Security", ["ai_ml"] = "AI/ML", ["methodology"] = "Methodology",
            ["technology"] = "Technology", ["engineering"] = "Engineering",
            ["healthcare"] = "Healthcare", ["finance"] = "Finance", ["accounting"] = "Accounting",
            ["sales"] = "Sales", ["education"] = "Education", ["human_resources"] = "Human Resources",
            ["construction"] = "Construction", ["design"] = "Design",
            ["digital_media"] = "Digital Media", ["aviation"] = "Aviation",
            ["hospitality"] = "Hospitality", ["legal"] = "Legal"
        };
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
    public static void Categorise(ResumeDocument resume, bool useDomainFallback = true)
    {
        LinkSourcePassages(resume);
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
                var domains = SkillTaxonomy.FindDomains(canonical);
                var skillDomain = domains.Count == 1 ? domains[0] : null;
                if (skillDomain != null && DomainToCategory.TryGetValue(skillDomain, out var cat))
                {
                    skill.Category = cat;
                    continue;
                }
            }

            // Fallback: use the resume's detected domain as a general category
            if (useDomainFallback && DomainToCategory.TryGetValue(domain, out var domainCat))
                skill.Category = domainCat;
        }
    }

    private static void LinkSourcePassages(ResumeDocument resume)
    {
        var source = resume.CanonicalMarkdown ?? resume.RawMarkdown;
        if (string.IsNullOrWhiteSpace(source)) return;
        var index = MarkdownEvidenceIndex.Create(source);
        foreach (var skill in resume.Skills)
        {
            var phrase = skill.Name.Trim();
            if (phrase.Length < 2) continue;
            var mention = new Regex($@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(phrase)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (var passage in index.Passages.Where(passage =>
                         mention.IsMatch(passage.Text) && !index.IsAmbiguous(passage.Reference)).Take(12))
                if (!skill.SourceReferences.Contains(passage.Reference, StringComparer.OrdinalIgnoreCase))
                    skill.SourceReferences.Add(passage.Reference);
        }
    }

    /// <summary>
    /// Classifies only unresolved, already extracted skills that occur in a uniquely
    /// referenced source paragraph. Uncertain decisions leave the skill unchanged.
    /// </summary>
    public static async Task CategoriseAmbiguousAsync(ResumeDocument resume,
        IResumeDecisionProvider provider, double acceptanceProbability = 0.80,
        double minimumMargin = 0.20, CancellationToken ct = default)
    {
        var source = resume.CanonicalMarkdown ?? resume.RawMarkdown;
        if (string.IsNullOrWhiteSpace(source)) return;
        var index = MarkdownEvidenceIndex.Create(source);
        var candidateHash = MarkdownEvidenceIndex.Fingerprint(string.Join('\n', DecisionCategories
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")));

        // Bound local inference work. Categories supplied by the source or taxonomy are final.
        foreach (var skill in resume.Skills.Where(value => string.IsNullOrWhiteSpace(value.Category)).Take(12))
        {
            ct.ThrowIfCancellationRequested();
            var phrase = skill.Name.Trim();
            if (phrase.Length < 2) continue;
            var mention = new Regex($@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(phrase)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var passages = index.Passages.Where(passage => mention.IsMatch(passage.Text) &&
                !index.IsAmbiguous(passage.Reference) &&
                passage.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 5).ToList();
            if (passages.Count == 0) continue;
            // A bare skills list proves the term was listed, but gives too little
            // context to decide what the skill is. Prefer substantive prose.
            var passage = passages.OrderByDescending(value => value.Text.Length).First();
            var match = mention.Match(passage.Text);
            var start = Math.Max(0, match.Index - 500);
            var length = Math.Min(1200, passage.Text.Length - start);
            var excerpt = passage.Text.Substring(start, length);
            var request = new ResumeDecisionRequest(
                $"{resume.ResumeId:N}:{SkillContractVersion}:{passage.Fingerprint[9..]}:{MarkdownEvidenceIndex.Fingerprint(phrase)[9..]}",
                passage.Reference, passage.Fingerprint,
                $"Skill: {phrase}\nSource passage: {excerpt}",
                "Classify only the named skill using the cited passage. Choose other if its category is unclear.",
                DecisionCategories);
            try
            {
                var result = await provider.DecideAsync(request, ct);
                var ranked = result.Probabilities.Values.OrderDescending().Take(2).ToArray();
                var selectedProbability = result.Probabilities.GetValueOrDefault(result.SelectedCandidate);
                var margin = ranked.Length == 2 ? ranked[0] - ranked[1] : ranked.FirstOrDefault();
                var accepted = result.SelectedCandidate != "other" &&
                    DecisionLabels.ContainsKey(result.SelectedCandidate) &&
                    selectedProbability >= acceptanceProbability && margin >= minimumMargin;
                var label = accepted ? DecisionLabels[result.SelectedCandidate] : null;
                resume.IngestionDecisions.Add(new IngestionDecision
                {
                    DecisionId = request.DecisionId, ContractVersion = SkillContractVersion,
                    SourceRef = request.SourceRef, SourceHash = request.SourceHash,
                    CandidateSetHash = candidateHash, SelectedCandidate = result.SelectedCandidate,
                    SelectedValue = label,
                    Probabilities = new Dictionary<string, double>(result.Probabilities, StringComparer.Ordinal),
                    Confidence = result.Confidence, Margin = margin, Accepted = accepted,
                    Provider = result.Provider, Model = result.Model,
                    InputTokens = result.InputTokens, OutputTokens = result.OutputTokens,
                    RequestId = result.RequestId, EvaluatedAt = DateTimeOffset.UtcNow
                });
                if (accepted) skill.Category = label;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { /* Classification is advisory; deterministic import continues. */ }
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
            var domains = SkillTaxonomy.FindDomains(canonical);
            var skillDomain = domains.Count == 1 ? domains[0] : null;
            if (skillDomain != null && DomainToCategory.TryGetValue(skillDomain, out var cat))
                return cat;
        }
        return null;
    }

    private static string? FindItSubCategory(string skillLower)
    {
        foreach (var (keywords, subCat) in ItSubCategories)
        {
            if (keywords.Any(k => string.Equals(skillLower.Trim(), k, StringComparison.Ordinal)))
                return subCat;
        }
        return null;
    }

}
