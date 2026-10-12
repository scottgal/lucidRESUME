using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;

namespace lucidRESUME.Core.Models.Evidence;

/// <summary>Transfers published JobML review decisions to the persisted career ledger.</summary>
public static class ReviewedEvidenceSnapshot
{
    private static string JobMlClaimId(string id) => $"claim-{EvidenceLedgerBuilder.Slug(id)}-{EvidenceLedgerBuilder.FastHash(id)[^8..]}";

    /// <summary>Publish explicitly reviewed role/summary edits as new author evidence.
    /// Original imports and prior interview transcripts remain intact.</summary>
    public static void ApplyProseEdits(ResumeDocument document, JobMlFile file)
    {
        var changedStatements = new List<string>();
        var resolved = JobMlProcessor.Reconcile(file).ToDictionary(item => item.Claim.Id);
        foreach (var old in document.EvidenceLedger.Claims.ToArray())
        {
            var edited = file.Data.Claims.SingleOrDefault(claim => claim.Id == JobMlClaimId(old.Id));
            if (edited is null || edited.Review != "accepted" || edited.Statement == old.Statement) continue;
            var prose = resolved[edited.Id];
            if (!prose.Evidence.Any(item => item.Evidence.Type == "prose" && item.State == EvidenceState.Valid &&
                MarkdownEvidenceIndex.NormalizeText(item.CurrentText ?? "") == MarkdownEvidenceIndex.NormalizeText(edited.Statement)))
                throw new InvalidDataException("Edited claims must agree with their reviewed prose before publishing.");
            if (old.Kind == "achievement" && old.SubjectId?.StartsWith("experience:") == true &&
                Guid.TryParse(old.SubjectId["experience:".Length..], out var roleId))
            {
                var role = document.Experience.Single(item => item.Id == roleId);
                var index = role.Achievements.IndexOf(old.Statement);
                if (index < 0) continue;
                role.Achievements[index] = edited.Statement;
                changedStatements.Add(edited.Statement);
            }
            else if (old.Kind == "personal" && old.Statement == document.Personal.Summary)
            {
                document.Personal.Summary = edited.Statement;
                changedStatements.Add(edited.Statement);
            }
            else throw new InvalidDataException("Use the role/profile editors for structural facts, or the transcript interview to add evidence.");
        }
        var updated = EvidenceLedgerBuilder.EnsureCurrent(document);
        foreach (var claim in updated.Claims.Where(claim => changedStatements.Contains(claim.Statement)))
            foreach (var evidence in updated.Evidence.Where(item => claim.EvidenceIds.Contains(item.Id)))
            {
                evidence.SourceName = "Author-reviewed career prose";
                evidence.ExtractionMethod = "author-edit";
                evidence.AccessedOn = DateOnly.FromDateTime(DateTime.UtcNow);
            }
    }

    public static void Apply(ResumeDocument document)
    {
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(document);
        if (string.IsNullOrWhiteSpace(document.JobMlSource)) return;
        var parser = new JobMlParser();
        if (!parser.TryParse(document.JobMlSource, out var file, out var error))
            throw new InvalidDataException($"The published JobML snapshot is invalid: {error}");
        if (JobMlProcessor.Validate(file!).Any(d => d.Severity == JobMlDiagnosticSeverity.Error))
            throw new InvalidDataException("The published JobML snapshot has integrity errors.");
        var resolved = JobMlProcessor.Reconcile(file!);
        var concepts = file!.Data.Concepts.ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var claim in ledger.Claims)
        {
            var matches = resolved.Where(item => (item.Claim.Type == claim.Kind || claim.Kind == "personal" && item.Claim.Type == "summary") &&
                item.Claim.Statement == claim.Statement && (item.Claim.Id == JobMlClaimId(claim.Id) || item.Claim.Evidence.Any(e =>
                    claim.EvidenceIds.Contains(e.Id ?? "", StringComparer.OrdinalIgnoreCase) ||
                    claim.EvidenceIds.Any(id => e.Ref == $"ledger://{id}")))).ToList();
            // Unpublished or changed material cannot inherit an accepted decision.
            claim.Review = "required";
            if (matches.Count != 1) continue;
            var match = matches[0];
            if (match.Claim.Review == "accepted" && (match.Evidence.Count == 0 ||
                match.Evidence.Any(e => e.State is not (EvidenceState.Valid or EvidenceState.External))))
                throw new InvalidDataException("Accepted JobML claim evidence requires reconciliation.");
            claim.Review = match.Claim.Review ?? "required";
            claim.Concepts = match.Claim.Concepts.Skills.Select(id => concepts.GetValueOrDefault(id, id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
