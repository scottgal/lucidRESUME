using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

/// <summary>Adapts DocSummarizer's retained segments/signals pattern to scoped career claims.</summary>
public sealed class CareerProseAnalyser(IEmbeddingService embedder)
{
    public async Task<CareerProseIndex> AnalyseAsync(ResumeDocument source, CancellationToken ct = default, bool force = false)
    {
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(source);
        string Space() => embedder is IEmbeddingSpaceDescriptor descriptor
            ? $"{descriptor.ModelId}|{descriptor.ModelDigest}|{descriptor.Normalization}" : embedder.GetType().FullName ?? "unknown";
        var space = Space();
        var previous = source.ProseIndex;
        // Review, skill and provenance edits need not change the original source text.
        var accepted = ledger.Claims.Where(claim => claim.Review == "accepted").ToList();
        var acceptedRevision = ReusableProseStore.Fingerprint(System.Text.Json.JsonSerializer.Serialize(
            accepted.OrderBy(claim => claim.Id).Select(claim => new { claim.Id, claim.Statement, claim.SubjectId, claim.Kind, claim.Concepts, claim.EvidenceIds })));
        if (!force && previous.SourceRevision == ledger.SourceRevision && previous.AcceptedClaimsRevision == acceptedRevision &&
            previous.EmbeddingSpace == space && previous.PolicyVersion == CareerProseIndex.CurrentPolicyVersion) return previous;
        var reusable = previous.Segments.Where(segment => segment.EmbeddingSpace == space && (!force || segment.Embedding.Length > 0))
            .GroupBy(segment => segment.ContentHash).ToDictionary(group => group.Key, group => group.First());
        var index = new CareerProseIndex { SourceRevision = ledger.SourceRevision, AcceptedClaimsRevision = acceptedRevision,
            EmbeddingSpace = space, AnalysedAt = DateTimeOffset.UtcNow };
        var vectorsAvailable = true;
        var newVectors = 0;
        foreach (var group in accepted.Where(claim => claim.Kind is "achievement" or "summary" or "project" or "publication" || claim.SubjectId == "personal" && claim.Statement == source.Personal.Summary)
                     .GroupBy(claim => (claim.SubjectId, Text: Regex.Replace(claim.Statement, @"\s+", " ").Trim().ToLowerInvariant())))
        {
            ct.ThrowIfCancellationRequested();
            var claims = group.ToList();
            var text = claims[0].Statement;
            var hash = ReusableProseStore.Fingerprint(text);
            float[] vector = [];
            if (reusable.TryGetValue(hash, out var cached)) vector = cached.Embedding;
            else if (vectorsAvailable && newVectors < 500)
            {
                try
                {
                    newVectors++;
                    vector = await embedder.EmbedAsync(text, ct);
                    if (vector.Any(value => !float.IsFinite(value))) vector = [];
                    vectorsAvailable = vector.Length > 0;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { vectorsAvailable = false; }
            }
            index.Segments.Add(new CareerProseSegment
            {
                Id = ReusableProseStore.Fingerprint((group.Key.SubjectId ?? "career") + "|" + group.Key.Text),
                ContentHash = hash, SubjectId = group.Key.SubjectId ?? "career", Kind = claims[0].Kind, Text = text,
                ClaimIds = claims.Select(claim => claim.Id).ToList(), EvidenceIds = claims.SelectMany(claim => claim.EvidenceIds).Distinct().ToList(),
                Skills = claims.SelectMany(claim => claim.Concepts).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SkillConceptIds = claims.SelectMany(claim => claim.Concepts).Select(EvidenceLedgerBuilder.Slug).Distinct().ToList(),
                Accounts = source.Experience.Where(role => group.Key.SubjectId == $"experience:{role.Id:N}")
                    .SelectMany(role => role.InterviewEntries).Where(entry => entry.Account is not null && claims.Any(claim => claim.Statement == entry.ReviewedProse))
                    .Select(entry => entry.Account!).ToList(),
                MissingDimensions = ProseStructure.MissingDimensions(text).ToList(), Embedding = vector,
                EmbeddingSpace = space, FactSignature = FactSignature(text),
                Salience = 1d - ProseStructure.MissingDimensions(text).Count / 4d
            });
        }
        // ONNX may initialize on the first call; stamp the actual resulting space.
        index.EmbeddingSpace = Space();
        foreach (var segment in index.Segments) segment.EmbeddingSpace = index.EmbeddingSpace;
        source.ProseIndex = index;
        return index;
    }

    public static string FactSignature(string text) => string.Join('|', Regex.Matches(text.ToLowerInvariant(),
        @"\b\d+(?:[.,]\d+)*(?:%|ms|x)?|\b(?:not|never|planned|intended|prototype|pilot|production|cancelled|canceled|helped|assisted|supported|contributed|led|owned|unknown|unmeasured)\b")
        .Select(match => match.Value).Order(StringComparer.Ordinal));
}

/// <summary>Assembles complementary facts for synthesis within one role; never joins employers.</summary>
public static class ProseSynthesisAssembler
{
    private static HashSet<string> Words(string text) => Regex.Matches(text.ToLowerInvariant(), @"[a-z][a-z0-9+#.-]{2,}")
        .Select(match => match.Value).Except("the and with from for built designed implemented developed systems platform production".Split(' ')).ToHashSet();
    private static string Ownership(string text) => Regex.IsMatch(text, @"\b(helped|assisted|supported|contributed)\b", RegexOptions.IgnoreCase) ? "assisted"
        : Regex.IsMatch(text, @"\b(led|owned|managed|directed)\b", RegexOptions.IgnoreCase) ? "led" : "unspecified";
    private static string Status(string text) => string.Join('|', Regex.Matches(text.ToLowerInvariant(), @"\b(prototype|pilot|production|planned|intended|proposed|cancelled|canceled)\b").Select(match => match.Value).Distinct().Order());
    private static bool Compatible(string left, string right)
    {
        if (Ownership(left) != Ownership(right) || Status(left) != Status(right)) return false;
        const string uncertain = @"\b(not|never|unknown|unmeasured|uncertain)\b";
        if (Regex.IsMatch(left, uncertain, RegexOptions.IgnoreCase) != Regex.IsMatch(right, uncertain, RegexOptions.IgnoreCase)) return false;
        string[] Numbers(string text) => Regex.Matches(text, @"\b\d+(?:[.,]\d+)*(?:%|ms|x)?").Select(match => match.Value).ToArray();
        var a = Numbers(left);
        var b = Numbers(right);
        if (a.Length > 0 && b.Length > 0 && !a.SequenceEqual(b)) return false;
        int Direction(string text) => Regex.IsMatch(text, @"\b(reduced|decreased|lowered|cut)\b", RegexOptions.IgnoreCase) ? -1
            : Regex.IsMatch(text, @"\b(increased|raised|grew)\b", RegexOptions.IgnoreCase) ? 1 : 0;
        return Direction(left) == 0 || Direction(right) == 0 || Direction(left) == Direction(right);
    }

    public static IReadOnlyList<IReadOnlyList<string>> Assemble(IEnumerable<string> ranked, IReadOnlySet<string> targetTerms, int maximumFacts = 2)
    {
        var groups = new List<List<string>>();
        foreach (var text in ranked.Distinct(StringComparer.Ordinal))
        {
            var terms = Words(text).Intersect(targetTerms).ToHashSet();
            var group = groups.FirstOrDefault(group => group.Count < maximumFacts &&
                group.All(existing => Compatible(existing, text)) &&
                Words(group[0]).Intersect(terms).Count() >= 2 &&
                string.Join(' ', group.Append(text)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 120);
            if (group is null) groups.Add([text]); else group.Add(text);
        }
        return groups;
    }
}
