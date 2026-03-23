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

    /// <summary>Whether to lowercase text before tokenization. Set false for cased models (e.g. bert-base-NER).</summary>
    public bool LowerCase { get; set; } = true;

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
    /// Entity type mapping: BIO entity name → internal classification.
    /// If null, uses the default mapping for the model type.
    /// </summary>
    public Dictionary<string, string>? EntityTypeMap { get; set; }

    /// <summary>
    /// Default BIO label list for yashpwr/resume-ner-bert-v2 (25 labels).
    /// </summary>
    public static readonly string[] ResumeNerLabels =
    [
        "O",
        "B-College Name", "I-College Name",
        "B-Companies worked at", "I-Companies worked at",
        "B-Degree", "I-Degree",
        "B-Designation", "I-Designation",
        "B-Email Address", "I-Email Address",
        "B-Graduation Year", "I-Graduation Year",
        "B-Location", "I-Location",
        "B-Name", "I-Name",
        "B-Phone", "I-Phone",
        "B-Skills", "I-Skills",
        "B-UNKNOWN", "I-UNKNOWN",
        "B-Years of Experience", "I-Years of Experience",
    ];

    /// <summary>
    /// Default BIO label list for dslim/bert-base-NER (9 labels).
    /// </summary>
    public static readonly string[] GeneralNerLabels =
    [
        "O",
        "B-MISC", "I-MISC",
        "B-PER", "I-PER",
        "B-ORG", "I-ORG",
        "B-LOC", "I-LOC",
    ];

    public static readonly string[] DefaultLabels = ResumeNerLabels;
}
