using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Models.Evidence;

public sealed record LedgerAuditFinding(string Code, string ClaimId, string? SubjectId, string Message, string SourceText, IReadOnlyList<string> Concepts);
public sealed record LedgerAuditReport(int Claims, int AcceptedClaims, int UnreviewedObservations, int EvidenceRecords,
    IReadOnlyDictionary<string, int> FindingsByCode, IReadOnlyList<LedgerAuditFinding> Findings);

/// <summary>Source comparison diagnostics, not a proficiency score or independent verification.</summary>
public static class EvidenceLedgerAudit
{
    public static LedgerAuditReport Analyse(ResumeDocument document)
    {
        var ledger = document.EvidenceLedger;
        var evidence = ledger.Evidence.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        var findings = new List<LedgerAuditFinding>();
        foreach (var claim in ledger.Claims)
        {
            var supports = claim.EvidenceIds.Where(evidence.ContainsKey).Select(id => evidence[id]).ToList();
            void Add(string code, string message, IReadOnlyList<string>? concepts = null) => findings.Add(new(code, claim.Id, claim.SubjectId, message, claim.Statement, concepts ?? []));
            if (supports.Count == 0 || supports.Count != claim.EvidenceIds.Count) Add("missing-support", "A claim refers to missing source evidence.");
            if (supports.Any(item => item.FastHash != EvidenceLedgerBuilder.FastHash(item.Text))) Add("source-drift", "Stored evidence text no longer matches its fingerprint.");
            if (claim.Review != "accepted") continue;
            if (claim.Kind == "achievement")
            {
                var missing = EvidenceTerms.FindExplicitConcepts(claim.Statement)
                    .Where(concept => !claim.Concepts.Any(existing => EvidenceTerms.Canonicalize(existing).Equals(concept, StringComparison.OrdinalIgnoreCase))).ToList();
                if (missing.Count > 0) Add("unlinked-explicit-mention", "Source prose explicitly names these concepts, but the claim has no matching concept links.", missing);
            }
            var unsupported = claim.Concepts.Where(concept => !supports.Any(item => EvidenceTerms.Mentions(item.Text, concept)))
                .Where(concept => !document.Experience.Where(role => claim.SubjectId == $"experience:{role.Id:N}")
                    .Any(role => role.InterviewEntries.Any(entry => entry.ReviewedProse == claim.Statement && entry.Skills.Contains(concept, StringComparer.OrdinalIgnoreCase)))).ToList();
            if (unsupported.Count > 0) Add("concept-needs-review", "Concept links are not literal positive mentions in their supports. Review aliases and any separately accepted author declaration.", unsupported);
        }
        return new(ledger.Claims.Count, ledger.Claims.Count(claim => claim.Review == "accepted"),
            ledger.Claims.Count(claim => claim.Kind == "entity" && claim.Review == "required"), ledger.Evidence.Count,
            findings.GroupBy(item => item.Code).ToDictionary(group => group.Key, group => group.Count()), findings);
    }
}
