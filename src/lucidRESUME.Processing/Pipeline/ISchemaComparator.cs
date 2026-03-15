namespace lucidRESUME.Processing.Pipeline;

/// <summary>
/// Compares two schema instances to produce a diff / match score.
/// Used for resume↔job-spec comparison, template fingerprint matching, etc.
/// </summary>
public interface ISchemaComparator<TSchema> where TSchema : class
{
    ComparisonResult Compare(TSchema source, TSchema target);
}

public sealed record ComparisonResult(
    double Score,
    IReadOnlyList<FieldDiff> Differences
);

public sealed record FieldDiff(
    string FieldPath,
    object? SourceValue,
    object? TargetValue,
    DiffKind Kind
);

public enum DiffKind { Missing, Added, Changed, Equal }
