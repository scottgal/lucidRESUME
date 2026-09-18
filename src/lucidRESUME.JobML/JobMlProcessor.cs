using System.Text.RegularExpressions;

namespace lucidRESUME.JobML;

public sealed class JobMlProcessor
{
    private static readonly Regex LanguageTag = new(@"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.Compiled);

    public static IReadOnlyList<JobMlDiagnostic> Validate(JobMlFile file)
    {
        var diagnostics = new List<JobMlDiagnostic>();
        var root = file.Data;
        var index = MarkdownEvidenceIndex.Create(file.Markdown);

        if (root.Version != "0.1")
            diagnostics.Add(Error("JML001", $"Unsupported JobML version '{root.Version}'. Expected 0.1.", "jobml"));
        if (string.IsNullOrWhiteSpace(root.Header.Purpose))
            diagnostics.Add(Warning("JML005", "jobml.purpose should explain this document to an unfamiliar parser.", "jobml.purpose"));
        if (root.Header.Semantics.Count == 0)
            diagnostics.Add(Warning("JML006", "jobml.semantics should carry the evidence and authority contract with the document.", "jobml.semantics"));
        else if (!root.Header.Semantics.Any(item =>
                     item.Contains("unsupported claims", StringComparison.OrdinalIgnoreCase)))
            diagnostics.Add(Warning("JML007", "jobml.semantics should explicitly prohibit unsupported claims.", "jobml.semantics"));
        if (string.IsNullOrWhiteSpace(root.Document.Id))
            diagnostics.Add(Error("JML002", "document.id is required.", "document.id"));
        if (string.IsNullOrWhiteSpace(root.Document.Language) || !LanguageTag.IsMatch(root.Document.Language))
            diagnostics.Add(Error("JML003", "document.language must be a language tag such as en-GB.", "document.language"));

        CheckUniqueIds(root.Entities.Select(e => e.Id), "entity", "entities", diagnostics);
        CheckUniqueIds(root.Claims.Select(c => c.Id), "claim", "claims", diagnostics);
        CheckUniqueIds(root.Concepts.Select(c => c.Id), "concept", "concepts", diagnostics);

        var entityIds = root.Entities.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conceptIds = root.Concepts.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < root.Entities.Count; i++)
        {
            var entity = root.Entities[i];
            if (string.IsNullOrWhiteSpace(entity.Id) || string.IsNullOrWhiteSpace(entity.Type) || string.IsNullOrWhiteSpace(entity.Name))
                diagnostics.Add(Error("JML010", "An entity requires id, type, and name.", $"entities[{i}]"));
            if (string.IsNullOrWhiteSpace(entity.Source) || !HasSource(index, entity.Source))
                diagnostics.Add(Warning("JML011", $"Entity source '{entity.Source}' does not resolve to prose.", $"entities[{i}].source"));
        }

        var reconciled = Reconcile(file);
        for (var i = 0; i < root.Claims.Count; i++)
        {
            var claim = root.Claims[i];
            var path = $"claims[{i}]";
            if (string.IsNullOrWhiteSpace(claim.Id) || string.IsNullOrWhiteSpace(claim.Subject) || string.IsNullOrWhiteSpace(claim.Statement))
                diagnostics.Add(Error("JML020", "A claim requires id, subject, and statement.", path));
            if (!entityIds.Contains(claim.Subject))
                diagnostics.Add(Error("JML021", $"Claim subject '{claim.Subject}' does not identify an entity.", $"{path}.subject"));
            foreach (var concept in claim.Concepts.All.Where(c => !conceptIds.Contains(c)))
                diagnostics.Add(Error("JML022", $"Claim references unknown concept '{concept}'.", $"{path}.concepts"));
            if (claim.Evidence.Count == 0)
                diagnostics.Add(Error("JML023", "Every substantive claim requires evidence.", $"{path}.evidence"));
            if (string.Equals(claim.Origin, "derived", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(claim.Review, "accepted", StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(Warning("JML024", "Machine-derived claim requires human review; it is not yet an accepted evidential fact.", path));

            foreach (var resolution in reconciled[i].Evidence)
            {
                var isAccepted = string.Equals(claim.Review, "accepted", StringComparison.OrdinalIgnoreCase);
                var isProse = string.Equals(resolution.Evidence.Type, "prose", StringComparison.OrdinalIgnoreCase) &&
                              string.IsNullOrWhiteSpace(resolution.Evidence.Uri);
                if (isAccepted && isProse && string.IsNullOrWhiteSpace(resolution.Evidence.Fingerprint?.Text))
                    diagnostics.Add(Error("JML027", "Accepted prose evidence requires a drift fingerprint.", $"{path}.evidence"));
                if (resolution.State is EvidenceState.Missing or EvidenceState.Ambiguous)
                    diagnostics.Add(Error("JML025", $"Evidence '{resolution.Evidence.Ref}' is {resolution.State.ToString().ToLowerInvariant()}.", $"{path}.evidence"));
                else if (resolution.State == EvidenceState.Changed)
                    diagnostics.Add(Warning("JML026", $"Evidence '{resolution.Evidence.Ref}' has changed and requires review.", $"{path}.evidence"));
            }
        }

        for (var i = 0; i < root.Requirements.Count; i++)
        {
            var requirement = root.Requirements[i];
            if (string.IsNullOrWhiteSpace(requirement.Id) || string.IsNullOrWhiteSpace(requirement.Concept))
                diagnostics.Add(Error("JML030", "A requirement requires id and concept.", $"requirements[{i}]"));
        }

        return diagnostics;
    }

    public static IReadOnlyList<ClaimEvidenceResolution> Reconcile(JobMlFile file)
    {
        var index = MarkdownEvidenceIndex.Create(file.Markdown);
        return file.Data.Claims.Select(claim => new ClaimEvidenceResolution(
            claim,
            claim.Evidence.Select(e => Resolve(e, index)).ToList())).ToList();
    }

    public static IReadOnlyList<JobMlCoverageEntry> AnalyseCoverage(JobMlFile file)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var concept in file.Data.Concepts)
        {
            aliases[concept.Id] = concept.Id;
            aliases[concept.Name] = concept.Id;
            foreach (var alias in concept.Aliases) aliases[alias] = concept.Id;
        }

        var evidence = Reconcile(file).ToDictionary(x => x.Claim.Id, StringComparer.OrdinalIgnoreCase);
        return file.Data.Requirements.Select(requirement =>
        {
            var canonical = aliases.GetValueOrDefault(requirement.Concept, requirement.Concept);
            var matches = file.Data.Claims.Where(claim => claim.Concepts.All.Any(value =>
            {
                var claimCanonical = aliases.GetValueOrDefault(value, value);
                return string.Equals(canonical, claimCanonical, StringComparison.OrdinalIgnoreCase);
            })).ToList();

            var direct = matches.Where(claim =>
                evidence[claim.Id].Evidence.Count > 0 &&
                evidence[claim.Id].Evidence.All(e => e.State is EvidenceState.Valid or EvidenceState.External) &&
                !(string.Equals(claim.Origin, "derived", StringComparison.OrdinalIgnoreCase) &&
                  !string.Equals(claim.Review, "accepted", StringComparison.OrdinalIgnoreCase))).ToList();
            return direct.Count > 0
                ? new JobMlCoverageEntry(requirement, CoverageState.Direct, direct)
                : matches.Count > 0
                    ? new JobMlCoverageEntry(requirement, CoverageState.Ambiguous, matches)
                    : new JobMlCoverageEntry(requirement, CoverageState.None, []);
        }).ToList();
    }

    private static EvidenceResolution Resolve(JobMlEvidence evidence, MarkdownEvidenceIndex index)
    {
        if (!string.IsNullOrWhiteSpace(evidence.Uri) ||
            string.Equals(evidence.Type, "source_ledger", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evidence.Type, "qualification", StringComparison.OrdinalIgnoreCase))
            return new EvidenceResolution(evidence, EvidenceState.External);
        if (string.IsNullOrWhiteSpace(evidence.Ref))
            return new EvidenceResolution(evidence, EvidenceState.Missing);

        if (index.TryGet(evidence.Ref, out var passage))
        {
            var fingerprint = evidence.Fingerprint?.Text;
            var matches = !string.IsNullOrWhiteSpace(fingerprint)
                ? MarkdownEvidenceIndex.MatchesFingerprint(passage.Text, fingerprint)
                : evidence.Selector is null || string.IsNullOrWhiteSpace(evidence.Selector.Exact) ||
                  string.Equals(
                      MarkdownEvidenceIndex.NormalizeText(evidence.Selector.Exact),
                      passage.Text,
                      StringComparison.Ordinal);
            return new EvidenceResolution(
                evidence,
                matches ? EvidenceState.Valid : EvidenceState.Changed,
                passage.Text);
        }

        if (string.IsNullOrWhiteSpace(evidence.Fingerprint?.Text))
        {
            if (evidence.Selector is null) return new EvidenceResolution(evidence, EvidenceState.Missing);
            return ResolutionFromCandidates(evidence, index.FindByQuote(evidence.Selector));
        }
        var candidates = index.FindByFingerprint(evidence.Fingerprint.Text);
        if (candidates.Count == 0 && evidence.Selector is not null)
            candidates = index.FindByQuote(evidence.Selector);
        return ResolutionFromCandidates(evidence, candidates);
    }

    private static EvidenceResolution ResolutionFromCandidates(JobMlEvidence evidence, IReadOnlyList<ProsePassage> candidates)
    {
        return candidates.Count switch
        {
            1 => new EvidenceResolution(evidence, EvidenceState.Changed, candidates[0].Text, candidates[0].Reference),
            > 1 => new EvidenceResolution(evidence, EvidenceState.Ambiguous),
            _ => new EvidenceResolution(evidence, EvidenceState.Missing)
        };
    }

    private static bool HasSource(MarkdownEvidenceIndex index, string source)
    {
        var normalized = MarkdownEvidenceIndex.NormalizeReference(source);
        return index.Passages.Any(p =>
            p.Reference.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            (p.HeadingId is not null && $"#{p.HeadingId}".Equals(normalized, StringComparison.OrdinalIgnoreCase)));
    }

    private static void CheckUniqueIds(
        IEnumerable<string> ids,
        string kind,
        string path,
        ICollection<JobMlDiagnostic> diagnostics)
    {
        foreach (var group in ids.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            diagnostics.Add(Error("JML004", $"Duplicate {kind} id '{group.Key}'.", path));
    }

    private static JobMlDiagnostic Error(string code, string message, string path) =>
        new(JobMlDiagnosticSeverity.Error, code, message, path);
    private static JobMlDiagnostic Warning(string code, string message, string path) =>
        new(JobMlDiagnosticSeverity.Warning, code, message, path);
}
