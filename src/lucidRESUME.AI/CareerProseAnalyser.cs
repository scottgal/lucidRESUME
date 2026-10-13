using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

/// <summary>Enriches persisted lexical chunks with reusable, model-stamped embeddings.</summary>
public sealed class CareerProseAnalyser(IEmbeddingService embedder) : ICareerProseEnricher
{
    public async Task<CareerProseIndex> AnalyseAsync(ResumeDocument source, CancellationToken ct = default, bool force = false)
    {
        string Space() => embedder is IEmbeddingSpaceDescriptor descriptor
            ? $"{descriptor.ModelId}|{descriptor.ModelDigest}|{descriptor.Normalization}" : embedder.GetType().FullName ?? "unknown";
        var index = CareerProseLexicalIndex.Build(source);
        var space = Space();
        string Revision(string model) => CareerProseLexicalIndex.Fingerprint(index.SearchRevision + "|" + model);
        if (!force && index.EmbeddingRevision == Revision(space)) return index;
        var available = true;
        var calls = 0;
        foreach (var segment in index.Segments)
        {
            ct.ThrowIfCancellationRequested();
            if (segment.EmbeddingSpace != space) segment.Embedding = [];
            if (segment.Embedding.Length == 0 && available && calls < 500)
            {
                try
                {
                    calls++;
                    segment.Embedding = await embedder.EmbedAsync(segment.Text, ct);
                    if (segment.Embedding.Any(value => !float.IsFinite(value))) segment.Embedding = [];
                    available = segment.Embedding.Length > 0;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { available = false; }
            }
        }
        index.EmbeddingSpace = Space(); // ONNX can initialize on its first call.
        foreach (var segment in index.Segments) segment.EmbeddingSpace = index.EmbeddingSpace;
        index.EmbeddingRevision = Revision(index.EmbeddingSpace);
        return index;
    }

    public static string FactSignature(string text) => CareerProseLexicalIndex.FactSignature(text);
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
