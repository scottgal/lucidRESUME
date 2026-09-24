using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
        var requirements = BuildRequirements(job.RequiredSkills, job.PreferredSkills, job.Responsibilities);
        if (requirements.Count == 0)
            requirements = ExtractFallbackRequirements(jobDescription);

        var reconciled = JobMlProcessor.Reconcile(completeResume.File)
            .ToDictionary(x => x.Claim.Id, StringComparer.OrdinalIgnoreCase);
        var concepts = BuildConceptTerms(completeResume.File.Data.Concepts);
        var entities = completeResume.File.Data.Entities.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var index = MarkdownEvidenceIndex.Create(completeResume.File.Markdown);
        var subjectAffinities = await BuildSubjectAffinitiesAsync(
            job.Title ?? jobDescription, completeResume.File.Data.RoleCentroids, entities, cancellationToken);

        var accepted = completeResume.File.Data.Claims
            .Where(IsAccepted)
            .Where(c => reconciled.TryGetValue(c.Id, out var resolution) && resolution.Evidence.Count > 0 &&
                        resolution.Evidence.All(e => e.State is EvidenceState.Valid or EvidenceState.External))
            .ToList();
        var subjectsWithNarrative = accepted
            .Where(claim => claim.Type is "achievement" or "project" or "summary")
            .Select(claim => claim.Subject)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = accepted
            .Where(IsResumeNarrative)
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
                    reason += $"; role affinity {roleAffinity:F2}";
                }
                if (score >= options.RelatedThreshold)
                    matches.Add(new ClaimMatch(requirement.Id, claim.Id,
                        direct ? MatchKind.Direct : MatchKind.Related, score, reason));
            }

        var selected = SelectClaims(candidates, matches, reconciled, entities, index, options);
        var sections = selected.GroupBy(x => x.Claim.Subject, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Any(item => item.Claim.Type == "summary"))
            .ThenByDescending(group => group.Max(item => item.Score))
            .Select((group, number) => new EvidencePacket(
                $"section-{number + 1}-{Slug(group.Key)}",
                group.Any(item => item.Claim.Type == "summary") ? "Professional Summary" : group.First().SubjectName,
                "Preserve the candidate's human voice while emphasizing evidenced relevance to the target role.",
                Math.Max(70, Math.Min(220, group.Sum(x => WordCount(x.Prose)))),
                group.OrderByDescending(x => x.Score).ToList(),
                group.SelectMany(x => x.Matches).Select(x => x.RequirementId).Distinct().ToList()))
            .ToList();
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
            embeddings is null ? "lexical" : "configured");

        var composed = await composition.ComposeAsync(manifest, jobDescription, options, cancellationToken);
        var human = RenderHumanMarkdown(completeResume.File.Markdown, composed.Blocks, sections);
        var projected = BuildProjection(completeResume.File, human, composed.Blocks, selected,
            options.FullJobMlUri);
        var full = JobMlArtifactComposer.Compose(projected);
        var published = CJobMlProjector.Project(projected).Markdown;
        return new CompilationResult(Guid.NewGuid().ToString("N"), manifest, human, published, full,
            projected, composed.Used, composed.Provider, composed.Warnings);
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
        var direct = claim.Concepts.All.Any(id => conceptTerms.GetValueOrDefault(id, [id])
            .Any(term => requirement.Text.Contains(term, StringComparison.OrdinalIgnoreCase)));
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
        if (candidateRole.Contains("Lead", StringComparison.OrdinalIgnoreCase) ||
            candidateRole.Contains("Manager", StringComparison.OrdinalIgnoreCase)) return .72;
        return 0;
    }

    private static List<SelectedClaim> SelectClaims(
        IReadOnlyList<JobMlClaim> candidates, IReadOnlyList<ClaimMatch> matches,
        IReadOnlyDictionary<string, ClaimEvidenceResolution> reconciled,
        IReadOnlyDictionary<string, JobMlEntity> entities, MarkdownEvidenceIndex index,
        CompilationOptions options)
    {
        var result = new List<SelectedClaim>();
        var coveredRequirements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = candidates
            .Select(c => (Claim: c, Matches: matches.Where(m => m.ClaimId.Equals(c.Id, StringComparison.OrdinalIgnoreCase)).ToList()))
            .Where(x => x.Matches.Count > 0).ToList();
        while (pending.Count > 0 && result.Count < options.MaximumClaims)
        {
            var claim = pending.OrderByDescending(x =>
            {
                var uncoveredBonus = x.Matches.Any(m => !coveredRequirements.Contains(m.RequirementId)) ? .12 : 0;
                var subjectPenalty = result.Count(r => r.Claim.Subject.Equals(x.Claim.Subject, StringComparison.OrdinalIgnoreCase)) * options.DiversityPenalty;
                var conceptOverlap = result.Count == 0 ? 0 : result.Max(r => Jaccard(r.Claim.Concepts.All, x.Claim.Concepts.All)) * options.DiversityPenalty;
                return x.Matches.Max(m => m.Score) + uncoveredBonus - subjectPenalty - conceptOverlap;
            }).First();
            pending.Remove(claim);
            var isNewSubject = result.All(existing =>
                !existing.Claim.Subject.Equals(claim.Claim.Subject, StringComparison.OrdinalIgnoreCase));
            if (isNewSubject && result.Select(existing => existing.Claim.Subject)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= options.MaximumSections)
                continue;
            if (result.Count(x => x.Claim.Subject.Equals(claim.Claim.Subject, StringComparison.OrdinalIgnoreCase)) >=
                options.MaximumClaimsPerSubject) continue;
            var prose = reconciled[claim.Claim.Id].Evidence
                .Where(x => x.State == EvidenceState.Valid && IsProse(x.Evidence))
                .Select(x => x.CurrentText ?? (x.Evidence.Ref is not null && index.TryGet(x.Evidence.Ref, out var p) ? p.Text : null))
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
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

    private static JobMlFile BuildProjection(JobMlFile source, string human,
        IReadOnlyList<CompositionBlock> blocks, IReadOnlyList<SelectedClaim> selected, string? fullJobMlUri)
    {
        var selectedById = selected.ToDictionary(x => x.Claim.Id, StringComparer.OrdinalIgnoreCase);
        var claims = new List<JobMlClaim>();
        foreach (var block in blocks)
            foreach (var claimId in block.ClaimIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!selectedById.TryGetValue(claimId, out var selectedClaim)) continue;
                var original = selectedClaim.Claim;
                var passageText = MarkdownEvidenceIndex.NormalizeText(block.Text);
                var evidence = new List<JobMlEvidence>
            {
                new()
                {
                    Id = $"projection-{block.SectionId}", Type = "prose", Ref = $"#{block.SectionId}:p1",
                    Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(passageText) },
                    Selector = new JobMlTextSelector { Exact = passageText }
                }
            };
                evidence.AddRange(original.Evidence.Where(e => !IsProse(e)).Select(CloneEvidence));
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
                FullJobMl = fullJobMlUri ?? source.Data.Document.EffectiveFullJobMl
            },
            Entities = source.Data.Entities.Where(x => entityIds.Contains(x.Id)).Select(x => new JobMlEntity
            { Id = x.Id, Name = x.Name, Type = x.Type, Source = $"#{blocks.First(b => b.ClaimIds.Any(id => selectedById.GetValueOrDefault(id)?.Claim.Subject.Equals(x.Id, StringComparison.OrdinalIgnoreCase) == true)).SectionId}" }).ToList(),
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

    private static string RenderHumanMarkdown(string completeMarkdown,
        IReadOnlyList<CompositionBlock> blocks, IReadOnlyList<EvidencePacket> packets)
    {
        var firstSection = Regex.Match(completeMarkdown, @"(?m)^##\s+");
        var identity = (firstSection.Success ? completeMarkdown[..firstSection.Index] : completeMarkdown).Trim();
        if (string.IsNullOrWhiteSpace(identity)) identity = "# Résumé";
        return identity + "\n\n" + string.Join("\n\n", blocks.Select(block =>
        {
            var heading = packets.First(x => x.SectionId == block.SectionId).Heading;
            // A composition block is the evidence passage for every claim it contains.
            // Keep it as one Markdown paragraph so :p1 and its fingerprint describe the
            // same text even when the source block combined several claim passages.
            return $"## {heading} {{#{block.SectionId}}}\n\n{MarkdownEvidenceIndex.NormalizeText(block.Text)}";
        }));
    }

    private static IReadOnlyDictionary<string, HashSet<string>> BuildConceptTerms(IEnumerable<JobMlConcept> concepts) =>
        concepts.ToDictionary(x => x.Id,
            x => new[] { x.Id, x.Name }.Concat(x.Aliases).Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    private static List<CompilerRequirement> BuildRequirements(
        IReadOnlyList<string> required, IReadOnlyList<string> preferred, IReadOnlyList<string> responsibilities)
    {
        var all = required.Select(x => (x, RequirementKind.Required))
            .Concat(preferred.Select(x => (x, RequirementKind.Preferred)))
            .Concat(responsibilities.Select(x => (x, RequirementKind.Responsibility)))
            .Where(x => !string.IsNullOrWhiteSpace(x.x)).DistinctBy(x => x.x, StringComparer.OrdinalIgnoreCase);
        return all.Select((x, i) => new CompilerRequirement($"req-{i + 1}", x.x.Trim(), x.Item2, x.x.Trim())).ToList();
    }

    private static List<CompilerRequirement> ExtractFallbackRequirements(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length is >= 12 and <= 240)
            .Take(40).Select((x, i) => new CompilerRequirement($"req-{i + 1}", x, RequirementKind.Responsibility, x)).ToList();

    private static bool IsAccepted(JobMlClaim claim) =>
        string.Equals(claim.Review, "accepted", StringComparison.OrdinalIgnoreCase);
    private static bool IsResumeNarrative(JobMlClaim claim) => claim.Type is null or
        "summary" or "achievement" or "project" or "education" or "experience";
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
