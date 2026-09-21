using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Extraction;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Ingestion.Parsing;

/// <summary>
/// Resolves only headings that deterministic rules could not classify. The provider
/// proposes a bounded decision; policy decides whether it is safe to apply.
/// </summary>
public sealed class ResumeDecisionResolver
{
    public const string SectionContractVersion = "resume-section-v1";
    public const string NameContractVersion = "resume-name-candidate-v1";
    public const string CompanyContractVersion = "resume-company-candidate-v1";
    public const string SectionInstructions =
        "Classify this top-level resume section by its actual purpose. Choose other when the evidence is unclear.";
    public const string CandidateEvidenceInstructions =
        "This is evidence verification, not nearest-text matching. First decide whether the passage explicitly " +
        "establishes the requested semantic role. A candidate merely appearing in the passage is not enough. " +
        "Choose none when the requested person or employer is not established, even if another label, place, " +
        "product, client, school, or project resembles a candidate.";

    private static readonly IReadOnlyDictionary<string, string> SectionCandidates =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["experience"] = "Employment history, professional roles, responsibilities, and achievements.",
            ["education"] = "Academic education, degrees, schools, universities, and formal study.",
            ["skills"] = "Skills, technologies, tools, competencies, or areas of expertise.",
            ["certifications"] = "Professional certifications, licences, credentials, and accreditations.",
            ["projects"] = "Named personal, open-source, consulting, or professional projects.",
            ["summary"] = "Profile, overview, objective, or introductory professional summary.",
            ["other"] = "None of the resume section types above clearly applies."
        };

    public static IReadOnlyDictionary<string, string> SectionDecisionCandidates => SectionCandidates;

    private static readonly IReadOnlyDictionary<string, string> CanonicalTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["experience"] = "Experience",
            ["education"] = "Education",
            ["skills"] = "Skills",
            ["certifications"] = "Certifications",
            ["projects"] = "Projects",
            ["summary"] = "Summary"
        };

    private readonly IResumeDecisionProvider _provider;
    private readonly ResumeDecisionPolicyOptions _policy;
    private readonly ILogger<ResumeDecisionResolver> _logger;

    public ResumeDecisionResolver(
        IResumeDecisionProvider provider,
        IOptions<ResumeDecisionPolicyOptions> policy,
        ILogger<ResumeDecisionResolver> logger)
    {
        _provider = provider;
        _policy = policy.Value;
        _logger = logger;
        if (_policy.AcceptanceProbability is < 0 or > 1)
            throw new InvalidOperationException("Jev:AcceptanceProbability must be between 0 and 1.");
        if (_policy.MinimumMargin is < 0 or > 1)
            throw new InvalidOperationException("Jev:MinimumMargin must be between 0 and 1.");
    }

    public async Task<IReadOnlyList<DocumentSection>> ResolveSectionsAsync(
        ResumeDocument resume,
        IReadOnlyList<DocumentSection> sections,
        CancellationToken ct = default)
    {
        var resolved = new List<DocumentSection>(sections.Count);
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            if (!ShouldResolve(section))
            {
                resolved.Add(section);
                continue;
            }

            var sourceRef = $"document:section:{index + 1}";
            var sourceText = $"Heading: {section.Heading.Trim()}\n\nContent:\n{section.Body.Trim()}";
            var sourceHash = EvidenceLedgerBuilder.FastHash(sourceText);
            var request = new ResumeDecisionRequest(
                $"{resume.ResumeId:N}:{SectionContractVersion}:{index + 1}",
                sourceRef,
                sourceHash,
                sourceText,
                SectionInstructions,
                SectionCandidates);

            try
            {
                var result = await _provider.DecideAsync(request, ct);
                var ranked = result.Probabilities.Values.OrderDescending().Take(2).ToArray();
                var selectedProbability = result.Probabilities.GetValueOrDefault(result.SelectedCandidate);
                var margin = ranked.Length == 2 ? ranked[0] - ranked[1] : ranked.FirstOrDefault();
                var accepted = !string.Equals(result.SelectedCandidate, "other", StringComparison.Ordinal)
                               && selectedProbability >= _policy.AcceptanceProbability
                               && margin >= _policy.MinimumMargin
                               && CanonicalTypes.ContainsKey(result.SelectedCandidate);

                resume.IngestionDecisions.Add(new IngestionDecision
                {
                    DecisionId = request.DecisionId,
                    ContractVersion = SectionContractVersion,
                    SourceRef = sourceRef,
                    SourceHash = sourceHash,
                    CandidateSetHash = HashCandidates(SectionCandidates),
                    SelectedCandidate = result.SelectedCandidate,
                    SelectedValue = CanonicalTypes.GetValueOrDefault(result.SelectedCandidate),
                    Probabilities = new Dictionary<string, double>(result.Probabilities, StringComparer.Ordinal),
                    Confidence = result.Confidence,
                    Margin = margin,
                    Accepted = accepted,
                    Provider = result.Provider,
                    Model = result.Model,
                    InputTokens = result.InputTokens,
                    OutputTokens = result.OutputTokens,
                    RequestId = result.RequestId,
                    EvaluatedAt = DateTimeOffset.UtcNow
                });

                resolved.Add(accepted
                    ? section with { SemanticType = CanonicalTypes[result.SelectedCandidate] }
                    : section);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Bounded section decision failed for {SourceRef}; deterministic parsing will continue",
                    sourceRef);
                resolved.Add(section);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Selects among names already proposed by positional rules or NER. Jev is not
    /// allowed to spell a new name, so the original extracted span remains authoritative.
    /// </summary>
    public async Task ResolveNameAsync(ResumeDocument resume, string documentText, CancellationToken ct = default)
    {
        var values = resume.Entities
            .Where(entity => entity.Classification == "PersonName")
            .Select(entity => entity.Value.Trim())
            .Append(resume.Personal.FullName?.Trim() ?? "")
            .Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        if (values.Length < 2) return;

        var selected = await ResolveCandidateAsync(
            resume,
            NameContractVersion,
            "document:identity:name",
            string.Join('\n', documentText.Split('\n').Take(20)),
            "Which candidate is the resume owner's personal name? " + CandidateEvidenceInstructions,
            values,
            ct);
        if (selected is not null)
            resume.Personal.FullName = selected;
    }

    /// <summary>
    /// Fills only missing employer names by selecting an original Organization NER span.
    /// Existing deterministic company values are never overwritten.
    /// </summary>
    public async Task ResolveMissingCompaniesAsync(ResumeDocument resume, CancellationToken ct = default)
    {
        var organisations = resume.Entities
            .Where(entity => entity.Classification == "Organization")
            .Select(entity => entity.Value.Trim())
            .Where(value => value.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
        if (organisations.Length == 0) return;

        foreach (var experience in resume.Experience.Where(item => string.IsNullOrWhiteSpace(item.Company)))
        {
            var roleParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(experience.Title))
                roleParts.Add(experience.Title);
            roleParts.AddRange(experience.Achievements.Where(value => !string.IsNullOrWhiteSpace(value)).Take(8));
            var roleText = string.Join('\n', roleParts);
            if (string.IsNullOrWhiteSpace(roleText)) continue;
            var selected = await ResolveCandidateAsync(
                resume,
                CompanyContractVersion,
                $"experience:{experience.Id:N}:company",
                roleText,
                "Which candidate is the employer for this work-experience entry? " + CandidateEvidenceInstructions,
                organisations,
                ct);
            if (selected is not null)
                experience.Company = selected;
        }
    }

    private async Task<string?> ResolveCandidateAsync(
        ResumeDocument resume,
        string contractVersion,
        string sourceRef,
        string sourceText,
        string instructions,
        IReadOnlyList<string> values,
        CancellationToken ct)
    {
        var candidates = values.Select((value, index) => (Key: $"candidate_{index + 1}", Value: value))
            .ToDictionary(item => item.Key, item => $"Exact extracted text: {item.Value}", StringComparer.Ordinal);
        candidates["none"] = "None of the extracted candidates is supported by this passage.";
        var sourceHash = EvidenceLedgerBuilder.FastHash(sourceText);
        var request = new ResumeDecisionRequest(
            $"{resume.ResumeId:N}:{contractVersion}:{sourceHash[9..]}",
            sourceRef,
            sourceHash,
            sourceText,
            instructions,
            candidates);

        try
        {
            var result = await _provider.DecideAsync(request, ct);
            var ranked = result.Probabilities.Values.OrderDescending().Take(2).ToArray();
            var selectedProbability = result.Probabilities.GetValueOrDefault(result.SelectedCandidate);
            var margin = ranked.Length == 2 ? ranked[0] - ranked[1] : ranked.FirstOrDefault();
            var accepted = result.SelectedCandidate != "none"
                           && selectedProbability >= _policy.AcceptanceProbability
                           && margin >= _policy.MinimumMargin
                           && candidates.ContainsKey(result.SelectedCandidate);
            var selectedIndex = accepted && result.SelectedCandidate.StartsWith("candidate_", StringComparison.Ordinal)
                && int.TryParse(result.SelectedCandidate[10..], out var parsedIndex)
                ? parsedIndex - 1
                : -1;
            var selectedValue = selectedIndex >= 0 && selectedIndex < values.Count ? values[selectedIndex] : null;

            resume.IngestionDecisions.Add(new IngestionDecision
            {
                DecisionId = request.DecisionId,
                ContractVersion = contractVersion,
                SourceRef = sourceRef,
                SourceHash = sourceHash,
                CandidateSetHash = HashCandidates(candidates),
                SelectedCandidate = result.SelectedCandidate,
                SelectedValue = selectedValue,
                Probabilities = new Dictionary<string, double>(result.Probabilities, StringComparer.Ordinal),
                Confidence = result.Confidence,
                Margin = margin,
                Accepted = accepted && selectedValue is not null,
                Provider = result.Provider,
                Model = result.Model,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
                RequestId = result.RequestId,
                EvaluatedAt = DateTimeOffset.UtcNow
            });
            return accepted ? selectedValue : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Bounded candidate decision failed for {SourceRef}; deterministic result remains unchanged",
                sourceRef);
            return null;
        }
    }

    private static bool ShouldResolve(DocumentSection section) =>
        section.Level <= 2
        && !string.IsNullOrWhiteSpace(section.Heading)
        && section.SemanticType is null
        && SectionClassifier.ClassifyHeading(section.Heading) is null;

    private static string HashCandidates(IReadOnlyDictionary<string, string> candidates) =>
        EvidenceLedgerBuilder.FastHash(string.Join('\n', candidates.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}")));
}
