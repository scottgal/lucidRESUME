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
    private const int MaximumRelevantRoles = 6;
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

    private readonly ResumeProjectionEditor? _editor;
    private readonly SkillLedgerBuilder _ledgerBuilder;
    private readonly JdSkillLedgerBuilder _jdLedgerBuilder;
    private readonly SkillLedgerMatcher _matcher;
    private readonly IEmbeddingService _embedder;
    private readonly SkillTaxonomyService _taxonomy;
    private readonly ILogger<SemanticCompressor> _logger;
    private readonly HashSet<string> _careerAnchorCompanies;
    private readonly IProseSearch? _proseSearch;

    public SemanticCompressor(
        SkillLedgerBuilder ledgerBuilder,
        JdSkillLedgerBuilder jdLedgerBuilder,
        SkillLedgerMatcher matcher,
        IEmbeddingService embedder,
        SkillTaxonomyService taxonomy,
        IOptions<TailoringOptions> options,
        ILogger<SemanticCompressor> logger, ResumeProjectionEditor? editor = null, IProseSearch? proseSearch = null)
    {
        _proseSearch = proseSearch;
        _editor = editor;
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
        ResumeDocument resume, JobDescription jd, CancellationToken ct = default, IProgress<string>? progress = null,
        ResumeGenerationOptions? generationOptions = null)
    {
        var preferences = generationOptions ?? new ResumeGenerationOptions();
        var budget = ResumeGenerationBudget.Resolve(jd, preferences);
        var sourceLedger = EvidenceLedgerBuilder.EnsureCurrent(resume);
        bool AcceptedRole(WorkExperience role) => Accepted(sourceLedger, $"experience:{role.Id:N}:role");
        var proseIndex = await new CareerProseAnalyser(_embedder).AnalyseAsync(resume, ct);
        var resumeLedger = await _ledgerBuilder.BuildAsync(resume, ct);
        var jdLedger = await _jdLedgerBuilder.BuildAsync(jd, ct);
        var matchResult = await _matcher.MatchAsync(resumeLedger, jdLedger, ct);

        _logger.LogInformation(
            "Compressing resume for {Title} at {Company}: explicit evidence coverage={Coverage:P0}, {Matched}/{Total} requirement terms matched",
            jd.Title, jd.Company, matchResult.OverallFit,
            matchResult.Matches.Count(m => m.IsMatched), matchResult.Matches.Count);

        // Collect the most relevant experience IDs - roles that evidence required skills
        var roleRequirements = new Dictionary<Guid, HashSet<string>>();

        foreach (var match in matchResult.Matches.Where(m => m.IsMatched))
        {
            var ledgerEntry = resumeLedger.Find(match.MatchedResumeSkill!);
            if (ledgerEntry is null) continue;

            foreach (var evidence in ledgerEntry.Evidence)
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
                var similarity = proseIndex.Segments.Where(segment => segment.SubjectId == $"experience:{experience.Id:N}" && segment.Embedding.Length == roleCentroid.Length)
                    .Select(segment => (double)_embedder.CosineSimilarity(roleCentroid, segment.Embedding)).Where(double.IsFinite).DefaultIfEmpty(0).Max();
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

        // Competency retrieval ranks actual role accounts; it never establishes coverage.
        var proseRequirements = new List<ProseRequirement>();
        var queryVectorsAvailable = proseIndex.Segments.Any(segment => segment.Embedding.Length > 0);
        var newQueryVectors = 0;
        var allProseRequirements = ProseJobRequirements.Build(jd);
        var retrievalQueries = ProseJobRequirements.SelectForRetrieval(jd, allProseRequirements, proseIndex);
        foreach (var requirement in retrievalQueries)
        {
            var vector = jdLedger.Requirements.FirstOrDefault(item => string.Equals(item.SkillName, requirement.Text, StringComparison.OrdinalIgnoreCase))?.Embedding ?? [];
            if (vector.Length == 0 && queryVectorsAvailable && newQueryVectors < 24)
            {
                try
                {
                    newQueryVectors++;
                    vector = await _embedder.EmbedAsync(requirement.Text, ct);
                    queryVectorsAvailable = vector.Length > 0 && vector.All(float.IsFinite);
                    if (!queryVectorsAvailable) vector = [];
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { queryVectorsAvailable = false; }
            }
            proseRequirements.Add(new(requirement.Id, requirement.Text, vector));
        }
        if (_proseSearch is not null) await _proseSearch.PrepareAsync(proseIndex, ct);
        var ignored = Tokens(string.Join(' ', new CoverageOptions().StopWords.Concat(new[]
            { "and", "the", "for", "are", "you", "our", "can", "has", "this", "that", "will", "from", "need", "work", "teams", "systems", "platform", "engineering", "technical", "built", "build", "designed", "design", "develop", "lead", "leadership", "production" })));
        // Structured competency extraction is optional. The original job and its
        // responsibilities must still drive evidence selection when no model is available.
        var targetTokens = Tokens(string.Join(' ', new[] { jd.Title ?? "", jd.RawText }
            .Concat(jd.RequiredSkills).Concat(jd.Responsibilities)
            .Concat(jd.Competencies.Where(c => c.Priority != Core.Models.Coverage.RequirementPriority.Preferred)
                .Select(c => c.Name + ": " + c.Description)))).Except(ignored).ToHashSet();
        var roleTokens = resume.Experience.Where(IsUsableRole).Select(role => Tokens(string.Join(' ', role.Achievements))).ToList();
        var isAiTarget = Tokens(jd.Title ?? "").Contains("ai");
        double LexicalRelevance(string text)
        {
            var tokens = Tokens(text).Except(ignored).ToHashSet();
            var score = tokens.Intersect(targetTokens).Sum(token => Math.Log(1 + roleTokens.Count / (1d + roleTokens.Count(role => role.Contains(token))))) /
                Math.Sqrt(Math.Max(1, tokens.Count));
            return score;
        }
        double RetrievalRelevance(string text)
        {
            var score = LexicalRelevance(text);
            // Broad AI titles still need useful offline retrieval of named AI technologies.
            if (isAiTarget && CareerProseLexicalIndex.Terms(text).Any(term =>
                    term is "ai" or "llm" or "llms" or "openai" or "azureopenai" or "agent" or "agents" or "retrieval" or "embeddings")) score += .4;
            return score;
        }
        var relevance = new Dictionary<string, double>(StringComparer.Ordinal);
        var roleSearchResults = new Dictionary<Guid, Dictionary<string, IReadOnlyList<ProseSearchHit>>>();
        var detailRequirements = roleRequirements.ToDictionary(item => item.Key,
            item => (IReadOnlySet<string>)new HashSet<string>(item.Value, StringComparer.OrdinalIgnoreCase));
        var primaryRequirementIds = allProseRequirements.Where(item => item.Kind != lucidRESUME.Compiler.RequirementKind.Preferred)
            .Select(item => item.Id).ToHashSet();
        var segmentSubjects = proseIndex.Segments.ToDictionary(segment => segment.Id, segment => segment.SubjectId);
        var corpusResults = new Dictionary<string, IReadOnlyList<ProseSearchHit>>();
        if (_proseSearch is not null)
            foreach (var query in proseRequirements)
                corpusResults[query.Id] = await _proseSearch.SearchAsync(new ProseSearchQuery(
                    proseIndex.SearchRevision, query.Text, Kind: "achievement",
                    Embedding: query.Embedding, EmbeddingSpace: proseIndex.EmbeddingSpace, Maximum: 256), ct);
        foreach (var role in resume.Experience.Where(IsUsableRole).Where(AcceptedRole))
        {
            var hits = new Dictionary<string, IReadOnlyList<ProseSearchHit>>();
            foreach (var query in proseRequirements)
                hits[query.Id] = (corpusResults.GetValueOrDefault(query.Id) ?? [])
                    .Where(hit => segmentSubjects.GetValueOrDefault(hit.SegmentId) == $"experience:{role.Id:N}").ToList();
            roleSearchResults[role.Id] = hits;
            var accounts = CareerProseRetriever.Select(proseIndex, proseRequirements, $"experience:{role.Id:N}",
                Math.Max(12, budget.AchievementsPerRole * 3), segment => RetrievalRelevance(segment.Text), hits);
            foreach (var account in accounts) relevance[account.Segment.Text] = account.Score;
            // These are allocation hints, never declarations that the candidate meets a requirement.
            detailRequirements[role.Id] = (detailRequirements.GetValueOrDefault(role.Id) ?? new HashSet<string>())
                .Concat(accounts.SelectMany(account => account.RequirementIds).Where(primaryRequirementIds.Contains))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (proseRequirements.Count > 0)
                roleScores[role.Id] = roleScores.GetValueOrDefault(role.Id) +
                    accounts.Take(2).Select(account => account.Score).DefaultIfEmpty(0).Average() * 24;
        }

        // A current specialist role deserves detail when its title explicitly matches the target.
        var targetTitleTerms = Tokens(jd.Title ?? "").Except(Tokens("head engineering engineer developer senior lead chief director applied"));
        foreach (var role in resume.Experience.Where(IsUsableRole).Where(role => role.IsCurrent))
            roleScores[role.Id] = roleScores.GetValueOrDefault(role.Id) +
                Tokens(role.Title ?? "").Intersect(targetTitleTerms).Count() * 12;

        var selectedRoleIds = SelectDetailedRoleIds(
            resume.Experience.Where(AcceptedRole).ToList(), roleScores, _careerAnchorCompanies, budget.Roles, detailRequirements);
        var currentSpecialist = resume.Experience.Where(IsUsableRole).Where(AcceptedRole).Where(role => role.IsCurrent &&
                Tokens(role.Title ?? "").Intersect(targetTitleTerms).Any())
            .OrderByDescending(role => role.StartDate).FirstOrDefault();
        if (currentSpecialist is not null && !selectedRoleIds.Contains(currentSpecialist.Id))
        {
            var replaceable = resume.Experience.Where(role => selectedRoleIds.Contains(role.Id) &&
                    !IsCareerAnchor(role, _careerAnchorCompanies))
                .OrderBy(role => roleScores.GetValueOrDefault(role.Id)).FirstOrDefault();
            if (replaceable is not null) selectedRoleIds.Remove(replaceable.Id);
            selectedRoleIds.Add(currentSpecialist.Id);
        }
        if (proseRequirements.Count > 0)
        {
            // A role needed for chronology need not spend a bullet on off-target work.
            var withoutJobEvidence = resume.Experience.Where(role => selectedRoleIds.Contains(role.Id) &&
                !IsCareerAnchor(role, _careerAnchorCompanies) && role.Id != currentSpecialist?.Id &&
                (detailRequirements.GetValueOrDefault(role.Id)?.Count ?? 0) == 0 &&
                !proseIndex.Segments.Any(segment => segment.SubjectId == $"experience:{role.Id:N}" &&
                    relevance.GetValueOrDefault(segment.Text) > 0)).Select(role => role.Id).ToHashSet();
            if (withoutJobEvidence.Count == selectedRoleIds.Count)
            {
                var newest = resume.Experience.Where(role => selectedRoleIds.Contains(role.Id))
                    .OrderByDescending(role => role.StartDate).FirstOrDefault();
                if (newest is not null) withoutJobEvidence.Remove(newest.Id);
            }
            selectedRoleIds.ExceptWith(withoutJobEvidence);
        }
        var experiencePlan = ExperienceDetailPlan.Build(jd,
            resume.Experience.Where(IsUsableRole).Where(AcceptedRole).ToList(), selectedRoleIds, roleScores,
            detailRequirements,
            budget, DateOnly.FromDateTime(DateTime.UtcNow));
        var anchorCount = resume.Experience.Count(experience =>
            selectedRoleIds.Contains(experience.Id) && IsCareerAnchor(experience, _careerAnchorCompanies));
        if (anchorCount > 0)
            _logger.LogInformation("Retained {Count} author-selected career anchor role(s)", anchorCount);

        var projection = ResumeDocument.Create(resume.FileName, resume.ContentType, resume.FileSizeBytes);
        projection.GenerationOptions = preferences;
        projection.MinimumOutputPages = 1; // Content flows naturally; no artificial page quota.
        projection.Personal = new PersonalInfo
        {
            FullName = Accepted(sourceLedger, "personal:name") ? resume.Personal.FullName : null,
            Email = resume.Personal.Email,
            Phone = resume.Personal.Phone,
            ContactPreference = resume.Personal.ContactPreference,
            Location = resume.Personal.Location,
            LinkedInUrl = CleanProfileUrl(resume.Personal.LinkedInUrl),
            GitHubUrl = resume.Personal.GitHubUrl,
            WebsiteUrl = resume.Personal.WebsiteUrl,
            Summary = Accepted(sourceLedger, "personal:summary") ? resume.Personal.Summary : null
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
        if (!string.IsNullOrEmpty(projection.Personal.LinkedInUrl)) contactParts.Add(projection.Personal.LinkedInUrl);
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

        var invalidRoleCount = resume.Experience.Count(role => !RoleEvidenceQuality.HasRoleIdentity(role));
        if (invalidRoleCount > 0)
            projection.GenerationWarnings.Add($"{invalidRoleCount} imported role entries have missing or malformed title/company fields. Retained in the source transcript; excluded from resume selection and role interviews until reviewed.");

        // Insert the summary after selecting role evidence, so its source facts follow the job.
        var summaryInsertion = md.Length;
        string? careerSummary = null;
        if (!string.IsNullOrWhiteSpace(resume.Personal.Summary) && Accepted(sourceLedger, "personal:summary"))
        {
            var summaryTerms = jd.RequiredSkills.Concat(jd.PreferredSkills).Concat(targetTokens)
                .Concat(roleProfile is null ? [] : _taxonomy.GetRoleSkills(roleProfile))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var summary = SelectSummary(CleanSummary(resume.Personal.Summary), summaryTerms,
                roleProfile is "CTO" or "VP of Engineering" or "Head of Engineering", budget.SummaryWords);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                careerSummary = summary;
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
            var unresolvedStatus = RoleEvidenceQuality.NeedsStatusClarification(exp);
            if (unresolvedStatus) projection.GenerationWarnings.Add($"Verify delivery and cancellation chronology for {exp.Title} at {exp.Company}; status-sensitive accounts remain in the full transcript.");
            var fullTextMatches = roleSearchResults.GetValueOrDefault(exp.Id);
            if (_proseSearch is not null && fullTextMatches is not null)
                foreach (var query in proseRequirements.Where(query => corpusResults.GetValueOrDefault(query.Id)?.Count >= 256))
                    fullTextMatches[query.Id] = await _proseSearch.SearchAsync(new ProseSearchQuery(
                        proseIndex.SearchRevision, query.Text, $"experience:{exp.Id:N}", "achievement",
                        query.Embedding, proseIndex.EmbeddingSpace), ct);
            var retrieved = CareerProseRetriever.Select(proseIndex, proseRequirements,
                $"experience:{exp.Id:N}", Math.Max(experiencePlan.AchievementLimits.GetValueOrDefault(exp.Id, 1) * 3, 12),
                segment => relevance.GetValueOrDefault(segment.Text), fullTextMatches);
            foreach (var item in retrieved)
            {
                var achievement = item.Segment.Text;
                if (RoleEvidenceQuality.HasConversationalAside(achievement) ||
                    unresolvedStatus && RoleEvidenceQuality.IsStatusSensitive(achievement)) continue;
                if (relevantAchievements.All(existing => !NearDuplicate(existing, achievement)))
                    relevantAchievements.Add(achievement);
            }

            var isCareerAnchor = IsCareerAnchor(exp, _careerAnchorCompanies);
            if (isCareerAnchor ||
                roleProfile is "CTO" or "VP of Engineering" or "Head of Engineering")
            {
                var definingAchievement = exp.Achievements.FirstOrDefault(text => proseIndex.Segments.Any(segment =>
                        segment.SubjectId == $"experience:{exp.Id:N}" && segment.Text == text) && IsCareerDefiningAchievement(text) &&
                    !RoleEvidenceQuality.HasConversationalAside(text) && (!unresolvedStatus || !RoleEvidenceQuality.IsStatusSensitive(text)));
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
                Location = CleanRoleLocation(exp.Location),
                StartDate = exp.StartDate,
                EndDate = exp.EndDate,
                EndDateIsMonthOnly = exp.EndDateIsMonthOnly,
                IsCurrent = exp.IsCurrent,
                IsCareerAnchor = IsCareerAnchor(exp, _careerAnchorCompanies),
                Technologies = exp.Technologies.Where(name => sourceLedger.Claims.Any(claim => claim.Review == "accepted" &&
                    claim.SubjectId == $"experience:{exp.Id:N}" && claim.Kind == "technology" && claim.Statement.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList(),
                ImportSources = [.. exp.ImportSources]
            };
            var paragraph = string.IsNullOrEmpty(dateRange) ? 1 : 2;
            var maximumAchievements = experiencePlan.AchievementLimits.GetValueOrDefault(exp.Id, 1);
            var packets = ProseSynthesisAssembler.Assemble(relevantAchievements, targetTokens,
                preferences.Detail == ResumeDetail.Thorough ? 3 : 2);
            foreach (var facts in packets.Take(maximumAchievements))
            {
                var a = string.Join(" ", facts.Select(fact => fact.Trim().TrimEnd('.') + "."));
                if (facts.Count == 1) a = facts[0];
                md.AppendLine($"- {a}");
                md.AppendLine();
                projectedExperience.Achievements.Add(a);
                foreach (var fact in facts)
                {
                    // Exact duplicate source accounts retain every claim/support identity.
                    var segment = proseIndex.Segments.FirstOrDefault(segment => segment.SubjectId == $"experience:{exp.Id:N}" && segment.Text == fact);
                    if (segment is not null)
                        foreach (var claimId in segment.ClaimIds.Where(id => sourceLedger.Claims.Any(claim => claim.Id == id && claim.Review == "accepted")))
                            projection.Projection!.Blocks.Add(new ResumeProjectionBlock
                            { ClaimId = claimId, ProseRef = $"#experience-{exp.Id:N}:p{paragraph}" });
                }
                paragraph++;
            }
            projection.Experience.Add(projectedExperience);
            includedRoles++;

        }

        if (careerSummary is not null)
        {
            var selectedAccountsForSummary = projection.Experience.SelectMany(role => role.Achievements.Select(text =>
                    (Role: role, Text: text, Relevance: LexicalRelevance(text))))
                .Where(item => item.Relevance > 0 && Regex.Matches(item.Text, @"\S+").Count <= budget.SummaryWords / 2)
                // Qualified accounts stay in Experience, where their complete status remains visible.
                .Where(item => ResumeSummaryBuilder.IsUnqualifiedAccount(item.Text))
                .OrderByDescending(item => item.Relevance).Take(2).ToList();
            var summary = careerSummary;
            if (selectedAccountsForSummary.Count >= 1)
            {
                var focusSkills = jd.RequiredSkills.Concat(jd.PreferredSkills)
                    .Where(skill => selectedAccountsForSummary.Any(item => SkillTaxonomy.MentionsEquivalent(item.Text, skill)))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
                var focusAccount = ResumeSummaryBuilder.FindFocusAccount(selectedAccountsForSummary.Select(item => item.Text), focusSkills);
                summary = ResumeSummaryBuilder.Build(careerSummary, focusSkills,
                    focusSkills.Count > 0 && focusSkills.All(skill => SkillTaxonomy.MentionsEquivalent(careerSummary, skill)) &&
                    !lucidRESUME.Compiler.CompositionValidator.RepeatsSentence(careerSummary,
                        projection.Experience.SelectMany(role => role.Achievements).ToList()), focusAccount);
                if (summary.Contains(careerSummary, StringComparison.Ordinal) || ResumeSummaryBuilder.UsesIdentity(summary, careerSummary))
                    Bind(projection, sourceLedger, "personal:summary", "#summary:p1");
                foreach (var item in summary == careerSummary ? [] : selectedAccountsForSummary.Where(item => focusAccount is null || focusAccount.Sources.Contains(item.Text)))
                {
                    var selectedParagraph = projection.Experience.Single(role => role.Id == item.Role.Id).Achievements.IndexOf(item.Text);
                    var firstParagraph = string.IsNullOrEmpty(FormatDateRange(item.Role.StartDate, item.Role.EndDate, item.Role.IsCurrent)) ? 1 : 2;
                    var proseRef = $"#experience-{item.Role.Id:N}:p{selectedParagraph + firstParagraph}";
                    foreach (var binding in projection.Projection.Blocks.Where(block => block.ProseRef == proseRef &&
                                 sourceLedger.Claims.Any(claim => claim.Id == block.ClaimId && claim.Review == "accepted" &&
                                     (focusAccount is null ? focusSkills.Any(skill => SkillTaxonomy.MentionsEquivalent(claim.Statement, skill))
                                         : ResumeSummaryBuilder.FocusPhrase(claim.Statement) is { } phrase && focusAccount.Sources.Any(source => ResumeSummaryBuilder.FocusPhrase(source) == phrase) ||
                                           focusAccount.RemainingSkills.Any(skill => SkillTaxonomy.MentionsEquivalent(claim.Statement, skill))))).ToList())
                        projection.Projection.Blocks.Add(new() { ClaimId = binding.ClaimId, ProseRef = "#summary:p1" });
                }
            }
            else Bind(projection, sourceLedger, "personal:summary", "#summary:p1");
            projection.Personal.Summary = summary;
            md.Insert(summaryInsertion, $"## Summary {{#summary}}\n{summary}\n\n");
        }

        var earlierRoles = new List<WorkExperience>();
        var chronology = resume.Experience.Where(role => selectedRoleIds.Contains(role.Id)).ToList();
        foreach (var role in experiencePlan.ChronologyRoleIds.Select(id => resume.Experience.Single(role => role.Id == id)))
        {
            if (chronology.Any(existing => PossibleDuplicateChronology(existing, role)))
            {
                projection.GenerationWarnings.Add($"Review possible duplicate employment entry: {role.Title} at {role.Company}. Retained in the full transcript; omitted from compact chronology.");
                continue;
            }
            earlierRoles.Add(role);
            chronology.Add(role);
        }
        var evidencedYears = ExperienceDetailPlan.CoveredYears(chronology, DateOnly.FromDateTime(DateTime.UtcNow));
        if (experiencePlan.RequiredYears is { } requiredYears && evidencedYears + .01 < requiredYears)
            projection.GenerationWarnings.Add($"The job requests {requiredYears} years of experience; accepted dated roles substantiate {evidencedYears:F1} non-overlapping years. Missing dates or experience can be added to the complete transcript.");
        earlierRoles = earlierRoles.OrderByDescending(role => role.StartDate).ToList();
        if (earlierRoles.Count > 0)
        {
            md.AppendLine("## Additional Experience {#additional-experience}");
            for (var i = 0; i < earlierRoles.Count; i++)
            {
                var role = earlierRoles[i];
                md.AppendLine($"- **{role.Title}**, {role.Company} ({FormatDateRange(role.StartDate, role.EndDate, role.IsCurrent)})");
                md.AppendLine();
                Bind(projection, sourceLedger, $"experience:{role.Id:N}:role", $"#additional-experience:p{i + 1}");
                projection.Experience.Add(new WorkExperience
                {
                    Id = role.Id,
                    Title = role.Title,
                    Company = role.Company,
                    StartDate = role.StartDate,
                    EndDate = role.EndDate,
                    EndDateIsMonthOnly = role.EndDateIsMonthOnly,
                    IsCurrent = role.IsCurrent,
                    IsCompact = true
                });
            }
        }

        var projectTerms = jd.RequiredSkills.Concat(jd.PreferredSkills)
            .Concat(roleProfile is null ? [] : _taxonomy.GetRoleSkills(roleProfile))
            .SelectMany(Tokens)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedProjects = SelectRelevantProjects(resume.Projects.Where(project =>
            Accepted(sourceLedger, $"project:{project.Id:N}:description")).ToList(), projectTerms, budget.Projects);

        if (selectedProjects.Count > 0)
        {
            md.AppendLine("## Selected Projects {#projects}");
            md.AppendLine();
            foreach (var project in selectedProjects)
            {
                var anchor = $"project-{project.Id:N}";
                var selectedDescription = SelectProjectDescription(project.Description!, projectTerms, budget.ProjectWords);
                var selectedTechnologies = SelectProjectTechnologies(project.Technologies.Where(name => sourceLedger.Claims.Any(claim =>
                    claim.Review == "accepted" && claim.SubjectId == $"project:{project.Id:N}" &&
                    claim.Concepts.Contains(name, StringComparer.OrdinalIgnoreCase))).ToList(), projectTerms);
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

        var related = RelatedWorkSelector.Select(resume, jd, selectedProjects.Select(project => project.Id), 20);
        var relatedProjects = related.Where(link => link.Kind == "project")
            .Take(preferences.Length == ResumeLength.Concise ? 2 : 4).ToList();
        if (relatedProjects.Count > 0)
        {
            md.AppendLine("## Related projects {#related-projects}");
            md.AppendLine();
            for (var i = 0; i < relatedProjects.Count; i++)
            {
                var link = relatedProjects[i];
                md.AppendLine($"- [{link.Name}]({link.Url}) — {link.Relevance}");
                md.AppendLine();
                foreach (var claimId in link.ClaimIds)
                    projection.Projection!.Blocks.Add(new ResumeProjectionBlock { ClaimId = claimId, ProseRef = $"#related-projects:p{i + 1}" });
                projection.RelatedWork.Add(link);
            }
        }
        var publicationLinks = related.Where(link => link.Kind == "publication")
            .Take(preferences.Length == ResumeLength.Concise ? 1 : 3).ToList();
        var selectedPublications = publicationLinks.Select(link => resume.Publications.Single(item => item.Id == link.SourceId)).ToList();
        if (selectedPublications.Count > 0)
        {
            md.AppendLine("## Selected Recent Publications {#publications}");
            md.AppendLine();
            for (var i = 0; i < selectedPublications.Count; i++)
            {
                var publication = selectedPublications[i];
                md.AppendLine($"- [{publication.Name}]({publication.Url})");
                md.AppendLine();
                Bind(projection, sourceLedger, $"publication:{publication.Id:N}", $"#publications:p{i + 1}");
                projection.Publications.Add(publication);
                projection.RelatedWork.Add(publicationLinks[i]);
            }
        }

        // Project only ledger-backed, specific skills. Concepts demonstrated by the
        // selected prose are included even when the JD matcher chose a broader alias.
        md.AppendLine("## Skills {#skills}");
        var matchedSkillNames = matchResult.Matches
            .Where(m => m.IsMatched)
            .Select(m => resumeLedger.Find(m.MatchedResumeSkill!))
            .Where(e => e is not null)
            .Select(entry => entry!.SkillName).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        var selectedAccounts = projection.Experience.SelectMany(role => role.Achievements).ToList();
        var explicitSkills = resume.Skills.Where(skill => Accepted(sourceLedger, EvidenceLedgerBuilder.SkillLocator(skill.Name)) &&
            selectedAccounts.Any(account => SkillTaxonomy.MentionsEquivalent(account, skill.Name))).Select(skill => skill.Name);
        var declaredAiSkills = jd.Title?.Contains("AI", StringComparison.OrdinalIgnoreCase) == true
            ? resume.Skills.Where(skill => Accepted(sourceLedger, EvidenceLedgerBuilder.SkillLocator(skill.Name)) &&
                Regex.IsMatch(skill.Category ?? "", @"\b(AI|ML)\b", RegexOptions.IgnoreCase)).Select(skill => skill.Name)
            : [];
        var relevantAccountConcepts = projection.Projection.Blocks.Select(block => sourceLedger.Claims.FirstOrDefault(claim => claim.Id == block.ClaimId))
            .Where(claim => claim is not null && Tokens(claim.Statement).Except(ignored).Intersect(targetTokens).Count() >= 2)
            .SelectMany(claim => claim!.Concepts).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobSkillContext = string.Join(' ', new[] { jd.Title ?? "", jd.RawText }.Concat(jd.RequiredSkills).Concat(jd.PreferredSkills).Concat(jd.Responsibilities));
        var skillsByCategory = priorityLeadership.Concat(matchedSkillNames).Concat(demonstratedConcepts)
            .Concat(explicitSkills).Concat(declaredAiSkills).Concat(relevantAccountConcepts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => matchedSkillNames.Contains(name) ||
                SkillTaxonomy.MentionsEquivalent(jobSkillContext, name) || priorityLeadership.Contains(name) || declaredAiSkills.Contains(name))
            .Where(name => !GenericSkillLabels.Contains(name))
            .Where(name => sourceLedger.Claims.Any(claim => claim.Review == "accepted" && claim.Kind is "skill" or "technology" &&
                claim.Statement.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Select(name => resume.Skills.FirstOrDefault(skill =>
                string.Equals(skill.Name, name, StringComparison.OrdinalIgnoreCase)) ??
                (sourceLedger.Evidence.Any(e => e.Kind == "technology" && e.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ? new Skill { Name = name, Category = "Role tools" } : null))
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
                var claim = sourceLedger.Claims.FirstOrDefault(claim => claim.Review == "accepted" && claim.Kind is "skill" or "technology" &&
                    claim.Statement.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
                if (claim is not null) projection.Projection!.Blocks.Add(new() { ClaimId = claim.Id, ProseRef = $"#skills:p{skillParagraph}" });
            }
            skillParagraph++;
        }

        // Education
        if (resume.Education.Count > 0)
        {
            md.AppendLine("## Education {#education}");
            var educationParagraph = 1;
            foreach (var edu in resume.Education.Where(e => !string.IsNullOrWhiteSpace(e.Degree) && Accepted(sourceLedger, $"education:{e.Id:N}")))
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

        if (_editor is not null) await _editor.EditAsync(resume, projection, jd, ct, progress);

        return new CompressedResume
        {
            Markdown = projection.RawMarkdown ?? compressedMd,
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
        var claim = FindSourceClaim(ledger, locator);
        if (claim is null) return;
        projection.Projection!.Blocks.Add(new ResumeProjectionBlock { ClaimId = claim.Id, ProseRef = proseRef });
    }

    private static bool PossibleDuplicateChronology(WorkExperience left, WorkExperience right)
    {
        if (left.StartDate is null || left.StartDate != right.StartDate || left.EndDate != right.EndDate) return false;
        var generic = new HashSet<string>(["ltd", "limited", "inc", "corp", "company", "com", "the", "very"]);
        var leftNames = Regex.Matches(left.Company ?? "", @"[\p{L}]{4,}").Select(match => match.Value.ToLowerInvariant()).Except(generic);
        var rightNames = Regex.Matches(right.Company ?? "", @"[\p{L}]{4,}").Select(match => match.Value.ToLowerInvariant()).Except(generic);
        return leftNames.Intersect(rightNames).Any();
    }

    private static bool NearDuplicate(string left, string right) =>
        Regex.Replace(left, @"\s+", " ").Trim().Equals(Regex.Replace(right, @"\s+", " ").Trim(), StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> Tokens(string text) => text.ToLowerInvariant()
        .Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '(', ')', '/', '-'], StringSplitOptions.RemoveEmptyEntries)
        .Where(token => token.Length > 2 || ShortTechnicalTokens.Contains(token))
        .Select(token => token.Length > 7 ? token[..6] : token.TrimEnd('s'))
        .ToHashSet(StringComparer.Ordinal);

    private static bool IsUsableRole(WorkExperience experience)
    {
        if (experience.Achievements.Count == 0) return false;
        if (!RoleEvidenceQuality.HasRoleIdentity(experience)) return false;
        if (experience.ImportSources.Contains("LLM extraction", StringComparer.OrdinalIgnoreCase)) return false;
        return true;
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
        int maximumRelevantRoles = MaximumRelevantRoles,
        IReadOnlyDictionary<Guid, IReadOnlySet<string>>? requirementHints = null)
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
        var represented = anchors.SelectMany(role => requirementHints?.GetValueOrDefault(role.Id) ?? new HashSet<string>()).ToHashSet();
        var remaining = Math.Max(0, maximumRelevantRoles);
        var candidates = usable.Where(role => !selected.Contains(role.Id)).ToList();
        while (remaining > 0 && candidates.Count > 0)
        {
            var role = candidates.OrderByDescending(role => roleScores.GetValueOrDefault(role.Id) +
                    12 * (requirementHints?.GetValueOrDefault(role.Id) ?? new HashSet<string>()).Count(id => !represented.Contains(id)))
                .ThenByDescending(role => role.StartDate).First();
            candidates.Remove(role);
            if (usable.Any(existing => selected.Contains(existing.Id) && PossibleDuplicateChronology(existing, role))) continue;
            selected.Add(role.Id);
            represented.UnionWith(requirementHints?.GetValueOrDefault(role.Id) ?? new HashSet<string>());
            remaining--;
        }
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

    private static LedgerClaim? FindSourceClaim(EvidenceLedger ledger, string locator) => ledger.Claims.FirstOrDefault(claim =>
        claim.Id.EndsWith(":" + locator, StringComparison.OrdinalIgnoreCase));

    private static bool Accepted(EvidenceLedger ledger, string locator) =>
        string.Equals(FindSourceClaim(ledger, locator)?.Review, "accepted", StringComparison.OrdinalIgnoreCase);

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

    internal static string? CleanRoleLocation(string? value) => string.IsNullOrWhiteSpace(value) ? value :
        Regex.Replace(value, @"\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{4}\s*[-–—]\s*(?:(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{4}|Present|Current)\s*$", "", RegexOptions.IgnoreCase).Trim();

    internal static string? CleanProfileUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var url = value.Contains("://") ? value : "https://" + value;
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
            parsed.Host.EndsWith("linkedin.com", StringComparison.OrdinalIgnoreCase) &&
            parsed.AbsolutePath.Trim('/').Equals("in", StringComparison.OrdinalIgnoreCase)) return null;
        return value;
    }

    internal static string CleanSummary(string summary)
    {
        // Some importers flatten the document header into the profile. Strip only the
        // labelled contact prefix through its final URL, retaining the following prose.
        if (Regex.IsMatch(summary, @"^\s*(Email|Phone|Blog|GitHub|LinkedIn|Website)\s*:", RegexOptions.IgnoreCase))
        {
            var contacts = Regex.Matches(summary, @"\b(?:Blog|GitHub|Website|LinkedIn)\s*:\s*[^\s|]+", RegexOptions.IgnoreCase);
            if (contacts.Count > 0)
            {
                var last = contacts[^1];
                var remainder = summary[(last.Index + last.Length)..].Trim(' ', '|');
                if (remainder.Length > 40) return remainder;
            }
        }
        return summary;
    }

    private static string SelectSummary(string summary, IReadOnlySet<string> roleSkills, bool isExecutiveTarget, int maximumWords = MaximumSummaryWords)
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
            .OrderByDescending(x => x.Score).ThenBy(x => x.Index).Take(Math.Max(4, maximumWords / 25))
            .OrderBy(x => x.Index).ToList();
        var selected = new List<string>();
        var words = 0;
        foreach (var item in ranked)
        {
            var sentence = item.Sentence;
            var count = Regex.Matches(sentence, @"\b[\p{L}\p{N}][\p{L}\p{N}'’-]*\b").Count;
            if (words + count > maximumWords) continue;
            selected.Add(sentence.Trim());
            words += count;
        }
        return selected.Count == 0 ? cleaned : string.Join(' ', selected);
    }

    private static string SelectProjectDescription(string description, IReadOnlySet<string> targetTerms, int maximumWords = MaximumProjectDescriptionWords)
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
            if (selected.Count >= Math.Max(3, maximumWords / 30) || wordCount + sentenceWords > maximumWords) continue;
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
            .Except(Tokens("platform project system engineering build develop"))
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
