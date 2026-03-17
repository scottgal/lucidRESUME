namespace lucidRESUME.Core.Models.Coverage;

/// <summary>A single "question" extracted from a job description.</summary>
public sealed record JdRequirement(
    string Text,
    RequirementPriority Priority);
