namespace lucidRESUME.Core.Models.Jobs;

/// <summary>A bounded classification of an extracted job phrase, never a candidate's skill or claim.</summary>
public sealed record JobSkillDecision(string Label, string Classification, string Method,
    string? Provider = null, string? Model = null, double? Probability = null,
    IReadOnlyDictionary<string, double>? Probabilities = null,
    string? ProposedClassification = null, bool? Accepted = null,
    string? SourceText = null, string? SourceSection = null);
