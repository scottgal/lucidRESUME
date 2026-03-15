namespace lucidRESUME.Extraction.Ner;

public sealed class OnnxNerOptions
{
    /// <summary>Path to the ONNX NER model file. Leave empty to disable NER.</summary>
    public string? ModelPath { get; set; }
    public double ConfidenceThreshold { get; set; } = 0.7;
}
