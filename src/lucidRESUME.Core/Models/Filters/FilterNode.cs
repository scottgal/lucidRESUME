namespace lucidRESUME.Core.Models.Filters;

public sealed class FilterNode
{
    public string? Field { get; init; }
    public FilterOp Op { get; init; }
    public object? Value { get; init; }
    public object? ValueTo { get; init; }
    public FilterLogic Logic { get; init; } = FilterLogic.None;
    public IReadOnlyList<FilterNode> Children { get; init; } = [];
    public bool IsLeaf => Logic == FilterLogic.None;

    public static FilterNode All(params FilterNode[] children) => new()
        { Logic = FilterLogic.All, Children = [.. children] };

    public static FilterNode Any(params FilterNode[] children) => new()
        { Logic = FilterLogic.Any, Children = [.. children] };

    public static FilterNode Not(FilterNode child) => new()
        { Logic = FilterLogic.Not, Children = [child] };

    public static FilterNode Leaf(string field, FilterOp op, object? value, object? valueTo = null) => new()
        { Field = field, Op = op, Value = value, ValueTo = valueTo };
}
