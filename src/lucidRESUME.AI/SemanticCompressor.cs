using System.Text;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Matching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace lucidRESUME.AI;

/// <summary>
/// Semantic resume compression. Given a JD's skill requirements, queries the
/// skill ledger for evidence of each required skill, selects only the most
/// relevant roles and achievements, and composes a compressed resume.
///
/// A 30-year career with 13 roles becomes 2 pages of laser-targeted evidence.
///
/// The result retains stable ledger claim IDs and is rendered without re-inference.
/// </summary>
public sealed class SemanticCompressor
{
    private const int MaximumRelevantRoles = 5;
    private const int MaximumSelectedProjects = 2;
    private const int MaximumSkillsPerCategory = 10;
    private const int MaximumSummaryWords = 105;
    private const int MaximumProjectDescriptionWords = 95;
    private static readonly HashSet<string> GenericSkillLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "engineering", "systems", "system", "technical", "tech", "technology", "technologies",
        "performance", "lead", "leading", "management", "it", "software", "development"
    };
    private static readonly HashSet<string> ShortTechnicalTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ai", "ml", "c#", "f#", "go", "r"
    };

    private readonly SkillLedgerBuilder _ledgerBuilder;
    private readonly JdSkillLedgerBuilder _jdLedgerBuilder;
    private readonly SkillLedgerMatcher _matcher;
    private readonly IEmbeddingService _embedder;
    private readonly SkillTaxonomyService _taxonomy;
    private readonly ILogger<SemanticCompressor> _logger;
    private readonly HashSet<string> _careerAnchorCompanies;

    public SemanticCompressor(
        SkillLedgerBuilder ledgerBuilder,
        JdSkillLedgerBuilder jdLedgerBuilder,
        SkillLedgerMatcher matcher,
        IEmbeddingService embedder,
        SkillTaxonomyService taxonomy,
        IOptions<TailoringOptions> options,
        ILogger<SemanticCompressor> logger)
    {
        _ledgerBuilder = ledgerBuilder;
        _jdLedgerBuilder = jdLedgerBuilder;
        _matcher = matcher;
        _embedder = embedder;
        _taxonomy = taxonomy;
        _logger = logger;
        _careerAnchorCompanies = options.Value.CareerAnchorCompanies
            .Select(NormalizeCompany)
            .Where(company => company.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compress a resume to only the evidence that matches a JD's requirements.
    /// Returns a pre-filtered projection whose prose blocks remain bound to ledger claims.
    /// </summary>
    public async Task<CompressedResume> CompressAsync(
        ResumeDocument resume, JobDescription jd, CancellationToken ct = default)
    {
        var sourceLedger = EvidenceLedgerBuilder.EnsureCurrent(resume);
        var resumeLedger = await _ledgerBuilder.BuildAsync(resume, ct);
        var jdLedger = await _jdLedgerBuilder.BuildAsync(jd, ct);
        var matchResult = await _matcher.MatchAsync(resumeLedger, jdLedger, ct);

        _logger.LogInformation(
            "Compressing resume for {Title} at {Company}: semantic evidence coverage={Coverage:P0}, {Matched}/{Total} requirement terms matched",
            jd.Title, jd.Company, matchResult.OverallFit,
            matchResult.Matches.Count(m => m.IsMatched), matchResult.Matches.Count);

        // Collect the most relevant experience IDs - roles that evidence required skills
        var roleRequirements = new Dictionary<Guid, HashSet<string>>();
        var skillToEvidence = new Dictionary<string, List<SkillEvidence>>();

        foreach (var match in matchResult.Matches.Where(m => m.IsMatched))
        {
            var ledgerEntry = resumeLedger.Find(match.MatchedResumeSkill!);
            if (ledgerEntry is null) continue;

            skillToEvidence[match.RequiredSkill] = ledgerEntry.Evidence
                .OrderByDescending(e => e.Confidence)
                .ThenByDescending(e => e.StartDate)
                .Take(3) // top 3 evidence pieces per skill
                .ToList();

            foreach (var evidence in skillToEvidence[match.RequiredSkill])
            {
                if (evidence.ExperienceId.HasValue)
                {
                    if (!roleRequirements.TryGetValue(evidence.ExperienceId.Value, out var supported))
                        roleRequirements[evidence.ExperienceId.Value] = supported = new(StringComparer.OrdinalIgnoreCase);
                    supported.Add(match.RequiredSkill);
                }
            }
        }

        var roleScores = roleRequirements.ToDictionary(x => x.Key, x => (double)x.Value.Count);
        var roleProfile = ResolveRoleProfile(jd.Title);
        var roleCentroid = roleProfile is null ? [] : await _taxonomy.GetRoleCentroidAsync(roleProfile, ct);
        if (roleCentroid.Length > 0)
        {
            foreach (var experience in resume.Experience.Where(IsUsableRole))
            {
                var roleText = string.Join(' ', new[] { experience.Title, experience.Company }
                    .Concat(experience.Achievements).Where(x => !string.IsNullOrWhiteSpace(x))!);
                var similarity = _embedder.CosineSimilarity(roleCentroid, await _embedder.EmbedAsync(roleText, ct));
                roleScores[experience.Id] = roleScores.GetValueOrDefault(experience.Id) + Math.Max(0, similarity) * 8;
                if (MentionsRole(roleText, roleProfile!)) roleScores[experience.Id] += 5;
                roleScores[experience.Id] += RoleTitleSignal(experience.Title, roleProfile!);
            }
        }

        // Recency is a modest tie-breaker, not permission for broad skill matches to emit
        // an eleven-role chronology. A projection needs enough evidence, not every match.
        var orderedByRecency = resume.Experience.OrderByDescending(e => e.StartDate).ToList();
        for (var i = 0; i < Math.Min(3, orderedByRecency.Count); i++)
            roleScores[orderedByRecency[i].Id] = roleScores.GetValueOrDefault(orderedByRecency[i].Id) + (3 - i) * .75;

        // Relevance still dominates, but two equally useful examples should favour the
        // recent one. Without a bounded decay, an exact title match from twenty years
        // ago can displace substantially better contemporary evidence.
        var currentYear = DateTime.UtcNow.Year;
        foreach (var experience in resume.Experience.Where(IsUsableRole))
        {
            var evidenceYear = experience.IsCurrent
                ? currentYear
                : experience.EndDate?.Year ?? experience.StartDate?.Year ?? currentYear - 20;
            var age = Math.Max(0, currentYear - evidenceYear);
            roleScores[experience.Id] = roleScores.GetValueOrDefault(experience.Id) + Math.Max(0, 6 - age * .5);
            if (IsContinuingCompanyRole(experience))
                roleScores[experience.Id] += 8;
        }

        var selectedRoleIds = SelectDetailedRoleIds(
            resume.Experience, roleScores, _careerAnchorCompanies, MaximumRelevantRoles);
        var anchorCount = resume.Experience.Count(experience =>
            selectedRoleIds.Contains(experience.Id) && IsCareerAnchor(experience, _careerAnchorCompanies));
        if (anchorCount > 0)
            _logger.LogInformation("Retained {Count} author-selected career anchor role(s)", anchorCount);

        var projection = ResumeDocument.Create(resume.FileName, resume.ContentType, resume.FileSizeBytes);
        projection.Personal = new PersonalInfo
        {
            FullName = Accepted(sourceLedger, "personal:name") ? resume.Personal.FullName : null,
            Email = resume.Personal.Email,
            Phone = resume.Personal.Phone,
            ContactPreference = resume.Personal.ContactPreference,
            Location = resume.Personal.Location,
            LinkedInUrl = resume.Personal.LinkedInUrl,
            GitHubUrl = resume.Personal.GitHubUrl,
            WebsiteUrl = resume.Personal.WebsiteUrl,
            Summary = resume.Personal.Summary
        };
        projection.Projection = new ResumeProjectionInfo
        {
            SourceResumeId = resume.ResumeId,
            SourceRevision = sourceLedger.SourceRevision
        };

        // Build Markdown and its ledger bindings together. This is a projection, not inference.
        var md = new StringBuilder();

        // Header
        md.AppendLine($"# {resume.Personal.FullName ?? ""}");
        var contactParts = new List<string>();
        if (!string.IsNullOrEmpty(resume.Personal.Email)) contactParts.Add(resume.Personal.Email);
        if (!string.IsNullOrEmpty(resume.Personal.Phone))
        {
            var phone = resume.Personal.Phone;
            if (!string.IsNullOrWhiteSpace(resume.Personal.ContactPreference))
                phone += $" - {resume.Personal.ContactPreference} -";
            contactParts.Add(phone);
        }
        if (!string.IsNullOrEmpty(resume.Personal.Location)) contactParts.Add(resume.Personal.Location);
        if (!string.IsNullOrEmpty(resume.Personal.LinkedInUrl)) contactParts.Add(resume.Personal.LinkedInUrl);
        if (!string.IsNullOrEmpty(resume.Personal.GitHubUrl)) contactParts.Add(resume.Personal.GitHubUrl);
        if (!string.IsNullOrEmpty(resume.Personal.WebsiteUrl)) contactParts.Add(resume.Personal.WebsiteUrl);
        if (contactParts.Count > 0)
        {
            md.AppendLine(string.Join(" | ", contactParts));
            md.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(jd.Title))
        {
            md.AppendLine($"**Target role: {jd.Title.Trim()}**");
            md.AppendLine();
        }
        md.AppendLine();

        // Compressed summary - targeted to the JD
        if (!string.IsNullOrWhiteSpace(resume.Personal.Summary))
        {
            var summaryTerms = jd.RequiredSkills.Concat(jd.PreferredSkills)
                .Concat(roleProfile is null ? [] : _taxonomy.GetRoleSkills(roleProfile))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var summary = SelectSummary(resume.Personal.Summary, summaryTerms,
                roleProfile is "CTO" or "VP of Engineering" or "Head of Engineering");
            if (!string.IsNullOrWhiteSpace(summary))
            {
                md.AppendLine("## Summary {#summary}");
                md.AppendLine(summary);
                md.AppendLine();
                Bind(projection, sourceLedger, "personal:summary", "#summary:p1");
                projection.Personal.Summary = summary;
            }
        }

        // Relevant experience only
        md.AppendLine("## Experience {#experience}");
        md.AppendLine();
        var includedRoles = 0;
        foreach (var exp in resume.Experience
                     .OrderByDescending(IsContinuingCompanyRole)
                     .ThenByDescending(e => e.StartDate))
        {
            if (!selectedRoleIds.Contains(exp.Id)) continue;

            md.AppendLine($"### {exp.Title ?? ""} - {exp.Company ?? ""} {{#experience-{exp.Id:N}}}");
            if (IsCareerAnchor(exp, _careerAnchorCompanies))
                md.AppendLine("<!-- lucidresume:career-anchor -->");
            var dateRange = FormatDateRange(exp.StartDate, exp.EndDate, exp.IsCurrent);
            if (!string.IsNullOrEmpty(dateRange))
            {
                md.AppendLine($"*{dateRange}*");
                md.AppendLine();
            }

            // Include only achievements that evidence matched skills
            var relevantAchievements = new List<string>();
            foreach (var achievement in exp.Achievements)
            {
                // Check if this achievement is evidence for any matched skill
                var isEvidence = skillToEvidence.Values
                    .SelectMany(e => e)
                    .Any(e => e.SourceText == achievement);

                if ((isEvidence || relevantAchievements.Count < 2) &&
                    relevantAchievements.All(existing => !NearDuplicate(existing, achievement)))
                    relevantAchievements.Add(achievement);
            }

            var isCareerAnchor = IsCareerAnchor(exp, _careerAnchorCompanies);
            if (isCareerAnchor ||
                roleProfile is "CTO" or "VP of Engineering" or "Head of Engineering")
            {
                var definingAchievement = exp.Achievements.FirstOrDefault(IsCareerDefiningAchievement);
                if (definingAchievement is not null && isCareerAnchor)
                {
                    relevantAchievements.RemoveAll(existing => string.Equals(existing, definingAchievement,
                        StringComparison.Ordinal));
                    relevantAchievements.Insert(0, definingAchievement);
                }
                else if (definingAchievement is not null &&
                         relevantAchievements.All(existing => !string.Equals(existing, definingAchievement,
                             StringComparison.Ordinal)))
                {
                    if (relevantAchievements.Count >= 3)
                        relevantAchievements[2] = definingAchievement;
                    else
                        relevantAchievements.Add(definingAchievement);
                }
            }

            var projectedExperience = new WorkExperience
            {
                Id = exp.Id,
                Company = exp.Company,
                Title = exp.Title,
                Location = exp.Location,
                StartDate = exp.StartDate,
                EndDate = exp.EndDate,
                IsCurrent = exp.IsCurrent,
                IsCareerAnchor = IsCareerAnchor(exp, _careerAnchorCompanies),
                Technologies = [.. exp.Technologies],
                ImportSources = [.. exp.ImportSources]
            };
            var paragraph = string.IsNullOrEmpty(dateRange) ? 1 : 2;
            var maximumAchievements = isCareerAnchor ? 1 : 3;
            foreach (var a in relevantAchievements.Take(maximumAchievements))
            {
                md.AppendLine($"- {a}");
                md.AppendLine();
                projectedExperience.Achievements.Add(a);
                var sourceIndex = exp.Achievements.IndexOf(a);
                if (sourceIndex >= 0)
                    Bind(projection, sourceLedger, $"experience:{exp.Id:N}:achievement:{sourceIndex + 1}",
                        $"#experience-{exp.Id:N}:p{paragraph++}");
            }
            projection.Experience.Add(projectedExperience);
            includedRoles++;

        }

        var projectTerms = jd.RequiredSkills.Concat(jd.PreferredSkills)
            .Concat(roleProfile is null ? [] : _taxonomy.GetRoleSkills(roleProfile))
            .SelectMany(Tokens)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedProjects = SelectRelevantProjects(resume.Projects, projectTerms, MaximumSelectedProjects);

        if (selectedProjects.Count > 0)
        {
            md.AppendLine("## Selected Projects {#projects}");
            md.AppendLine();
            foreach (var project in selectedProjects)
            {
                var anchor = $"project-{project.Id:N}";
                var selectedDescription = SelectProjectDescription(project.Description!, projectTerms);
                var selectedTechnologies = SelectProjectTechnologies(project.Technologies, projectTerms);
                md.AppendLine($"### {project.Name} {{#{anchor}}}");
                md.AppendLine(selectedDescription);
                md.AppendLine();
                if (selectedTechnologies.Count > 0)
                {
                    md.AppendLine($"**Technologies:** {string.Join(", ", selectedTechnologies)}");
                    md.AppendLine();
                }
                projection.Projects.Add(new Project
                {
                    Id = project.Id,
                    Name = project.Name,
                    Description = selectedDescription,
                    Technologies = selectedTechnologies,
                    Url = project.Url,
                    Date = project.Date,
                    ImportSources = [.. project.ImportSources],
                    EvidenceMetadata = new Dictionary<string, string>(project.EvidenceMetadata,
                        StringComparer.OrdinalIgnoreCase)
                });
                Bind(projection, sourceLedger, $"project:{project.Id:N}:description", $"#{anchor}:p1");
            }
        }

        // Project only ledger-backed, specific skills. Concepts demonstrated by the
        // selected prose are included even when the JD matcher chose a broader alias.
        md.AppendLine("## Skills {#skills}");
        var matchedSkillNames = matchResult.Matches
            .Where(m => m.IsMatched)
            .Select(m => resumeLedger.Find(m.MatchedResumeSkill!))
            .Where(e => e is not null)
            .Select(entry => entry!.SkillName);
        var demonstratedConcepts = projection.Projection.Blocks
            .Select(block => sourceLedger.Claims.FirstOrDefault(claim => claim.Id == block.ClaimId))
            .Where(claim => claim is not null)
            .SelectMany(claim => claim!.Concepts);
        var priorityLeadership = roleProfile is "CTO" or "VP of Engineering" or "Head of Engineering"
            ? resume.Skills
                .Where(skill => string.Equals(skill.Category, "Leadership", StringComparison.OrdinalIgnoreCase))
                .OrderBy(skill => ExecutiveLeadershipOrder(skill.Name))
                .Select(skill => skill.Name)
            : [];
        var skillsByCategory = priorityLeadership.Concat(matchedSkillNames).Concat(demonstratedConcepts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !GenericSkillLabels.Contains(name))
            .Select(name => resume.Skills.FirstOrDefault(skill =>
                string.Equals(skill.Name, name, StringComparison.OrdinalIgnoreCase)))
            .Where(skill => skill is not null)
            .DistinctBy(skill => skill!.Name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(skill => skill!.Category ?? "General")
            .ToList();

        var skillParagraph = 1;
        foreach (var group in skillsByCategory)
        {
            var entries = group.Take(MaximumSkillsPerCategory).ToList();
            md.AppendLine($"**{group.Key}:** {string.Join(", ", entries.Select(skill => skill!.Name))}");
            md.AppendLine();
            foreach (var entry in entries)
            {
                projection.Skills.Add(new Skill
                {
                    Name = entry!.Name,
                    Category = entry.Category,
                    YearsExperience = entry.YearsExperience,
                    ImportSources = [.. entry.ImportSources]
                });
                Bind(projection, sourceLedger, EvidenceLedgerBuilder.SkillLocator(entry.Name), $"#skills:p{skillParagraph}");
            }
            skillParagraph++;
        }

        // Education
        if (resume.Education.Count > 0)
        {
            md.AppendLine("## Education {#education}");
            var educationParagraph = 1;
            foreach (var edu in resume.Education)
            {
                var qualification = string.IsNullOrWhiteSpace(edu.FieldOfStudy)
                    ? edu.Degree ?? ""
                    : $"{edu.Degree} | {edu.FieldOfStudy}";
                md.AppendLine($"**{qualification}** | {edu.Institution ?? ""}");
                var educationDates = FormatDateRange(edu.StartDate, edu.EndDate, false);
                if (!string.IsNullOrWhiteSpace(educationDates))
                    md.AppendLine($"*{educationDates}*");
                md.AppendLine();
                projection.Education.Add(edu);
                Bind(projection, sourceLedger, $"education:{edu.Id:N}", $"#education:p{educationParagraph++}");
            }
        }

        var compressedMd = md.ToString();
        projection.SetDoclingOutput(compressedMd, null, compressedMd);
        projection.EvidenceLedger = sourceLedger;

        return new CompressedResume
        {
            Markdown = compressedMd,
            OriginalRoleCount = resume.Experience.Count,
            IncludedRoleCount = includedRoles,
            // This ratio describes target requirements, not the size of the
            // candidate's source skill catalogue.
            OriginalSkillCount = matchResult.Matches.Count,
            MatchedSkillCount = matchResult.Matches.Count(m => m.IsMatched),
            OverallFit = matchResult.OverallFit,
            Gaps = matchResult.Gaps,
            Projection = projection
        };
    }

    private static void Bind(ResumeDocument projection, EvidenceLedger ledger, string locator, string proseRef)
    {
        var evidence = ledger.Evidence.FirstOrDefault(item =>
            string.Equals(item.Locator, locator, StringComparison.OrdinalIgnoreCase));
        if (evidence is null) return;
        var claim = ledger.Claims.FirstOrDefault(item => item.EvidenceIds.Contains(evidence.Id, StringComparer.OrdinalIgnoreCase));
        if (claim is null) return;
        projection.Projection!.Blocks.Add(new ResumeProjectionBlock { ClaimId = claim.Id, ProseRef = proseRef });
    }

    private static bool NearDuplicate(string left, string right)
    {
        var leftTokens = Tokens(left);
        var rightTokens = Tokens(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0) return false;
        var intersection = leftTokens.Intersect(rightTokens).Count();
        var union = leftTokens.Union(rightTokens).Count();
        return union > 0 && intersection / (double)union >= 0.25;
    }

    private static HashSet<string> Tokens(string text) => text.ToLowerInvariant()
        .Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '(', ')', '/', '-'], StringSplitOptions.RemoveEmptyEntries)
        .Where(token => token.Length > 2 || ShortTechnicalTokens.Contains(token))
        .Select(token => token.Length > 7 ? token[..6] : token.TrimEnd('s'))
        .ToHashSet(StringComparer.Ordinal);

    private static bool IsUsableRole(WorkExperience experience)
    {
        if (experience.Achievements.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(experience.Company) || string.IsNullOrWhiteSpace(experience.Title)) return false;
        if (experience.ImportSources.Contains("LLM extraction", StringComparer.OrdinalIgnoreCase)) return false;
        // Common legacy-parser artefact: the date is mistaken for the company and
        // "Present" becomes part of the title. The real role is retained elsewhere.
        if (Regex.IsMatch(experience.Company, @"^\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}$")) return false;
        return !experience.Title.StartsWith("Present ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContinuingCompanyRole(WorkExperience experience)
    {
        if (!experience.IsCurrent || string.IsNullOrWhiteSpace(experience.Title)) return false;
        var title = experience.Title;
        if (!title.Contains("Founder", StringComparison.OrdinalIgnoreCase) &&
            !title.Contains("Owner", StringComparison.OrdinalIgnoreCase)) return false;

        return experience.Achievements.Any(achievement =>
            achievement.Contains("owns", StringComparison.OrdinalIgnoreCase) ||
            achievement.Contains("company through which", StringComparison.OrdinalIgnoreCase));
    }

    internal static HashSet<Guid> SelectDetailedRoleIds(
        IEnumerable<WorkExperience> experience,
        IReadOnlyDictionary<Guid, double> roleScores,
        IReadOnlySet<string>? careerAnchorCompanies = null,
        int maximumRelevantRoles = MaximumRelevantRoles)
    {
        var usable = experience
            .Where(IsUsableRole)
            .ToList();
        var anchors = usable
            .Where(role => IsCareerAnchor(role, careerAnchorCompanies))
            .OrderByDescending(role => roleScores.GetValueOrDefault(role.Id))
            .ThenByDescending(role => role.StartDate)
            .ToList();
        var selected = anchors.Select(role => role.Id).ToHashSet();
        selected.UnionWith(usable
            .Where(role => !selected.Contains(role.Id))
            .OrderByDescending(role => roleScores.GetValueOrDefault(role.Id))
            .ThenByDescending(role => role.StartDate)
            .Take(Math.Max(0, maximumRelevantRoles))
            .Select(role => role.Id));
        return selected;
    }

    private static bool IsCareerAnchor(WorkExperience experience, IReadOnlySet<string>? companies) =>
        experience.IsCareerAnchor || companies?.Contains(NormalizeCompany(experience.Company ?? "")) == true;

    internal static string NormalizeCompany(string company)
    {
        var normalized = Regex.Replace(company.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        string[] suffixes = [" limited", " ltd", " incorporated", " inc", " corporation", " corp", " plc", " llc"];
        foreach (var suffix in suffixes)
            if (normalized.EndsWith(suffix, StringComparison.Ordinal))
                normalized = normalized[..^suffix.Length].TrimEnd();
        return normalized;
    }

    private static bool IsCareerDefiningAchievement(string achievement) =>
        achievement.Contains("first release", StringComparison.OrdinalIgnoreCase) ||
        achievement.Contains("public production release", StringComparison.OrdinalIgnoreCase) ||
        achievement.Contains("launched", StringComparison.OrdinalIgnoreCase) &&
        achievement.Contains("first", StringComparison.OrdinalIgnoreCase);

    private static bool Accepted(EvidenceLedger ledger, string locator)
    {
        var evidence = ledger.Evidence.FirstOrDefault(item =>
            string.Equals(item.Locator, locator, StringComparison.OrdinalIgnoreCase));
        return evidence is not null && ledger.Claims.Any(claim =>
            claim.EvidenceIds.Contains(evidence.Id, StringComparer.OrdinalIgnoreCase) &&
            string.Equals(claim.Review, "accepted", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveRoleProfile(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        if (title.Contains("chief technology", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(title, @"\bCTO\b", RegexOptions.IgnoreCase)) return "CTO";
        if (title.Contains("vp", StringComparison.OrdinalIgnoreCase) &&
            title.Contains("engineering", StringComparison.OrdinalIgnoreCase)) return "VP of Engineering";
        if (title.Contains("head", StringComparison.OrdinalIgnoreCase) &&
            title.Contains("engineering", StringComparison.OrdinalIgnoreCase)) return "Head of Engineering";
        if (title.Contains("technical lead", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("tech lead", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("engineering lead", StringComparison.OrdinalIgnoreCase)) return "Lead Developer";
        if (title.Contains("lead", StringComparison.OrdinalIgnoreCase) &&
            title.Contains("developer", StringComparison.OrdinalIgnoreCase)) return "Lead Developer";
        return null;
    }

    private static bool MentionsRole(string text, string role) => role switch
    {
        "CTO" => Regex.IsMatch(text, @"\bCTO\b|chief technology", RegexOptions.IgnoreCase),
        "VP of Engineering" => Regex.IsMatch(text, @"\bVP\b.*engineering|vice president.*engineering", RegexOptions.IgnoreCase),
        "Head of Engineering" => text.Contains("Head of Engineering", StringComparison.OrdinalIgnoreCase),
        "Lead Developer" => text.Contains("Lead Developer", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Development Lead", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Lead Engineer", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Head of Software", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Head of Development", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Head of Engineering", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    internal static double RoleTitleSignal(string? title, string role)
    {
        if (string.IsNullOrWhiteSpace(title)) return 0;
        if (role == "Lead Developer")
        {
            // A hands-on head-of role is stronger evidence for an engineering-lead
            // target than an old exact-title match. Titles are a hierarchy, not an
            // exact-string taxonomy.
            if (title.Contains("Head of Software", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Head of Development", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Head of Engineering", StringComparison.OrdinalIgnoreCase)) return 6;
            if (title.Contains("Lead Developer", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Lead Engineer", StringComparison.OrdinalIgnoreCase)) return 4;
            if (title.Contains("Development Lead", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0;
        }

        if (role is not ("CTO" or "VP of Engineering" or "Head of Engineering")) return 0;
        if (title.Contains("Head of Engineering", StringComparison.OrdinalIgnoreCase)) return 6;
        if (title.Contains("Chief Technology", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(title, @"\bCTO\b", RegexOptions.IgnoreCase)) return 6;
        if (title.Contains("Owner", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Principal", StringComparison.OrdinalIgnoreCase)) return 4;
        if (title.Contains("Program Manager", StringComparison.OrdinalIgnoreCase)) return 3;
        if (title.Contains("Lead", StringComparison.OrdinalIgnoreCase)) return 2;
        return 0;
    }

    private static string SelectSummary(string summary, IReadOnlySet<string> roleSkills, bool isExecutiveTarget)
    {
        var cleaned = Regex.Replace(summary,
            @"(?im)^.*desired\s+job\s+title\s*:.*(?:\r?\n|$)", "").Trim();
        var sentences = Regex.Split(cleaned, @"(?<=[.!?])\s+")
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .Where(sentence => !Regex.IsMatch(sentence.Trim(), @"^desired\s+job\s+title\s*:", RegexOptions.IgnoreCase))
            .ToList();
        var profileWords = Tokens(string.Join(' ', roleSkills));
        var ranked = sentences.Select((sentence, index) => new
        {
            Sentence = sentence,
            Index = index,
            Score = Tokens(sentence).Intersect(profileWords).Count() + (index == 0 ? .5 : 0) +
                    (isExecutiveTarget && Regex.IsMatch(sentence,
                        @"\b(led|lead|trained|recruited|built)\b.*\b(team|teams|developers|engineers)\b",
                        RegexOptions.IgnoreCase) ? 4 : 0)
        })
            .OrderByDescending(x => x.Score).ThenBy(x => x.Index).Take(4)
            .OrderBy(x => x.Index).ToList();
        var selected = new List<string>();
        var words = 0;
        foreach (var item in ranked)
        {
            var sentence = item.Sentence;
            var count = Regex.Matches(sentence, @"\b[\p{L}\p{N}][\p{L}\p{N}'’-]*\b").Count;
            if (words + count > MaximumSummaryWords) continue;
            selected.Add(sentence.Trim());
            words += count;
        }
        return selected.Count == 0 ? cleaned : string.Join(' ', selected);
    }

    private static string SelectProjectDescription(string description, IReadOnlySet<string> targetTerms)
    {
        var sentences = Regex.Split(description.Trim(), @"(?<=[.!?])\s+")
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .Select((sentence, index) => new
            {
                Sentence = sentence.Trim(),
                Index = index,
                Score = Tokens(sentence).Intersect(targetTerms).Count() + (index == 0 ? .5 : 0)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .ToList();

        var selected = new List<(string Sentence, int Index)>();
        var wordCount = 0;
        foreach (var item in sentences)
        {
            var sentenceWords = Regex.Matches(item.Sentence, @"\b[\p{L}\p{N}][\p{L}\p{N}'’-]*\b").Count;
            if (selected.Count >= 3 || wordCount + sentenceWords > MaximumProjectDescriptionWords) continue;
            selected.Add((item.Sentence, item.Index));
            wordCount += sentenceWords;
        }

        return selected.Count == 0
            ? description
            : string.Join(' ', selected.OrderBy(item => item.Index).Select(item => item.Sentence));
    }

    internal static IReadOnlyList<Project> SelectRelevantProjects(
        IEnumerable<Project> projects,
        IReadOnlySet<string> targetTerms,
        int maximum = MaximumSelectedProjects)
    {
        if (maximum <= 0 || targetTerms.Count == 0) return [];
        var normalizedTargetTerms = targetTerms.SelectMany(Tokens)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ranked = projects
            .Where(project => !string.IsNullOrWhiteSpace(project.Name) &&
                              !string.IsNullOrWhiteSpace(project.Description) &&
                              (!project.EvidenceMetadata.TryGetValue("eligible_for_personal_evidence", out var eligible) ||
                               !bool.TryParse(eligible, out var mayUse) || mayUse))
            .Select(project => new
            {
                Project = project,
                Score = Tokens(string.Join(' ', new[] { project.Name, project.Description }
                        .Concat(project.Technologies).Where(value => !string.IsNullOrWhiteSpace(value))!))
                    .Intersect(normalizedTargetTerms).Count()
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Project.Name, StringComparer.OrdinalIgnoreCase);
        var selected = new List<Project>();
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in ranked)
        {
            if (!families.Add(ProjectFamily(candidate.Project.Name))) continue;
            selected.Add(candidate.Project);
            if (selected.Count == maximum) break;
        }
        return selected;
    }

    private static List<string> SelectProjectTechnologies(
        IReadOnlyList<string> technologies,
        IReadOnlySet<string> targetTerms) => technologies
        .Select((technology, index) => new
        {
            Technology = technology,
            Index = index,
            IsTargetMatch = Tokens(technology).Overlaps(targetTerms)
        })
        .OrderByDescending(item => item.IsTargetMatch)
        .ThenBy(item => item.Index)
        .Take(12)
        .OrderBy(item => item.Index)
        .Select(item => item.Technology)
        .ToList();

    private static string ProjectFamily(string name)
    {
        var normalized = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "");
        string[] suffixes = ["productisation", "productization", "platform", "engine", "project"];
        foreach (var suffix in suffixes)
            if (normalized.EndsWith(suffix, StringComparison.Ordinal) && normalized.Length > suffix.Length)
                normalized = normalized[..^suffix.Length];
        return normalized;
    }

    private static int ExecutiveLeadershipOrder(string skill) => skill.ToLowerInvariant() switch
    {
        "engineering strategy" => 0,
        "technical direction" => 1,
        "team leadership" => 2,
        "team building and recruitment" => 3,
        "engineering standards" => 4,
        "architecture" => 5,
        "product ownership" => 6,
        "delivery governance" => 7,
        "security and risk" => 8,
        "mentoring" => 9,
        "release management" => 10,
        "stakeholder communication" => 11,
        _ => 20
    };

    private static string FormatDateRange(DateOnly? start, DateOnly? end, bool isCurrent)
    {
        var s = start?.ToString("MMM yyyy") ?? "";
        var e = isCurrent ? "Present" : end?.ToString("MMM yyyy") ?? "";
        if (s == "" && e == "") return "";
        return e == "" ? s : s == "" ? e : $"{s} - {e}";
    }
}

public sealed class CompressedResume
{
    public string Markdown { get; init; } = "";
    public int OriginalRoleCount { get; init; }
    public int IncludedRoleCount { get; init; }
    public int OriginalSkillCount { get; init; }
    public int MatchedSkillCount { get; init; }
    /// <summary>
    /// Semantic coverage of extracted requirement terms. This is not an eligibility
    /// score and does not establish seniority, scale, qualifications, or job fit.
    /// </summary>
    public double OverallFit { get; init; }
    public List<string> Gaps { get; init; } = [];
    public ResumeDocument Projection { get; init; } = new();
}
