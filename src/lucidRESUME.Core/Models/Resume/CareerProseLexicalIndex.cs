using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Evidence;

namespace lucidRESUME.Core.Models.Resume;

/// <summary>Cheap derived analysis at the write boundary. A chunk retains its whole scoped claim and limitations.</summary>
public static class CareerProseLexicalIndex
{
    private static readonly Regex Token = new(@"(?:\.[\p{L}][\p{L}\p{N}]*)|[\p{L}\p{N}]+(?:[+#]+|[.-][\p{L}\p{N}]+)*", RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = "the and for with from that this will have your our you are was were been being into its their they them then than which when where while before after using used built build designed design engineering engineer work worked software a an to of in on by as is it or".Split(' ').ToHashSet(StringComparer.Ordinal);
    private static readonly Regex NonProduction = new(@"\b(?:not|never)(?:\s+(?:yet|been|ever)){0,2}\s+(?:deployed|released|shipped|operated)\s+(?:to|in|into|on)\s+(?:(?:a|the|real|live)\s+)?production\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string[] Terms(string text) => Token.Matches(text.ToLowerInvariant()).Select(match => match.Value)
        .Where(term => !StopWords.Contains(term)).ToArray();
    public static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool HasExplicitNonProductionDeployment(string text) => NonProduction.IsMatch(text);

    public static CareerProseIndex Build(ResumeDocument source)
    {
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(source);
        var accepted = ledger.Claims.Where(claim => claim.Review == "accepted").ToList();
        var revision = Fingerprint(JsonSerializer.Serialize(new
        {
            claims = accepted.OrderBy(claim => claim.Id).Select(claim => new { claim.Id, claim.Statement, claim.SubjectId, claim.Kind, claim.Concepts, claim.EvidenceIds }),
            accounts = source.Experience.SelectMany(role => role.InterviewEntries).Where(entry => entry.Account is not null)
                .OrderBy(entry => entry.Id).Select(entry => new { entry.Id, entry.Account })
        }));
        var previous = source.ProseIndex;
        if (previous.SourceRevision == ledger.SourceRevision && previous.AcceptedClaimsRevision == revision &&
            previous.PolicyVersion == CareerProseIndex.CurrentPolicyVersion) return previous;
        var reusable = previous.Segments.GroupBy(segment => segment.ContentHash).ToDictionary(group => group.Key, group => group.First());
        var index = new CareerProseIndex
        {
            SourceRevision = ledger.SourceRevision,
            AcceptedClaimsRevision = revision,
            EmbeddingSpace = previous.EmbeddingSpace,
            AnalysedAt = DateTimeOffset.UtcNow
        };
        foreach (var group in accepted.Where(claim => claim.Kind is "achievement" or "summary" or "project" or "publication" ||
                     claim.SubjectId == "personal" && claim.Statement == source.Personal.Summary)
                 .GroupBy(claim => (claim.SubjectId, Text: Regex.Replace(claim.Statement, @"\s+", " ").Trim().ToLowerInvariant())))
        {
            var claims = group.ToList();
            var text = claims[0].Statement;
            var hash = Fingerprint(text);
            reusable.TryGetValue(hash, out var cached);
            var missing = ProseStructure.MissingDimensions(text).ToList();
            index.Segments.Add(new()
            {
                Id = Fingerprint((group.Key.SubjectId ?? "career") + "|" + group.Key.Text),
                ContentHash = hash,
                SubjectId = group.Key.SubjectId ?? "career",
                Kind = claims[0].Kind,
                Text = text,
                ClaimIds = claims.Select(claim => claim.Id).ToList(),
                EvidenceIds = claims.SelectMany(claim => claim.EvidenceIds).Distinct().ToList(),
                Skills = claims.SelectMany(claim => claim.Concepts).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SkillConceptIds = claims.SelectMany(claim => claim.Concepts).Select(EvidenceLedgerBuilder.Slug).Distinct().ToList(),
                Accounts = source.Experience.Where(role => group.Key.SubjectId == $"experience:{role.Id:N}")
                    .SelectMany(role => role.InterviewEntries).Where(entry => entry.Account is not null && claims.Any(claim => claim.Statement == entry.ReviewedProse))
                    .Select(entry => entry.Account!).ToList(),
                MissingDimensions = missing,
                Salience = 1d - missing.Count / 4d,
                Embedding = cached?.Embedding ?? [],
                EmbeddingSpace = cached?.EmbeddingSpace ?? "",
                FactSignature = FactSignature(text),
                TermFrequency = Terms(text).GroupBy(term => term).ToDictionary(terms => terms.Key, terms => terms.Count())
            });
        }
        index.DocumentFrequency = index.Segments.SelectMany(segment => segment.TermFrequency.Keys).GroupBy(term => term)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var segment in index.Segments)
        {
            var weights = segment.TermFrequency.ToDictionary(term => term.Key,
                term => (1 + Math.Log(term.Value)) * (1 + Math.Log((1d + index.Segments.Count) / (1 + index.DocumentFrequency[term.Key]))));
            var norm = Math.Sqrt(weights.Values.Sum(weight => weight * weight));
            segment.TfIdf = weights.ToDictionary(term => term.Key, term => norm > 0 ? term.Value / norm : 0);
        }
        source.ProseIndex = index;
        return index;
    }

    public static string FactSignature(string text) => string.Join('|', Regex.Matches(text.ToLowerInvariant(),
        @"\b\d+(?:[.,]\d+)*(?:%|ms|x)?|\b(?:not|never|planned|intended|prototype|pilot|production|cancelled|canceled|helped|assisted|supported|contributed|led|owned|unknown|unmeasured)\b")
        .Select(match => match.Value).Order(StringComparer.Ordinal));
}
