namespace lucidRESUME.JobSpec.Extraction;

/// <summary>
/// A candidate value for a JD field, produced by any extractor.
/// Multiple candidates for the same field are fused by confidence.
/// </summary>
public sealed record JdFieldCandidate(
    string FieldType,    // "title", "company", "skill", "location", "yearsExp", "remote"
    string Value,
    double Confidence,
    string Source);      // "ner", "regex", "llm", "recognizer"
