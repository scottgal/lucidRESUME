using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;

namespace lucidRESUME.Export;

public sealed record ArchivedEvidence(int Number, string Label, string Markdown);

public static class ArchivedEvidenceResolver
{
    public static IReadOnlyList<ArchivedEvidence> Resolve(GeneratedResume entry)
    {
        var parser = new JobMlParser();
        if (!parser.TryParse(entry.Resume.JobMlSource ?? "", out var file, out _)) return [];
        var compact = CJobMlProjector.Project(file!);
        var records = entry.Source.EvidenceLedger.Evidence.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        return compact.References.Select(reference =>
        {
            var claimIds = compact.Anchors.Where(anchor => anchor.ReferenceNumbers.Contains(reference.Number))
                .Select(anchor => anchor.ClaimId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var claims = file!.Data.Claims.Where(claim => claimIds.Contains(claim.Id)).ToList();
            var support = claims.SelectMany(claim => claim.Evidence)
                .Where(item => item.Id is not null &&
                    (item.Id == reference.Evidence.Id || (item.Type == reference.Evidence.Type && item.Title == reference.Evidence.Title && item.Uri == reference.Evidence.Uri)))
                .Select(item => records.GetValueOrDefault(item.Id!)).Where(item => item is not null)
                .DistinctBy(item => item!.Id).ToList();
            var prose = $"## Reference [{reference.Number}]\n\n{reference.PlainText}\n\n### Generated claims\n\n" +
                string.Join("\n\n", claims.Select(claim => claim.Statement)) + "\n\n### Saved source evidence\n\n" +
                string.Join("\n\n", support.Select(item => $"**{item!.SourceName}** · `{item.Locator}`\n\n{item.Text}"));
            if (Uri.TryCreate(reference.Evidence.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                prose += $"\n\n[Open supporting publication]({uri})";
            if (support.Count == 0) prose += "\n\nThis reference has no local source passage. Its external citation is preserved above.";
            return new ArchivedEvidence(reference.Number, $"[{reference.Number}] {reference.Evidence.Title}", prose);
        }).ToList();
    }
}
