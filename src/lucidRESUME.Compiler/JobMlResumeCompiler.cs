using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.JobML;

namespace lucidRESUME.Compiler;

public sealed class JobMlResumeCompiler(
    IJobSpecParser jobParser,
    ResumeCompositionOrchestrator composition,
    IEmbeddingService? embeddings = null) : IJobMlCompiler
{
    private static readonly Regex TokenPattern = new(@"[\p{L}\p{N}][\p{L}\p{N}+#.-]{1,}", RegexOptions.Compiled);

    public async Task<CompilationResult> CompileAsync(
        JobMlSnapshot completeResume,
        string jobDescription,
        CompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completeResume);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobDescription);
        options ??= new CompilationOptions();

        var job = await jobParser.ParseFromTextAsync(jobDescription, cancellationToken);
        var requirements = BuildRequirements(job.RequiredSkills, job.PreferredSkills, job.Responsibilities,
            job.RequiredEducation);
        if (requirements.Count == 0)
            requirements = ExtractFallbackRequirements(jobDescription);

        var reconciled = JobMlProcessor.Reconcile(completeResume.File)
            .ToDictionary(x => x.Claim.Id, StringComparer.OrdinalIgnoreCase);
        var concepts = BuildConceptTerms(completeResume.File.Data.Concepts);
        var entities = completeResume.File.Data.Entities.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var sourceMarkdown = MarkdownSourceChunkIndex.Create(completeResume.File.Markdown).Reassemble();
        var index = MarkdownEvidenceIndex.Create(sourceMarkdown);
        var subjectAffinities = await BuildSubjectAffinitiesAsync(
            job.Title ?? jobDescription, completeResume.File.Data.RoleCentroids, entities, cancellationToken);

        var accepted = completeResume.File.Data.Claims
            .Where(IsAccepted)
            .Where(c => reconciled.TryGetValue(c.Id, out var resolution) && resolution.Evidence.Count > 0 &&
                        resolution.Evidence.All(e => e.State is EvidenceState.Valid or EvidenceState.External))
            .ToList();
        var duplicateGenericSubjects = FindDuplicateExperienceSubjects(accepted, entities);
        var subjectsWithNarrative = accepted
            .Where(claim => claim.Type is "achievement" or "project" or "summary")
            .Select(claim => claim.Subject)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = accepted
            .Where(IsResumeNarrative)
            .Where(claim => !duplicateGenericSubjects.Contains(claim.Subject))
            .Where(claim => claim.Type != "experience" || !subjectsWithNarrative.Contains(claim.Subject))
            .ToList();

        var matches = new List<ClaimMatch>();
        foreach (var requirement in requirements)
            foreach (var claim in candidates)
            {
                var subjectName = entities.GetValueOrDefault(claim.Subject)?.Name ?? claim.Subject;
                var (score, reason, direct) = await ScoreAsync(requirement, claim, subjectName, concepts, cancellationToken);
                if (subjectAffinities.TryGetValue(claim.Subject, out var roleAffinity))
                {
                    score = score * .75 + roleAffinity * .25;
                    if (roleAffinity >= .9 && claim.Type == "achievement")
                        score = Math.Max(score, options.RelatedThreshold + .08);
                    reason += $"; role affinity {roleAffinity:F2}";
                }
                if (score >= options.RelatedThreshold)
                    matches.Add(new ClaimMatch(requirement.Id, claim.Id,
                        direct ? MatchKind.Direct : MatchKind.Related, score, reason));
            }

        var careerAnchorSubjects = entities.Values
            .Where(IsCareerAnchor)
            .Select(entity => entity.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = SelectClaims(candidates, accepted, matches, reconciled, entities, index, options,
            careerAnchorSubjects, job.Title, requirements);
        var sections = selected.GroupBy(x => x.Claim.Subject, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Any(item => item.Claim.Type == "summary"))
            .ThenByDescending(group => FindExperiencePeriod(group.Key, accepted)?.SortEnd ?? DateOnly.MinValue)
            .ThenByDescending(group => FindExperiencePeriod(group.Key, accepted)?.Start ?? DateOnly.MinValue)
            .ThenByDescending(group => group.Max(item => item.Score))
            .Select((group, number) => BuildEvidencePacket(group, number, requirements, accepted))
            .ToList();
        if (options.IncludeAdditionalExperience)
        {
            var additional = BuildAdditionalExperiencePackets(accepted, selected, entities,
                duplicateGenericSubjects, options.MinimumAdditionalExperienceMonths,
                options.MaximumAdditionalExperienceEntries, sections.Count);
            sections.AddRange(additional);
            selected.AddRange(additional.SelectMany(packet => packet.Claims));
        }
        var gaps = requirements.Where(r => matches.All(m => m.RequirementId != r.Id || m.Kind == MatchKind.None))
            .Select(r => r.Text).ToList();
        var manifest = new ProjectionManifest(
            completeResume.Revision,
            Hash(jobDescription),
            DateTimeOffset.UtcNow,
            requirements,
            sections,
            matches,
            gaps,
            embeddings is null ? "lexical" : "configured")
        {
            TargetTitle = job.Title
        };

        var composed = await composition.ComposeAsync(manifest, jobDescription, options, cancellationToken);
        var human = RenderHumanMarkdown(sourceMarkdown, composed.Blocks, sections, job.Title,
            selected, completeResume.File.Data.Concepts, requirements);
        var projected = BuildProjection(completeResume.File, human, composed.Blocks, sections, selected,
            options.FullJobMlUri);
        var full = JobMlArtifactComposer.Compose(projected);
        var published = CJobMlProjector.Project(projected).Markdown;
        return new CompilationResult(Guid.NewGuid().ToString("N"), manifest, human, published, full,
            projected, composed.Used, composed.Provider, composed.Warnings);
    }

    private static EvidencePacket BuildEvidencePacket(
        IGrouping<string, SelectedClaim> group,
        int number,
        IReadOnlyList<CompilerRequirement> requirements,
        IReadOnlyList<JobMlClaim> acceptedClaims)
    {
        var groupClaims = group.ToList();
        var hasNarrative = groupClaims.Any(item => item.Claim.Type != "experience");
        var subject = group.Key;
        var isDatedExperience = acceptedClaims.Any(item =>
            item.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase) && item.Type == "experience");
        var rankedClaims = groupClaims
            // Date claims establish chronology and the heading, but are not resume
            // prose when the same subject has a reviewed narrative passage.
            .Where(item => item.Claim.Type != "experience" || !hasNarrative)
            .OrderByDescending(item => item.Matches.Select(match => match.RequirementId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count())
            .ThenByDescending(item => item.Score)
            .ThenByDescending(item => RoleFramingClaimSignal(item.Claim.Statement))
            .ToList();
        var initialRequirementIds = rankedClaims.SelectMany(x => x.Matches)
            .Select(x => x.RequirementId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var allFocus = requirements
            .Where(requirement => initialRequirementIds.Contains(requirement.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(requirement => requirement.Kind)
            .Select(requirement => requirement.Text)
            .ToList();
        var focus = allFocus.Take(6).ToList();
        var isSummary = rankedClaims.Any(item => item.Claim.Type == "summary");
        var isProject = !isDatedExperience && rankedClaims.Any(item => item.Claim.Type == "project");
        var isEducation = rankedClaims.Any(item => item.Claim.Type == "education");
        var isPublication = !isDatedExperience && rankedClaims.Any(item => item.Claim.Type == "publication");
        // A senior summary needs enough room for identity, the vacancy-specific
        // differentiator and the relevant implementation/leadership context. At
        // 72 words the sentence-preserving compactor commonly retained identity
        // and stack but dropped the differentiator (for example daily agent use).
        var targetWords = isSummary ? 80 : isEducation ? 36 : isPublication ? 60 : isProject ? 60 :
            48 + Math.Max(0, rankedClaims.Count - 1) * 20;
        var aiNarrative = isDatedExperience && rankedClaims.Count(item => HasAiContent(item.Prose)) >= 2;
        targetWords = Math.Min(targetWords, isSummary ? 80 : aiNarrative ? 100 : 76);
        var claims = FitHumanProse(rankedClaims, targetWords, allFocus, isSummary);
        var requirementIds = claims.SelectMany(x => x.Matches)
            .Select(x => x.RequirementId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sourceWords = claims.Sum(item => WordCount(item.Prose));
        var editingAllowance = isSummary || isPublication ? 0 : 5;
        var maximumWords = Math.Clamp(Math.Max(24, sourceWords + editingAllowance), 24,
            isSummary ? 80 : aiNarrative ? 100 : 76);
        var intent = isSummary
            ? "Write one plain, specific professional summary for this vacancy. Keep the candidate's vocabulary and omit generic aspiration or self-praise."
            : "Write one compact role passage, not a catalogue of duties. Lead with the outcome most relevant to this vacancy, retain the substance of each selected claim, combine overlapping detail, and omit unrelated context.";
        if (!isSummary && focus.Count > 0)
            intent += $" The target emphasis is: {string.Join("; ", focus)}.";

        var heading = isSummary
            ? "Professional Summary"
            : isPublication
                ? CompactPublicationHeading(claims[0].SubjectName)
                : CompactHeading(claims[0].SubjectName);
        var dateRange = isSummary || isProject || isEducation || isPublication
            ? null
            : ExperienceDateRange(group.Key, acceptedClaims);
        if (!string.IsNullOrWhiteSpace(dateRange)) heading += $" | {dateRange}";

        return new EvidencePacket(
            $"section-{number + 1}-{Slug(group.Key)}",
            heading,
            intent,
            maximumWords,
            claims,
            requirementIds,
            isSummary ? "summary" : isProject ? "project" : isEducation ? "education" :
            isPublication ? "publication" : "experience");
    }

    private static IReadOnlyList<EvidencePacket> BuildAdditionalExperiencePackets(
        IReadOnlyList<JobMlClaim> acceptedClaims,
        IReadOnlyList<SelectedClaim> selected,
        IReadOnlyDictionary<string, JobMlEntity> entities,
        IReadOnlySet<string> duplicateGenericSubjects,
        int minimumMonths,
        int maximumEntries,
        int sectionOffset)
    {
        var selectedSubjects = selected.Select(item => item.Claim.Subject)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = acceptedClaims
            .Where(claim => claim.Type == "experience" && !selectedSubjects.Contains(claim.Subject) &&
                            !duplicateGenericSubjects.Contains(claim.Subject))
            .Where(claim => entities.GetValueOrDefault(claim.Subject)?.Type == "experience")
            .Select(claim => new
            {
                Claim = claim,
                Entity = entities[claim.Subject],
                Dates = ParseExperiencePeriod(claim.Statement)
            })
            .Where(item => item.Dates is not null &&
                           item.Dates.Value.IsLongerThan(minimumMonths))
            .GroupBy(item => item.Claim.Subject, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Dates!.Value.SortEnd).First())
            .OrderByDescending(item => item.Dates!.Value.SortEnd)
            .ThenByDescending(item => item.Dates!.Value.Start)
            .Take(Math.Max(0, maximumEntries))
            .ToList();

        return candidates.Select((item, index) =>
        {
            var dates = item.Dates!.Value;
            var heading = CompactHeading(item.Entity.Name);
            var line = $"{heading} | {dates.Format()}";
            var selectedClaim = new SelectedClaim(item.Claim, heading, line,
                item.Claim.Evidence.Select((evidence, evidenceIndex) =>
                    evidence.Id ?? $"{item.Claim.Id}-e{evidenceIndex + 1}").ToList(),
                0, []);
            return new EvidencePacket(
                $"section-{sectionOffset + index + 1}-additional-{Slug(item.Entity.Id)}",
                line,
                "Retain this accepted ledger chronology entry exactly. Do not rewrite it.",
                WordCount(line),
                [selectedClaim],
                [],
                "additional_experience");
        }).ToList();
    }

    private static ExperiencePeriod? ParseExperiencePeriod(string statement)
    {
        var match = Regex.Match(statement,
            @"\|\s*(?<start>\d{4}-\d{2}-\d{2})\s*\|\s*(?<end>\d{4}-\d{2}-\d{2}|Present)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !DateOnly.TryParseExact(match.Groups["start"].Value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) return null;
        if (match.Groups["end"].Value.Equals("Present", StringComparison.OrdinalIgnoreCase))
            return new ExperiencePeriod(start, null);
        return DateOnly.TryParseExact(match.Groups["end"].Value, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            ? new ExperiencePeriod(start, end)
            : null;
    }

    private static List<SelectedClaim> FitHumanProse(
        IReadOnlyList<SelectedClaim> claims,
        int wordBudget,
        IReadOnlyList<string> focus,
        bool preserveOpening = false)
    {
        var fitted = new List<SelectedClaim>();
        var remaining = wordBudget;
        for (var index = 0; index < claims.Count && remaining > 0; index++)
        {
            var futureMinimum = Math.Min(claims.Count - index - 1, 2) * 12;
            var allowance = Math.Max(12, remaining - futureMinimum);
            var sourceProse = StripRepeatedHeadingPrefix(claims[index].Prose, claims[index].SubjectName);
            var prose = CompactHumanProse(sourceProse, allowance, focus, preserveOpening && index == 0);
            var words = WordCount(prose);
            if (words == 0 || words > remaining)
            {
                // Prefer fewer complete, evidenced claims to chopped prose or an
                // oversized deterministic fallback.
                continue;
            }
            fitted.Add(claims[index] with { Prose = prose });
            remaining -= words;
        }
        return fitted.Count > 0 ? fitted : [claims[0]];
    }

    private static string StripRepeatedHeadingPrefix(string prose, string subjectName)
    {
        var normalized = MarkdownEvidenceIndex.NormalizeText(prose);
        var heading = CompactHeading(subjectName).Trim();
        if (heading.Length == 0 || !normalized.StartsWith(heading, StringComparison.OrdinalIgnoreCase))
            return normalized;
        var remainder = normalized[heading.Length..];
        return remainder.StartsWith(':')
            ? remainder[1..].TrimStart()
            : normalized;
    }

    private static string CompactHumanProse(string prose, int maximumWords, IReadOnlyList<string> focus,
        bool preserveOpening = false)
    {
        var normalized = NormalizeTechnicalProse(MarkdownEvidenceIndex.NormalizeText(prose));
        if (WordCount(normalized) <= maximumWords) return normalized;
        var sentences = Regex.Split(normalized, @"(?<=[.!?])\s+")
            .Select((text, index) => new { Text = text.Trim(), Index = index })
            .Where(item => item.Text.Length > 0)
            .ToList();
        if (sentences.Count <= 1) return normalized;

        var focusTokens = Tokens(string.Join(' ', focus));
        var selected = new List<(string Text, int Index)>();
        var remaining = maximumWords;
        if (preserveOpening)
        {
            var openingWords = WordCount(sentences[0].Text);
            if (openingWords <= remaining)
            {
                selected.Add((sentences[0].Text, sentences[0].Index));
                remaining -= openingWords;
            }
        }
        foreach (var sentence in sentences
                     .Where(item => selected.All(existing => existing.Index != item.Index))
                     .OrderByDescending(item => Tokens(item.Text).Intersect(focusTokens).Count())
                     .ThenBy(item => item.Index))
        {
            var words = WordCount(sentence.Text);
            if (words > remaining) continue;
            selected.Add((sentence.Text, sentence.Index));
            remaining -= words;
        }
        return selected.Count == 0
            ? normalized
            : string.Join(' ', selected.OrderBy(item => item.Index).Select(item => item.Text));
    }

    private static string NormalizeTechnicalProse(string prose)
    {
        (string Pattern, string Replacement)[] joinedTerms =
        [
            (@"\bopensource\b", "open-source"),
            (@"\bselfoptimizing\b", "self-optimising"),
            (@"\bmultiLLM\b", "multi-LLM"),
            (@"\bRAGbacked\b", "RAG-backed"),
            (@"\bplatformaware\b", "platform-aware"),
            (@"\bmetricsdriven\b", "metrics-driven"),
            (@"\bgroundtruth\b", "ground-truth")
        ];
        foreach (var (pattern, replacement) in joinedTerms)
            prose = Regex.Replace(prose, pattern, replacement, RegexOptions.IgnoreCase);
        return prose;
    }

    private async Task<(double Score, string Reason, bool Direct)> ScoreAsync(
        CompilerRequirement requirement, JobMlClaim claim, string subjectName,
        IReadOnlyDictionary<string, HashSet<string>> conceptTerms, CancellationToken cancellationToken)
    {
        var requirementTokens = Tokens(requirement.Text);
        var claimTerms = claim.Concepts.All.SelectMany(id => conceptTerms.GetValueOrDefault(id, [id]))
            .Append(subjectName).Append(claim.Statement).ToList();
        var claimTokens = Tokens(string.Join(' ', claimTerms));
        var intersection = requirementTokens.Intersect(claimTokens).Count();
        var lexical = intersection == 0 ? 0 : intersection / (double)Math.Max(1, requirementTokens.Count);
        var requirementPhrases = EquivalentRequirementPhrases(requirement.Text);
        var direct = claim.Concepts.All.Any(id => conceptTerms.GetValueOrDefault(id, [id])
            .Any(term => ContainsSupportedTerm(claim.Statement, term) &&
                         requirementPhrases.Any(phrase => ContainsStandaloneTerm(phrase, term))));
        if (direct) return (Math.Max(.92, lexical), "concept name or alias appears in the requirement", true);
        if (lexical >= .35) return (Math.Min(.88, .55 + lexical * .3), "shared terms", false);
        if (embeddings is null) return (lexical, "no semantic match", false);
        var a = await embeddings.EmbedAsync(requirement.Text, cancellationToken);
        var b = await embeddings.EmbedAsync(claim.Statement + " " + string.Join(' ', claimTerms), cancellationToken);
        var semantic = embeddings.CosineSimilarity(a, b);
        return (semantic, "embedding similarity", false);
    }

    private async Task<IReadOnlyDictionary<string, double>> BuildSubjectAffinitiesAsync(
        string targetText, IReadOnlyList<JobMlRoleCentroid> centroids,
        IReadOnlyDictionary<string, JobMlEntity> entities, CancellationToken cancellationToken)
    {
        if (embeddings is null || centroids.Count == 0) return new Dictionary<string, double>();
        var target = centroids
            .Where(centroid => targetText.Contains(centroid.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(centroid => centroid.Name.Length)
            .FirstOrDefault();
        if (target is null)
        {
            var targetVector = await embeddings.EmbedAsync(targetText, cancellationToken);
            target = centroids.Where(centroid => centroid.Vector.Count == targetVector.Length)
                .OrderByDescending(centroid => embeddings.CosineSimilarity(targetVector, [.. centroid.Vector]))
                .FirstOrDefault();
        }
        if (target is null || target.Vector.Count == 0) return new Dictionary<string, double>();

        var targetCentroid = target.Vector.ToArray();
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities.Values.Where(entity => entity.Type == "experience"))
        {
            var roleVector = await embeddings.EmbedAsync(entity.Name, cancellationToken);
            if (roleVector.Length == targetCentroid.Length)
            {
                var semantic = Math.Clamp(embeddings.CosineSimilarity(roleVector, targetCentroid), 0, 1);
                result[entity.Id] = Math.Max(semantic, TitleAffinity(target.Name, entity.Name));
            }
        }
        return result;
    }

    private static double TitleAffinity(string targetRole, string candidateRole)
    {
        if (candidateRole.Contains(targetRole, StringComparison.OrdinalIgnoreCase)) return 1;
        var executiveTarget = targetRole.Contains("VP", StringComparison.OrdinalIgnoreCase) ||
                              targetRole.Contains("Head", StringComparison.OrdinalIgnoreCase) ||
                              targetRole.Contains("CTO", StringComparison.OrdinalIgnoreCase);
        if (!executiveTarget) return 0;
        string[] executiveTitles = ["VP", "Head of Engineering", "CTO", "Director of Engineering", "Engineering Director"];
        if (executiveTitles.Any(title => candidateRole.Contains(title, StringComparison.OrdinalIgnoreCase))) return .92;
        if (candidateRole.Contains("Head", StringComparison.OrdinalIgnoreCase) ||
            candidateRole.Contains("Director", StringComparison.OrdinalIgnoreCase) ||
            candidateRole.Contains("CTO", StringComparison.OrdinalIgnoreCase) ||
            candidateRole.Contains("VP", StringComparison.OrdinalIgnoreCase)) return .92;
        if (candidateRole.Contains("Lead", StringComparison.OrdinalIgnoreCase) ||
            candidateRole.Contains("Manager", StringComparison.OrdinalIgnoreCase)) return .72;
        return 0;
    }

    private static List<SelectedClaim> SelectClaims(
        IReadOnlyList<JobMlClaim> candidates, IReadOnlyList<JobMlClaim> acceptedClaims,
        IReadOnlyList<ClaimMatch> matches,
        IReadOnlyDictionary<string, ClaimEvidenceResolution> reconciled,
        IReadOnlyDictionary<string, JobMlEntity> entities, MarkdownEvidenceIndex index,
        CompilationOptions options, IReadOnlySet<string>? careerAnchorSubjects = null,
        string? targetTitle = null, IReadOnlyList<CompilerRequirement>? requirements = null)
    {
        var result = new List<SelectedClaim>();
        var coveredRequirements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requirementFrequency = matches.GroupBy(match => match.RequirementId,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.Select(match => match.ClaimId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                StringComparer.OrdinalIgnoreCase);
        double SpecificityScore(IReadOnlyList<ClaimMatch> claimMatches) => claimMatches
            .DistinctBy(match => match.RequirementId, StringComparer.OrdinalIgnoreCase)
            .Sum(match => 1 / Math.Sqrt(requirementFrequency[match.RequirementId]));

        // Keep author-selected career anchors, choosing their passages for this
        // vacancy rather than allowing generic leadership wording to dominate.
        foreach (var subject in careerAnchorSubjects ?? new HashSet<string>())
        {
            var anchored = candidates
                .Where(claim => claim.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase))
                .Select(claim => new
                {
                    Claim = claim,
                    Matches = matches.Where(match =>
                        match.ClaimId.Equals(claim.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
                    Prose = ResolveProse(claim, reconciled, index)
                })
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Prose))
                .OrderByDescending(candidate => candidate.Matches.Select(match => match.RequirementId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count())
                .ThenByDescending(candidate => candidate.Matches.Select(match => match.Score).DefaultIfEmpty(0).Max())
                .ThenByDescending(candidate => CareerAnchorClaimSignal(candidate.Claim.Statement))
                .ThenByDescending(candidate => candidate.Claim.Type == "achievement")
                .ToList();
            if (anchored.Count == 0) continue;
            var chosen = anchored[0];
            AddAnchorClaim(chosen.Claim, chosen.Prose!, chosen.Matches);
            var supporting = anchored.Skip(1)
                .FirstOrDefault(candidate => candidate.Matches.Count > 0 &&
                    TextOverlap(chosen.Prose!, candidate.Prose!) < .48 &&
                    candidate.Matches.Any(match => !coveredRequirements.Contains(match.RequirementId)));
            if (supporting is not null)
                AddAnchorClaim(supporting.Claim, supporting.Prose!, supporting.Matches);

            void AddAnchorClaim(JobMlClaim claim, string prose, List<ClaimMatch> claimMatches)
            {
                result.Add(new SelectedClaim(claim,
                    entities.GetValueOrDefault(claim.Subject)?.Name ?? claim.Subject,
                    prose,
                    claim.Evidence.Select((e, i) => e.Id ?? $"{claim.Id}-e{i + 1}").ToList(),
                    claimMatches.Select(match => match.Score).DefaultIfEmpty(.5).Max(), claimMatches));
                foreach (var requirement in claimMatches.Select(match => match.RequirementId))
                    coveredRequirements.Add(requirement);
            }
        }

        // Every targeted resume needs the author's reviewed summary. It frames the
        // selected evidence but never gains facts from the vacancy.
        var summary = candidates
            .Where(claim => claim.Type == "summary")
            .Select(claim => new
            {
                Claim = claim,
                Matches = matches.Where(match =>
                    match.ClaimId.Equals(claim.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
                Prose = ResolveProse(claim, reconciled, index)
            })
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Prose))
            .OrderByDescending(candidate => candidate.Matches.Select(match => match.Score).DefaultIfEmpty(.55).Max())
            .FirstOrDefault();
        if (summary is not null && result.All(item => item.Claim.Id != summary.Claim.Id))
            result.Add(new SelectedClaim(summary.Claim,
                entities.GetValueOrDefault(summary.Claim.Subject)?.Name ?? summary.Claim.Subject,
                summary.Prose!,
                summary.Claim.Evidence.Select((e, i) => e.Id ?? $"{summary.Claim.Id}-e{i + 1}").ToList(),
                summary.Matches.Select(match => match.Score).DefaultIfEmpty(.55).Max(), summary.Matches));

        // Education is compact, conventional resume information. Keep reviewed
        // qualifications visible even when an advert does not use the same wording
        // (for example "degree" versus the qualification's exact title).
        foreach (var education in candidates
                     .Where(claim => claim.Type == "education")
                     .Select(claim => new
                     {
                         Claim = claim,
                         Matches = matches.Where(match =>
                             match.ClaimId.Equals(claim.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
                         Prose = ResolveProse(claim, reconciled, index)
                     })
                     .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Prose))
                     .GroupBy(candidate => candidate.Claim.Subject, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.OrderByDescending(candidate =>
                         candidate.Matches.Select(match => match.Score).DefaultIfEmpty(.4).Max()).First())
                     .OrderBy(candidate => candidate.Prose!.Length))
        {
            if (result.Any(item => item.Claim.Id.Equals(education.Claim.Id, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (result.Any(item => item.Claim.Type == "education" &&
                                   TextOverlap(item.Prose, education.Prose!) >= .85))
                continue;
            result.Add(new SelectedClaim(education.Claim,
                entities.GetValueOrDefault(education.Claim.Subject)?.Name ?? education.Claim.Subject,
                education.Prose!,
                education.Claim.Evidence.Select((e, i) => e.Id ?? $"{education.Claim.Id}-e{i + 1}").ToList(),
                education.Matches.Select(match => match.Score).DefaultIfEmpty(.4).Max(), education.Matches));
        }

        // A career record can mark a small publication list as resume material.
        // Preserve its reviewed titles and links as a compact final section; it is
        // portfolio context, not evidence-footnote machinery.
        foreach (var publication in candidates
                     .Where(claim => claim.Type == "publication" &&
                                     entities.GetValueOrDefault(claim.Subject)?.Type == "publication")
                     .Select(claim => new
                     {
                         Claim = claim,
                         Matches = matches.Where(match =>
                             match.ClaimId.Equals(claim.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
                         Prose = ResolveProse(claim, reconciled, index)
                     })
                     .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Prose))
                     .OrderByDescending(candidate => candidate.Matches.Count > 0)
                     .ThenByDescending(candidate =>
                         candidate.Matches.Select(match => match.Score).DefaultIfEmpty(.4).Max())
                     .Take(3))
        {
            result.Add(new SelectedClaim(publication.Claim,
                entities.GetValueOrDefault(publication.Claim.Subject)?.Name ?? publication.Claim.Subject,
                publication.Prose!,
                publication.Claim.Evidence.Select((e, i) => e.Id ?? $"{publication.Claim.Id}-e{i + 1}").ToList(),
                publication.Matches.Select(match => match.Score).DefaultIfEmpty(.4).Max(), publication.Matches));
        }

        // Reserve relevant employment evidence before project matches consume the
        // section budget. Leadership resumes still need to read as career histories.
        var reservedRoles = candidates
            .Where(claim => entities.GetValueOrDefault(claim.Subject)?.Type == "experience")
            .Where(claim => careerAnchorSubjects?.Contains(claim.Subject) != true)
            .Select(claim => new
            {
                Claim = claim,
                Matches = matches.Where(match =>
                    match.ClaimId.Equals(claim.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
                Prose = ResolveProse(claim, reconciled, index),
                Recency = ExperienceRecency(claim.Subject, acceptedClaims)
            })
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Prose))
            .GroupBy(candidate => candidate.Claim.Subject, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Any(candidate => candidate.Matches.Count > 0))
            .Select(group => new
            {
                Candidate = group
                    .OrderByDescending(candidate => RoleFramingClaimSignal(candidate.Claim.Statement) >= 7)
                    .ThenByDescending(candidate => IsAiTarget(targetTitle) && HasAiContent(candidate.Claim.Statement))
                    .ThenByDescending(candidate => candidate.Matches.Select(match => match.RequirementId)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count())
                    .ThenByDescending(candidate => candidate.Matches.Select(match => match.Score).DefaultIfEmpty(.4).Max())
                    .ThenByDescending(candidate => RoleFramingClaimSignal(candidate.Claim.Statement))
                    .First(),
                SubjectMatch = group.Max(candidate =>
                    candidate.Matches.Select(match => match.Score).DefaultIfEmpty(.55).Max() +
                    Math.Min(.25, candidate.Matches.Select(match => match.RequirementId)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count() * .025))
            })
            .OrderByDescending(item => item.SubjectMatch * .55 + item.Candidate.Recency * .45)
            .Take(options.MinimumExperienceSections)
            .Select(item => item.Candidate);
        foreach (var role in reservedRoles)
        {
            if (result.Any(item => item.Claim.Id.Equals(role.Claim.Id, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(new SelectedClaim(role.Claim,
                entities.GetValueOrDefault(role.Claim.Subject)?.Name ?? role.Claim.Subject,
                role.Prose!,
                role.Claim.Evidence.Select((e, i) => e.Id ?? $"{role.Claim.Id}-e{i + 1}").ToList(),
                role.Matches.Select(match => match.Score).DefaultIfEmpty(.55).Max(), role.Matches));
            foreach (var requirement in role.Matches.Select(match => match.RequirementId))
                coveredRequirements.Add(requirement);

            if (NonAnchorClaimCount(result, careerAnchorSubjects) >= options.MaximumClaims ||
                options.MaximumClaimsPerSubject < 2) continue;
            var supportingCandidates = candidates
                .Where(claim => claim.Subject.Equals(role.Claim.Subject, StringComparison.OrdinalIgnoreCase) &&
                                !claim.Id.Equals(role.Claim.Id, StringComparison.OrdinalIgnoreCase))
                .Select(claim => new
                {
                    Claim = claim,
                    Matches = matches.Where(match =>
                        match.ClaimId.Equals(claim.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
                    Prose = ResolveProse(claim, reconciled, index)
                })
                .Where(candidate => candidate.Matches.Count > 0 && !string.IsNullOrWhiteSpace(candidate.Prose))
                .Where(candidate => TextOverlap(role.Prose!, candidate.Prose!) < .48)
                .OrderByDescending(candidate => IsAiTarget(targetTitle) && HasAiContent(candidate.Claim.Statement))
                .ThenByDescending(candidate => SpecificityScore(candidate.Matches))
                .ThenByDescending(candidate => candidate.Matches.Max(match => match.Score))
                .ToList();
            var supporting = supportingCandidates.FirstOrDefault();
            if (supporting is null) continue;
            result.Add(new SelectedClaim(supporting.Claim,
                entities.GetValueOrDefault(supporting.Claim.Subject)?.Name ?? supporting.Claim.Subject,
                supporting.Prose!,
                supporting.Claim.Evidence.Select((e, i) => e.Id ?? $"{supporting.Claim.Id}-e{i + 1}").ToList(),
                supporting.Matches.Max(match => match.Score), supporting.Matches));
            foreach (var requirement in supporting.Matches.Select(match => match.RequirementId))
                coveredRequirements.Add(requirement);
            if (!IsAiTarget(targetTitle) || options.MaximumClaimsPerSubject < 3 ||
                NonAnchorClaimCount(result, careerAnchorSubjects) >= options.MaximumClaims)
                continue;
            var additionalAi = supportingCandidates.Skip(1)
                .FirstOrDefault(candidate => HasAiContent(candidate.Claim.Statement) &&
                    TextOverlap(supporting.Prose!, candidate.Prose!) < .48);
            if (additionalAi is null) continue;
            result.Add(new SelectedClaim(additionalAi.Claim,
                entities.GetValueOrDefault(additionalAi.Claim.Subject)?.Name ?? additionalAi.Claim.Subject,
                additionalAi.Prose!,
                additionalAi.Claim.Evidence.Select((e, i) => e.Id ?? $"{additionalAi.Claim.Id}-e{i + 1}").ToList(),
                additionalAi.Matches.Max(match => match.Score), additionalAi.Matches));
            foreach (var requirement in additionalAi.Matches.Select(match => match.RequirementId))
                coveredRequirements.Add(requirement);
        }

        // Keep a stable, role-relevant engineering portfolio. Without this
        // reservation, projects compete with every supporting employment claim
        // and can disappear when an unrelated role gains one extra passage.
        var reservedProjects = candidates
            .Where(claim => claim.Type == "project" ||
                            entities.GetValueOrDefault(claim.Subject)?.Type == "project")
            .Select(claim => new
            {
                Claim = claim,
                Matches = matches.Where(match =>
                    match.ClaimId.Equals(claim.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
                Prose = ResolveProse(claim, reconciled, index)
            })
            .Where(candidate => candidate.Matches.Count > 0 && !string.IsNullOrWhiteSpace(candidate.Prose))
            .GroupBy(candidate => candidate.Claim.Subject, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate =>
                    ProjectSelectionScore(candidate.Matches, candidate.Claim.Statement, targetTitle, requirements))
                .First())
            .OrderByDescending(candidate =>
                ProjectSelectionScore(candidate.Matches, candidate.Claim.Statement, targetTitle, requirements))
            .Take(options.MinimumProjectSections);
        foreach (var project in reservedProjects)
        {
            if (NonAnchorClaimCount(result, careerAnchorSubjects) >= options.MaximumClaims) break;
            if (result.Any(item => item.Claim.Id.Equals(project.Claim.Id, StringComparison.OrdinalIgnoreCase)))
                continue;
            result.Add(new SelectedClaim(project.Claim,
                entities.GetValueOrDefault(project.Claim.Subject)?.Name ?? project.Claim.Subject,
                project.Prose!,
                project.Claim.Evidence.Select((e, i) => e.Id ?? $"{project.Claim.Id}-e{i + 1}").ToList(),
                project.Matches.Max(match => match.Score), project.Matches));
            foreach (var requirement in project.Matches.Select(match => match.RequirementId))
                coveredRequirements.Add(requirement);
        }

        var pending = candidates
            .Select(c => (Claim: c, Matches: matches.Where(m => m.ClaimId.Equals(c.Id, StringComparison.OrdinalIgnoreCase)).ToList()))
            .Where(x => x.Matches.Count > 0)
            // Anchors already have their one defining passage. Keep them compact and
            // leave the ordinary claim and section budgets to role-specific evidence.
            .Where(x => careerAnchorSubjects?.Contains(x.Claim.Subject) != true)
            .Where(x => result.All(selected => !selected.Claim.Id.Equals(x.Claim.Id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        while (pending.Count > 0 && NonAnchorClaimCount(result, careerAnchorSubjects) < options.MaximumClaims)
        {
            var claim = pending.OrderByDescending(x =>
            {
                var uncoveredBonus = x.Matches.Any(m => !coveredRequirements.Contains(m.RequirementId)) ? .12 : 0;
                var subjectPenalty = result.Count(r => r.Claim.Subject.Equals(x.Claim.Subject, StringComparison.OrdinalIgnoreCase)) * options.DiversityPenalty;
                var conceptOverlap = result.Count == 0 ? 0 : result.Max(r => Jaccard(r.Claim.Concepts.All, x.Claim.Concepts.All)) * options.DiversityPenalty;
                var executiveProjectBonus = IsExecutiveTarget(targetTitle) && x.Claim.Type == "project" &&
                                             IsProductisationEvidence(x.Claim.Statement)
                    ? .24
                    : 0;
                var recencyBonus = entities.GetValueOrDefault(x.Claim.Subject)?.Type == "experience"
                    ? ExperienceRecency(x.Claim.Subject, acceptedClaims) * .08
                    : 0;
                var framingBonus = Math.Min(.1, RoleFramingClaimSignal(x.Claim.Statement) * .0125);
                return x.Matches.Max(m => m.Score) + uncoveredBonus + executiveProjectBonus + recencyBonus +
                       framingBonus - subjectPenalty - conceptOverlap;
            }).First();
            pending.Remove(claim);
            var isNewSubject = result.All(existing =>
                !existing.Claim.Subject.Equals(claim.Claim.Subject, StringComparison.OrdinalIgnoreCase));
            if (isNewSubject && result
                    .Where(existing => careerAnchorSubjects?.Contains(existing.Claim.Subject) != true)
                    .Where(existing => existing.Claim.Type is not ("summary" or "education"))
                    .Select(existing => existing.Claim.Subject)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= options.MaximumSections)
                continue;
            if (result.Count(x => x.Claim.Subject.Equals(claim.Claim.Subject, StringComparison.OrdinalIgnoreCase)) >=
                options.MaximumClaimsPerSubject) continue;
            var prose = ResolveProse(claim.Claim, reconciled, index);
            // A role-specific resume is never bootstrapped from an LLM or the advert.
            if (string.IsNullOrWhiteSpace(prose)) continue;
            // Imports commonly contain lightly rewritten copies of the same bullet.
            // Keep their separate evidence in the career record, but do not print both
            // in one role section of the projection.
            if (result.Any(existing =>
                    existing.Claim.Subject.Equals(claim.Claim.Subject, StringComparison.OrdinalIgnoreCase) &&
                    TextOverlap(existing.Prose, prose) >= .48))
                continue;
            result.Add(new SelectedClaim(claim.Claim,
                entities.GetValueOrDefault(claim.Claim.Subject)?.Name ?? claim.Claim.Subject,
                prose,
                claim.Claim.Evidence.Select((e, i) => e.Id ?? $"{claim.Claim.Id}-e{i + 1}").ToList(),
                claim.Matches.Max(x => x.Score), claim.Matches));
            foreach (var requirement in claim.Matches.Select(x => x.RequirementId)) coveredRequirements.Add(requirement);
        }
        return result;
    }

    private static bool IsAiTarget(string? title) => !string.IsNullOrWhiteSpace(title) &&
        Regex.IsMatch(title, @"\b(?:AI|LLM|machine learning|data science)\b", RegexOptions.IgnoreCase);

    private static bool HasAiContent(string statement) =>
        Regex.IsMatch(statement,
            @"\b(?:AI|LLMs?|multiLLMs?|RAG(?:backed)?|OpenAI|Ollama|agents?|embeddings?|models?|machine learning)\b",
            RegexOptions.IgnoreCase);

    private static bool IsExecutiveTarget(string? title) => !string.IsNullOrWhiteSpace(title) &&
        (title.Contains("Head", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("VP", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Chief", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("CTO", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Director", StringComparison.OrdinalIgnoreCase));

    private static bool IsProductisationEvidence(string statement) =>
        statement.Contains("productisation", StringComparison.OrdinalIgnoreCase) ||
        statement.Contains("commercial platform", StringComparison.OrdinalIgnoreCase) ||
        statement.Contains("commercial release", StringComparison.OrdinalIgnoreCase);

    private static double ProjectSelectionScore(
        IReadOnlyList<ClaimMatch> matches, string statement, string? targetTitle,
        IReadOnlyList<CompilerRequirement>? requirements) =>
        matches.Select(match => match.Score).DefaultIfEmpty(0).Max() +
        Math.Min(.3, matches.Select(match => match.RequirementId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() * .03) +
        (IsExecutiveTarget(targetTitle) && IsProductisationEvidence(statement) ? .24 : 0) +
        AgenticProjectBonus(matches, statement, requirements);

    private static double AgenticProjectBonus(
        IReadOnlyList<ClaimMatch> matches, string statement,
        IReadOnlyList<CompilerRequirement>? requirements)
    {
        if (!statement.Contains("coding agent", StringComparison.OrdinalIgnoreCase) &&
            !statement.Contains("coding CLI", StringComparison.OrdinalIgnoreCase) &&
            !statement.Contains("agent orchestration", StringComparison.OrdinalIgnoreCase)) return 0;
        var matched = matches.Select(match => match.RequirementId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requirements?.Any(requirement => matched.Contains(requirement.Id) &&
            (requirement.Text.Contains("coding agent", StringComparison.OrdinalIgnoreCase) ||
             requirement.Text.Contains("AI-assisted", StringComparison.OrdinalIgnoreCase) ||
             requirement.Text.Contains("agent orchestration", StringComparison.OrdinalIgnoreCase))) == true
            ? .18
            : 0;
    }

    private static int NonAnchorClaimCount(
        IEnumerable<SelectedClaim> selected,
        IReadOnlySet<string>? careerAnchorSubjects) => selected.Count(item =>
        careerAnchorSubjects?.Contains(item.Claim.Subject) != true &&
        item.Claim.Type is not ("summary" or "education" or "publication"));

    private static string? ResolveProse(JobMlClaim claim,
        IReadOnlyDictionary<string, ClaimEvidenceResolution> reconciled, MarkdownEvidenceIndex index) =>
        reconciled.GetValueOrDefault(claim.Id)?.Evidence
            .Where(x => x.State == EvidenceState.Valid && IsProse(x.Evidence))
            .Select(x => x.CurrentText ??
                         (x.Evidence.Ref is not null && index.TryGet(x.Evidence.Ref, out var passage)
                             ? passage.Text
                             : null))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static int CareerAnchorClaimSignal(string statement)
    {
        var score = 0;
        if (statement.Contains("first release", StringComparison.OrdinalIgnoreCase) ||
            statement.Contains("public production release", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (statement.Contains("led", StringComparison.OrdinalIgnoreCase) ||
            statement.Contains("owned", StringComparison.OrdinalIgnoreCase)) score += 3;
        if (statement.Contains("architect", StringComparison.OrdinalIgnoreCase) ||
            statement.Contains("built", StringComparison.OrdinalIgnoreCase)) score += 2;
        return score;
    }

    private static int RoleFramingClaimSignal(string statement)
    {
        var score = 0;
        if (statement.Contains("took technical ownership", StringComparison.OrdinalIgnoreCase) ||
            statement.Contains("owned", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (statement.Contains("asked to", StringComparison.OrdinalIgnoreCase) ||
            statement.Contains("commissioned", StringComparison.OrdinalIgnoreCase)) score += 7;
        if (statement.Contains("recruited", StringComparison.OrdinalIgnoreCase) ||
            statement.Contains("hired", StringComparison.OrdinalIgnoreCase)) score += 7;
        if (statement.Contains("led", StringComparison.OrdinalIgnoreCase)) score += 5;
        if (statement.Contains("responsible for", StringComparison.OrdinalIgnoreCase)) score += 4;
        return score;
    }

    private static string? ExperienceDateRange(string subject, IReadOnlyList<JobMlClaim> claims)
    {
        return FindExperiencePeriod(subject, claims)?.Format();
    }

    private static ExperiencePeriod? FindExperiencePeriod(
        string subject, IReadOnlyList<JobMlClaim> claims)
    {
        var temporal = claims.FirstOrDefault(claim =>
            claim.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase) && claim.Type == "experience");
        return temporal is null ? null : ParseExperiencePeriod(temporal.Statement);
    }

    private static HashSet<string> FindDuplicateExperienceSubjects(
        IReadOnlyList<JobMlClaim> claims, IReadOnlyDictionary<string, JobMlEntity> entities)
    {
        var dated = entities.Values.Where(entity => entity.Type == "experience")
            .Select(entity => (Entity: entity, Period: FindExperiencePeriod(entity.Id, claims)))
            .Where(item => item.Period is not null).ToList();
        static bool GenericConsulting(JobMlEntity entity) =>
            Regex.IsMatch(entity.Name, @"·\s*Consulting\s*$", RegexOptions.IgnoreCase);
        var namedPeriods = dated.Where(item => !GenericConsulting(item.Entity))
            .Select(item => item.Period!.Value).ToHashSet();
        var duplicates = dated.Where(item => GenericConsulting(item.Entity) &&
                                       namedPeriods.Contains(item.Period!.Value))
            .Select(item => item.Entity.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in dated.Where(item => !duplicates.Contains(item.Entity.Id))
                     .GroupBy(item => (item.Period!.Value, CompanyKey(item.Entity.Name))))
        {
            if (group.Count() <= 1) continue;
            var preferred = group.OrderByDescending(item => claims.Count(claim =>
                    claim.Subject.Equals(item.Entity.Id, StringComparison.OrdinalIgnoreCase) &&
                    claim.Type is "achievement" or "project"))
                .ThenByDescending(item => Regex.IsMatch(item.Entity.Name,
                    @"\b(?:developer|engineer|manager|architect|consultant|lead|CTO)\b",
                    RegexOptions.IgnoreCase) ? 1 : 0)
                .ThenByDescending(item => item.Entity.Name.Length)
                .First().Entity.Id;
            foreach (var item in group.Where(item => !item.Entity.Id.Equals(preferred,
                         StringComparison.OrdinalIgnoreCase)))
                duplicates.Add(item.Entity.Id);
        }
        return duplicates;
    }

    private static string CompanyKey(string name)
    {
        var parts = name.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var company = parts.FirstOrDefault(part => Regex.IsMatch(part,
            @"\b(?:Ltd|Limited|Corp|Corporation|PLC|Inc|AB)\.?\b", RegexOptions.IgnoreCase))
            ?? parts.LastOrDefault() ?? name;
        return Regex.Replace(Regex.Replace(company.ToLowerInvariant(),
                @"\b(?:ltd|limited|corp|corporation|plc|inc|ab)\b", ""), @"[^\p{L}\p{N}]+", "")
            .Trim();
    }

    private static double ExperienceRecency(string subject, IReadOnlyList<JobMlClaim> claims)
    {
        var period = FindExperiencePeriod(subject, claims);
        if (period is null) return .4;
        if (period.Value.IsCurrent) return 1;
        return period.Value.End!.Value.Year switch
        {
            >= 2025 => 1,
            >= 2023 => .85,
            >= 2021 => .7,
            >= 2018 => .55,
            _ => .4
        };
    }

    private readonly record struct ExperiencePeriod(DateOnly Start, DateOnly? End)
    {
        public bool IsCurrent => End is null;
        public DateOnly SortEnd => End ?? DateOnly.MaxValue;

        public bool IsLongerThan(int months) =>
            Start.AddMonths(Math.Max(0, months)) < (End ?? DateOnly.FromDateTime(DateTime.UtcNow));

        public string Format()
        {
            var culture = CultureInfo.GetCultureInfo("en-GB");
            var end = IsCurrent ? "Present" : End!.Value.ToString("MMM yyyy", culture);
            return $"{Start.ToString("MMM yyyy", culture)} - {end}";
        }
    }

    private static bool IsCareerAnchor(JobMlEntity entity) =>
        string.Equals(entity.Projection?.Include, "always", StringComparison.OrdinalIgnoreCase);

    private static JobMlFile BuildProjection(JobMlFile source, string human,
        IReadOnlyList<CompositionBlock> blocks, IReadOnlyList<EvidencePacket> packets,
        IReadOnlyList<SelectedClaim> selected, string? fullJobMlUri)
    {
        var effectiveFullJobMl = AbsoluteHttpUri(fullJobMlUri ?? source.Data.Document.EffectiveFullJobMl);
        var sourceEntities = source.Data.Entities.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var selectedById = selected.ToDictionary(x => x.Claim.Id, StringComparer.OrdinalIgnoreCase);
        var packetsBySection = packets.ToDictionary(packet => packet.SectionId, StringComparer.OrdinalIgnoreCase);
        var additionalParagraphs = blocks
            .Where(block => packetsBySection.GetValueOrDefault(block.SectionId)?.Kind == "additional_experience")
            .Select((block, index) => new { block.SectionId, Paragraph = index + 1 })
            .ToDictionary(item => item.SectionId, item => item.Paragraph, StringComparer.OrdinalIgnoreCase);
        var claims = new List<JobMlClaim>();
        foreach (var block in blocks)
            foreach (var claimId in block.ClaimIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!selectedById.TryGetValue(claimId, out var selectedClaim)) continue;
                var original = selectedClaim.Claim;
                var passageText = MarkdownEvidenceIndex.NormalizeText(block.Text);
                var proseRef = additionalParagraphs.TryGetValue(block.SectionId, out var paragraph)
                    ? $"#additional-experience:p{paragraph}"
                    : $"#{block.SectionId}:p1";
                var evidence = new List<JobMlEvidence>
            {
                new()
                {
                    Id = $"projection-{block.SectionId}", Type = "prose", Ref = proseRef,
                    Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(passageText) },
                    Selector = new JobMlTextSelector { Exact = passageText }
                }
            };
                evidence.AddRange(original.Evidence.Where(e => !IsProse(e)).Select(item =>
                    ProjectSupportingEvidence(item, original.Subject,
                        sourceEntities.GetValueOrDefault(original.Subject)?.Name ?? original.Subject,
                        effectiveFullJobMl)));
                claims.Add(new JobMlClaim
                {
                    Id = original.Id,
                    Subject = original.Subject,
                    Type = original.Type,
                    Statement = original.Statement,
                    Concepts = new JobMlClaimConcepts
                    {
                        Skills = [.. original.Concepts.Skills],
                        Capabilities = [.. original.Concepts.Capabilities],
                        Domains = [.. original.Concepts.Domains]
                    },
                    Evidence = evidence,
                    Origin = original.Origin,
                    Review = "accepted"
                });
            }
        claims = claims.DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var entityIds = claims.Select(x => x.Subject).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conceptIds = claims.SelectMany(x => x.Concepts.All).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceIds = claims.SelectMany(claim => claim.Evidence)
            .Select(evidence => evidence.SourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = new JobMlRoot
        {
            Header = new JobMlHeader
            {
                Version = source.Data.Header.Version,
                Profile = "resume",
                Purpose = "Machine-readable projection of claims selected for this role-specific resume.",
                Semantics = [.. source.Data.Header.Semantics]
            },
            Document = new JobMlDocumentMetadata
            {
                Id = source.Data.Document.Id + "-projection",
                Language = source.Data.Document.Language,
                FullJobMl = effectiveFullJobMl?.ToString()
            },
            Entities = source.Data.Entities.Where(x => entityIds.Contains(x.Id)).Select(x => new JobMlEntity
            {
                Id = x.Id,
                Name = CompactHeading(x.Name),
                Type = x.Type,
                Source = ProjectionEntitySource(x.Id, blocks, packetsBySection, selectedById),
                Projection = x.Projection is null ? null : new JobMlProjectionPreference
                {
                    Include = x.Projection.Include,
                    Reason = x.Projection.Reason
                }
            }).ToList(),
            Claims = claims,
            Concepts = source.Data.Concepts.Where(x => conceptIds.Contains(x.Id)).Select(x => new JobMlConcept
            {
                Id = x.Id,
                Type = x.Type,
                Name = x.Name,
                Aliases = [.. x.Aliases]
            }).ToList(),
            Sources = source.Data.Sources.Where(x => sourceIds.Contains(x.Id)).ToList()
        };
        return new JobMlFile(human, root);
    }

    private static string ProjectionEntitySource(string entityId,
        IReadOnlyList<CompositionBlock> blocks,
        IReadOnlyDictionary<string, EvidencePacket> packetsBySection,
        IReadOnlyDictionary<string, SelectedClaim> selectedById)
    {
        var block = blocks.First(candidate => candidate.ClaimIds.Any(id =>
            selectedById.GetValueOrDefault(id)?.Claim.Subject.Equals(entityId,
                StringComparison.OrdinalIgnoreCase) == true));
        return packetsBySection.GetValueOrDefault(block.SectionId)?.Kind == "additional_experience"
            ? "#additional-experience"
            : $"#{block.SectionId}";
    }

    private static string RenderHumanMarkdown(string completeMarkdown,
        IReadOnlyList<CompositionBlock> blocks, IReadOnlyList<EvidencePacket> packets, string? targetTitle,
        IReadOnlyList<SelectedClaim> selected, IReadOnlyList<JobMlConcept> concepts,
        IReadOnlyList<CompilerRequirement> requirements)
    {
        var firstSection = Regex.Match(completeMarkdown, @"(?m)^##\s+");
        var identity = (firstSection.Success ? completeMarkdown[..firstSection.Index] : completeMarkdown).Trim();
        if (string.IsNullOrWhiteSpace(identity)) identity = "# Résumé";
        var packetById = packets.ToDictionary(packet => packet.SectionId, StringComparer.OrdinalIgnoreCase);
        var orderedKinds = new[] { "summary", "project", "experience", "education", "publication" };
        var renderedGroups = new List<string>();
        foreach (var kind in orderedKinds)
        {
            var matching = blocks.Where(block =>
                    packetById.GetValueOrDefault(block.SectionId)?.Kind.Equals(kind,
                        StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            var additional = kind == "experience"
                ? blocks.Where(block => packetById.GetValueOrDefault(block.SectionId)?.Kind.Equals(
                    "additional_experience", StringComparison.OrdinalIgnoreCase) == true).ToList()
                : [];
            if (matching.Count == 0 && additional.Count == 0) continue;

            if (kind == "summary")
            {
                renderedGroups.AddRange(matching.Select(block => RenderResumeBlock(block,
                    packetById[block.SectionId], 2)));
                continue;
            }

            if (kind == "education" && matching.Count == 1)
            {
                var block = matching[0];
                renderedGroups.Add($"## Education {{#{block.SectionId}}}\n\n" +
                                   MarkdownEvidenceIndex.NormalizeText(block.Text));
                continue;
            }

            var groupHeading = kind switch
            {
                "project" => "Selected Engineering",
                "education" => "Education",
                "publication" => "Selected Recent Publications",
                _ => "Experience"
            };
            var rendered = matching.Select(block => RenderResumeBlock(block, packetById[block.SectionId], 3))
                .ToList();
            if (additional.Count > 0)
            {
                rendered.Add("### Additional consulting, contract and earlier experience {#additional-experience}\n\n" +
                             string.Join("\n\n", additional.Select(block =>
                                 MarkdownEvidenceIndex.NormalizeText(block.Text))));
            }
            renderedGroups.Add($"## {groupHeading}\n\n" + string.Join("\n\n", rendered));
        }

        // The visible index is a compact projection of this document, not of the
        // broader source ledger. Only advertise concepts whose claim IDs survived
        // paragraph fitting and therefore remain traceable in the rendered JobML.
        var renderedClaimIds = blocks.SelectMany(block => block.ClaimIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var skills = RenderCoreSkills(selected
            .Where(item => renderedClaimIds.Contains(item.Claim.Id))
            .ToList(), concepts, requirements, blocks, packetById);
        if (!string.IsNullOrWhiteSpace(skills))
        {
            var summaryEnd = renderedGroups.FindLastIndex(group =>
                group.StartsWith("## Professional Summary", StringComparison.Ordinal));
            renderedGroups.Insert(summaryEnd + 1, skills);
        }

        var title = string.IsNullOrWhiteSpace(targetTitle)
            ? null
            : $"**{Regex.Replace(targetTitle.Trim(), @"\s+", " ")}**";
        return string.Join("\n\n", new[] { identity, title }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Concat(renderedGroups));
    }

    private static string RenderResumeBlock(CompositionBlock block, EvidencePacket packet, int headingLevel)
    {
        // A composition block is the evidence passage for every claim it contains.
        // Keep it as one Markdown paragraph so :p1 and its fingerprint describe the
        // same text even when the source block combined several claim passages.
        return $"{new string('#', headingLevel)} {packet.Heading} {{#{block.SectionId}}}\n\n" +
               MarkdownEvidenceIndex.NormalizeText(block.Text);
    }

    private static string? RenderCoreSkills(
        IReadOnlyList<SelectedClaim> selected,
        IReadOnlyList<JobMlConcept> concepts,
        IReadOnlyList<CompilerRequirement> requirements,
        IReadOnlyList<CompositionBlock> blocks,
        IReadOnlyDictionary<string, EvidencePacket> packets)
    {
        var byId = concepts.ToDictionary(concept => concept.Id, StringComparer.OrdinalIgnoreCase);
        var requirementText = string.Join(' ', requirements.Select(requirement => requirement.Text));
        // Every advertised concept must resolve to a selected prose chunk in the
        // assembled résumé. The compact chronology has a shared heading rather
        // than one anchor per claim, so it is excluded from the visible index.
        var anchorByClaim = blocks
            .Where(block => packets.GetValueOrDefault(block.SectionId)?.Kind != "additional_experience")
            .SelectMany(block => block.ClaimIds.Select(claimId => (claimId, anchor: "#" + block.SectionId)))
            .GroupBy(entry => entry.claimId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().anchor, StringComparer.OrdinalIgnoreCase);
        var renderedTextByClaim = blocks
            .Where(block => packets.GetValueOrDefault(block.SectionId)?.Kind != "additional_experience")
            .SelectMany(block => block.ClaimIds.Select(claimId => (claimId, block.Text)))
            .GroupBy(entry => entry.claimId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Text, StringComparer.OrdinalIgnoreCase);
        var selectedConcepts = selected
            .Where(item => item.Claim.Type is not ("summary" or "education"))
            .Where(item => anchorByClaim.ContainsKey(item.Claim.Id))
            .SelectMany(item => item.Claim.Concepts.All.Select(id => new { Id = id, Item = item }))
            .Where(entry => byId.ContainsKey(entry.Id))
            .Where(entry => new[] { byId[entry.Id].Name }.Concat(byId[entry.Id].Aliases)
                .Any(term => ContainsSupportedTerm(entry.Item.Prose, term) &&
                             ContainsSupportedTerm(renderedTextByClaim[entry.Item.Claim.Id], term)))
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new
            {
                Score = group.Max(entry => entry.Item.Score),
                Anchor = anchorByClaim[group.OrderByDescending(entry => entry.Item.Score)
                    .ThenBy(entry => entry.Item.Claim.Id, StringComparer.Ordinal).First().Item.Claim.Id],
                Subjects = group.Select(entry => entry.Item.Claim.Subject)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count()
            }, StringComparer.OrdinalIgnoreCase);
        var displayStopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "coding", "commercial", "deployment", "design", "engineering", "engineer", "engineers",
            "experience", "implementation", "it", "lead", "mentor", "or", "product", "products",
            "production", "research", "responsible", "review", "systems", "teams", "technical",
            "technology", "test", "testing", "workflow", "workflows"
        };
        var rankedConcepts = selectedConcepts
            .Select(entry =>
            {
                var concept = byId[entry.Key];
                var terms = new[] { concept.Name }.Concat(concept.Aliases)
                    .Where(term => !string.IsNullOrWhiteSpace(term) && term.Trim().Length >= 2)
                    .ToList();
                var requirementMatch = terms.Any(term => requirementText.Contains(term, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    Concept = concept,
                    entry.Value.Anchor,
                    Score = entry.Value.Score + (requirementMatch ? 1 : 0) +
                            Math.Min(.5, entry.Value.Subjects * .1)
                };
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Concept.Name) && entry.Concept.Name.Length <= 48 &&
                            !displayStopWords.Contains(entry.Concept.Name.Trim()))
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Concept.Name, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(entry => DisplayConceptFamily(entry.Concept.Name), StringComparer.OrdinalIgnoreCase)
            .Select(entry => (entry.Concept, entry.Anchor))
            .ToList();
        if (rankedConcepts.Count == 0) return null;

        // Apply quotas after categorisation. A single concept-rich project must not
        // consume the global cut and hide leadership or delivery evidence from the
        // conventional ATS-visible projection.
        var skills = rankedConcepts
            .DistinctBy(entry => entry.Concept.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => (Name: entry.Concept.Name, entry.Anchor)).ToList();
        var capabilities = skills.Where(skill => DisplaySkillCategory(skill.Name) == "capability").Take(6).ToList();
        var ai = skills.Where(skill => DisplaySkillCategory(skill.Name) == "ai").Take(6).ToList();
        var platform = skills.Where(skill => DisplaySkillCategory(skill.Name) == "platform").Take(9).ToList();
        var lines = new List<string>();
        static string Link((string Name, string Anchor) skill) =>
            $"[{skill.Name.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal)}]({skill.Anchor})";
        if (capabilities.Count > 0) lines.Add($"**Leadership and delivery:** {string.Join(", ", capabilities.Select(Link))}");
        if (ai.Count > 0) lines.Add($"**AI and data:** {string.Join(", ", ai.Select(Link))}");
        if (platform.Count > 0) lines.Add($"**Platform engineering:** {string.Join(", ", platform.Select(Link))}");
        return lines.Count == 0 ? null : $"## Core Skills\n\n{string.Join("\n\n", lines)}";
    }

    private static IReadOnlyList<string> EquivalentRequirementPhrases(string text)
    {
        var phrases = new List<string> { text };
        if (Regex.IsMatch(text, @"\bretrieval[ -]augmented generation\b", RegexOptions.IgnoreCase))
            phrases.Add("RAG");
        if (Regex.IsMatch(text, @"\blarge language models?\b", RegexOptions.IgnoreCase))
            phrases.Add("LLM");
        return phrases;
    }

    private static bool ContainsSupportedTerm(string text, string? term) =>
        ContainsStandaloneTerm(text, term) ||
        term?.Equals("RAG", StringComparison.OrdinalIgnoreCase) == true &&
        Regex.IsMatch(text, @"(?<![\p{L}\p{N}])RAGbacked\b", RegexOptions.IgnoreCase) ||
        term?.Equals("LLM", StringComparison.OrdinalIgnoreCase) == true &&
        Regex.IsMatch(text, @"\bmultiLLMs?\b", RegexOptions.IgnoreCase);

    private static bool ContainsStandaloneTerm(string text, string? term) =>
        !string.IsNullOrWhiteSpace(term) && Regex.IsMatch(text,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term.Trim())}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string DisplaySkillCategory(string name)
    {
        var value = name.ToLowerInvariant();
        string[] capabilityTerms =
        [
            "architecture", "delivery", "governance", "leadership", "management", "mentor", "hiring",
            "security", "standards", "strategy", "technical direction", "productisation"
        ];
        if (capabilityTerms.Any(value.Contains)) return "capability";
        string[] aiTerms =
        [
            "ai", "agent", "anthropic", "behavioural", "bm25", "claude", "codex", "deepseek",
            "document intelligence", "embedding", "evaluation", "graphrag", "hnsw", "leiden", "llm", "llms",
            "machine learning", "ml", "ner", "ocr", "onnx", "openai", "program synthesis", "prompt",
            "pgvector", "qdrant", "rag", "retrieval", "semantic", "vector"
        ];
        return aiTerms.Any(term => term.Length <= 3
            ? Regex.IsMatch(value, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase)
            : value.Contains(term, StringComparison.Ordinal)) ? "ai" : "platform";
    }

    private static string DisplayConceptFamily(string name)
    {
        var normalized = Regex.Replace(name.Trim(), @"\s+", " ");
        if (normalized.Equals(".NET Core", StringComparison.OrdinalIgnoreCase)) return ".NET";
        if (normalized.Equals("ASP.NET", StringComparison.OrdinalIgnoreCase)) return "ASP.NET Core";
        return normalized;
    }

    private static IReadOnlyDictionary<string, HashSet<string>> BuildConceptTerms(IEnumerable<JobMlConcept> concepts) =>
        concepts.ToDictionary(x => x.Id,
            x => new[] { x.Id, x.Name }.Concat(x.Aliases).Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    private static List<CompilerRequirement> BuildRequirements(
        IReadOnlyList<string> required, IReadOnlyList<string> preferred, IReadOnlyList<string> responsibilities,
        string? requiredEducation)
    {
        var all = required.Select(x => (Text: x, Kind: RequirementKind.Required))
            .Concat(preferred.Select(x => (Text: x, Kind: RequirementKind.Preferred)))
            .Concat(responsibilities.Select(x => (Text: x, Kind: RequirementKind.Responsibility)))
            .Concat(string.IsNullOrWhiteSpace(requiredEducation)
                ? []
                : new[] { (Text: requiredEducation, Kind: RequirementKind.Required) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Text)).DistinctBy(x => x.Text, StringComparer.OrdinalIgnoreCase);
        return all.Select((x, i) => new CompilerRequirement($"req-{i + 1}", x.Text.Trim(), x.Kind, x.Text.Trim())).ToList();
    }

    private static List<CompilerRequirement> ExtractFallbackRequirements(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length is >= 12 and <= 240)
            .Take(40).Select((x, i) => new CompilerRequirement($"req-{i + 1}", x, RequirementKind.Responsibility, x)).ToList();

    private static string CompactHeading(string value)
    {
        var heading = Regex.Replace(value, @"\s+", " ").Trim();
        heading = Regex.Replace(heading,
            @"\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{4}\s*[–-]\s*(?:Present|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{4})\s+(?:Remote\s+)?(?=·)",
            " ", RegexOptions.IgnoreCase);
        heading = Regex.Replace(heading, @"\s+·", " ·");
        var colon = heading.IndexOf(':');
        if (colon is > 0 and <= 80)
            heading = heading[..colon].Trim();
        if (heading.Length <= 110) return heading;
        var boundary = heading.LastIndexOf(' ', 106);
        return heading[..(boundary > 30 ? boundary : 106)].TrimEnd() + "…";
    }

    private static string CompactPublicationHeading(string value)
    {
        var heading = Regex.Replace(value, @"\s+", " ").Trim();
        if (heading.Length <= 110) return heading;
        var boundary = heading.LastIndexOf(' ', 106);
        return heading[..(boundary > 30 ? boundary : 106)].TrimEnd() + "…";
    }

    private static bool IsAccepted(JobMlClaim claim) =>
        string.Equals(claim.Review, "accepted", StringComparison.OrdinalIgnoreCase);
    private static bool IsResumeNarrative(JobMlClaim claim) => claim.Type is null or
        "summary" or "achievement" or "project" or "education" or "experience" or "publication";
    private static bool IsProse(JobMlEvidence evidence) =>
        evidence.Type.Equals("prose", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(evidence.Uri);
    private static JobMlEvidence CloneEvidence(JobMlEvidence e) => new()
    {
        Id = e.Id,
        Type = e.Type,
        Ref = e.Ref,
        Uri = e.Uri,
        SourceId = e.SourceId,
        Issuer = e.Issuer,
        Qualification = e.Qualification,
        Title = e.Title,
        Authors = [.. e.Authors],
        Publisher = e.Publisher,
        Published = e.Published,
        Accessed = e.Accessed,
        Fingerprint = e.Fingerprint,
        Selector = e.Selector,
        State = e.State
    };
    private static JobMlEvidence ProjectSupportingEvidence(JobMlEvidence evidence, string subjectId,
        string subjectName, Uri? fullJobMl)
    {
        var projected = CloneEvidence(evidence);
        if (!IsTranscriptEvidence(projected)) return projected;

        projected.Type = "career_transcript";
        projected.Title = $"Complete transcript: {subjectName}";
        projected.Uri = TranscriptSectionUri(fullJobMl, subjectId)?.ToString();
        return projected;
    }

    private static bool IsTranscriptEvidence(JobMlEvidence evidence) =>
        string.Equals(evidence.Type, "source_ledger", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(evidence.Type, "career_transcript", StringComparison.OrdinalIgnoreCase);

    private static Uri? AbsoluteHttpUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https"
            ? uri
            : null;

    private static Uri? TranscriptSectionUri(Uri? fullJobMl, string subjectId)
    {
        if (fullJobMl is null) return null;
        var builder = new UriBuilder(fullJobMl) { Fragment = subjectId };
        return builder.Uri;
    }
    private static HashSet<string> Tokens(string value) => TokenPattern.Matches(value.ToLowerInvariant()).Select(x => x.Value).ToHashSet();
    private static int WordCount(string value) => Regex.Matches(value, @"\b[\p{L}\p{N}][\p{L}\p{N}'’-]*\b").Count;
    private static double Jaccard(IEnumerable<string> left, IEnumerable<string> right)
    {
        var a = left.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var b = right.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var union = a.Union(b, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0 : a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count() / (double)union;
    }
    private static double TextOverlap(string left, string right)
    {
        var a = Tokens(left);
        var b = Tokens(right);
        var smaller = Math.Min(a.Count, b.Count);
        return smaller == 0 ? 0 : a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count() / (double)smaller;
    }
    private static string Slug(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
