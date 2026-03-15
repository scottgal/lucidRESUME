namespace lucidRESUME.Extraction.Ner;

public sealed class OnnxNerOptions
{
    /// <summary>Path to the ONNX model file (model.onnx). Leave empty to disable NER.</summary>
    public string? ModelPath { get; set; }

    /// <summary>
    /// Path to the vocab.txt file for the WordPiece tokenizer.
    /// Defaults to vocab.txt in the same directory as <see cref="ModelPath"/>.
    /// </summary>
    public string? VocabPath { get; set; }

    /// <summary>Minimum token-level confidence to emit an entity (0–1).</summary>
    public double ConfidenceThreshold { get; set; } = 0.70;

    /// <summary>Maximum BERT sequence length (must match model's max_position_embeddings).</summary>
    public int MaxSequenceLength { get; set; } = 512;

    /// <summary>
    /// Ordered list of label strings matching the model's id2label config.
    /// Defaults to labels from yashpwr/resume-ner-bert-v2.
    /// Override in appsettings if using a different model.
    /// </summary>
    public string[]? Labels { get; set; }

    // ── Resolved helpers ─────────────────────────────────────────────────────

    public string? ResolvedVocabPath =>
        !string.IsNullOrEmpty(VocabPath) ? VocabPath :
        !string.IsNullOrEmpty(ModelPath) ? Path.Combine(Path.GetDirectoryName(ModelPath)!, "vocab.txt") :
        null;

    /// <summary>
    /// Default BIO label list for yashpwr/resume-ner-bert-v2 (25 labels).
    /// </summary>
    public static readonly string[] DefaultLabels =
    [
        "O",
        "B-Name", "I-Name",
        "B-Degree", "I-Degree",
        "B-Graduation Year", "I-Graduation Year",
        "B-Years of Experience", "I-Years of Experience",
        "B-Companies worked at", "I-Companies worked at",
        "B-College Name", "I-College Name",
        "B-Designation", "I-Designation",
        "B-Skills", "I-Skills",
        "B-Location", "I-Location",
        "B-Links", "I-Links",
        "B-Email Address", "I-Email Address",
        "B-Worked as", "I-Worked as",
    ];
}
