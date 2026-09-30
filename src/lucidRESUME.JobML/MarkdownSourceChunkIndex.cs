using System.Security.Cryptography;
using System.Text;

namespace lucidRESUME.JobML;

/// <summary>
/// Exact, ordered slices of the human Markdown transcript. Offsets and lengths are
/// UTF-16 string positions; hashes cover the UTF-8 representation of each slice.
/// </summary>
public sealed record MarkdownSourceChunk(int Index, int SourceStart, int SourceLength,
    string Sha256, string Text);

public sealed class MarkdownSourceChunkIndex
{
    private MarkdownSourceChunkIndex(string sourceSha256, IReadOnlyList<MarkdownSourceChunk> chunks)
    {
        SourceSha256 = sourceSha256;
        Chunks = chunks;
    }

    public string SourceSha256 { get; }
    public IReadOnlyList<MarkdownSourceChunk> Chunks { get; }

    public static MarkdownSourceChunkIndex Create(string markdown, int maximumCharacters = 4096)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (maximumCharacters < 128) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        var chunks = new List<MarkdownSourceChunk>();
        for (var start = 0; start < markdown.Length;)
        {
            var end = Math.Min(markdown.Length, start + maximumCharacters);
            if (end < markdown.Length)
            {
                var boundary = markdown.LastIndexOf("\n\n", end - 1, end - start, StringComparison.Ordinal);
                if (boundary >= start + maximumCharacters / 2) end = boundary + 2;
                else if (char.IsHighSurrogate(markdown[end - 1]) && char.IsLowSurrogate(markdown[end])) end--;
            }
            var slice = markdown[start..end];
            chunks.Add(new MarkdownSourceChunk(chunks.Count, start, slice.Length, Hash(slice), slice));
            start = end;
        }
        return new MarkdownSourceChunkIndex(Hash(markdown), chunks);
    }

    public string Reassemble()
    {
        var result = new StringBuilder();
        for (var index = 0; index < Chunks.Count; index++)
        {
            var chunk = Chunks[index];
            if (chunk.Index != index || chunk.SourceStart != result.Length)
                throw new InvalidDataException("Transcript chunks are out of order.");
            if (chunk.SourceLength != chunk.Text.Length || chunk.Sha256 != Hash(chunk.Text))
                throw new InvalidDataException("A transcript chunk changed after indexing.");
            result.Append(chunk.Text);
        }
        var markdown = result.ToString();
        if (Hash(markdown) != SourceSha256)
            throw new InvalidDataException("Reassembled transcript differs from the source.");
        return markdown;
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
