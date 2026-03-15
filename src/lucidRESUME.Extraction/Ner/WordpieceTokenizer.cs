namespace lucidRESUME.Extraction.Ner;

/// <summary>
/// Minimal BERT WordPiece tokenizer. Compatible with bert-base-uncased vocab.txt
/// (and models fine-tuned from it such as yashpwr/resume-ner-bert-v2).
///
/// Algorithm:
///   1. Lowercase + basic tokenization (whitespace / punctuation split)
///   2. WordPiece segmentation: greedy longest-prefix with ## continuation pieces
///   3. Wrap with [CLS] / [SEP] and pad to maxLength
/// </summary>
internal sealed class WordpieceTokenizer
{
    // Standard bert-base-uncased special token IDs
    private const int PadId = 0;
    private const int UnkId = 100;
    private const int ClsId = 101;
    private const int SepId = 102;

    private readonly Dictionary<string, int> _vocab;

    public WordpieceTokenizer(string vocabPath)
    {
        var lines = File.ReadAllLines(vocabPath);
        _vocab = new Dictionary<string, int>(lines.Length, StringComparer.Ordinal);
        for (int i = 0; i < lines.Length; i++)
        {
            var tok = lines[i];
            if (!string.IsNullOrEmpty(tok))
                _vocab[tok] = i;
        }
    }

    /// <summary>
    /// Encodes <paramref name="text"/> into BERT input tensors ready for ONNX inference.
    /// Character offsets map each token position back to the original string.
    /// </summary>
    public BertEncoding Encode(string text, int maxLength = 512)
    {
        var lower = text.ToLowerInvariant();
        var wordTokens = BasicTokenize(lower);

        var ids = new List<int>(maxLength) { ClsId };
        var offsets = new List<(int Start, int End)>(maxLength) { (-1, -1) };

        foreach (var (word, wStart, wEnd) in wordTokens)
        {
            if (ids.Count >= maxLength - 1) break; // reserve slot for [SEP]

            var pieces = Segment(word);
            int cursor = wStart;
            foreach (var piece in pieces)
            {
                if (ids.Count >= maxLength - 1) break;
                int pieceLen = piece.StartsWith("##") ? piece.Length - 2 : piece.Length;
                ids.Add(_vocab.TryGetValue(piece, out var vid) ? vid : UnkId);
                offsets.Add((cursor, cursor + pieceLen));
                cursor += pieceLen;
            }
        }

        ids.Add(SepId);
        offsets.Add((-1, -1));

        int realCount = ids.Count; // real tokens including [CLS] and [SEP]

        // Pad to maxLength
        while (ids.Count < maxLength) { ids.Add(PadId); offsets.Add((-1, -1)); }

        var inputIds = new long[maxLength];
        var attnMask = new long[maxLength];
        var typeIds = new long[maxLength]; // all zero — single-sentence task

        for (int i = 0; i < maxLength; i++)
        {
            inputIds[i] = ids[i];
            attnMask[i] = i < realCount ? 1L : 0L;
        }

        return new BertEncoding(inputIds, attnMask, typeIds, [.. offsets], realCount);
    }

    // ── Basic tokenization: split on whitespace; isolate punctuation ──────────

    private static List<(string Word, int Start, int End)> BasicTokenize(string text)
    {
        var result = new List<(string, int, int)>();
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }

            if (IsBertPunct(text[i]))
            {
                result.Add((text[i].ToString(), i, i + 1));
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && !IsBertPunct(text[i]))
                i++;
            result.Add((text[start..i], start, i));
        }
        return result;
    }

    // ── WordPiece: greedy longest-prefix matching ─────────────────────────────

    private List<string> Segment(string word)
    {
        if (_vocab.ContainsKey(word)) return [word];

        var subTokens = new List<string>();
        int start = 0;
        while (start < word.Length)
        {
            int end = word.Length;
            string? found = null;
            while (start < end)
            {
                var sub = start == 0 ? word[start..end] : "##" + word[start..end];
                if (_vocab.ContainsKey(sub)) { found = sub; break; }
                end--;
            }
            if (found == null) return ["[UNK]"]; // whole word is unknown
            subTokens.Add(found);
            start = end;
        }
        return subTokens;
    }

    // BERT treats CJK chars and most punctuation as isolated tokens
    private static bool IsBertPunct(char c) =>
        char.IsPunctuation(c) || char.IsSymbol(c) || (c >= 0x4E00 && c <= 0x9FFF);
}

/// <summary>BERT tokenizer output — ready to feed directly into an ONNX session.</summary>
internal sealed record BertEncoding(
    long[] InputIds,
    long[] AttentionMask,
    long[] TokenTypeIds,
    (int Start, int End)[] CharOffsets,
    /// <summary>Number of real tokens including [CLS] and [SEP], excluding padding.</summary>
    int RealTokenCount);
