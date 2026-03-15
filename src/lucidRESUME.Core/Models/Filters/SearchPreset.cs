namespace lucidRESUME.Core.Models.Filters;

public sealed class SearchPreset
{
    public string PresetId { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsBuiltIn { get; init; }
    public FilterNode? Filter { get; set; }
    public List<SortCriterion> Sort { get; set; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    // Built-in presets — static readonly so they are allocated once, not per-access
    public static readonly SearchPreset RemoteDotNetUk = new()
    {
        PresetId = "builtin-remote-dotnet-uk",
        Name = "Remote .NET (UK)",
        IsBuiltIn = true,
        Filter = FilterNode.All(
            FilterNode.Leaf("skills", FilterOp.In, new[] { ".NET", "C#" }),
            FilterNode.Leaf("work_model", FilterOp.Equal, "Remote")
        )
    };

    public static readonly SearchPreset SeniorFintech = new()
    {
        PresetId = "builtin-senior-fintech",
        Name = "Senior Fintech",
        IsBuiltIn = true,
        Filter = FilterNode.All(
            FilterNode.Leaf("industry", FilterOp.Equal, "Fintech"),
            FilterNode.Leaf("salary_min", FilterOp.GreaterThanOrEqual, 70000m)
        )
    };

    public static readonly SearchPreset StartupEngineer = new()
    {
        PresetId = "builtin-startup",
        Name = "Startup Engineer",
        IsBuiltIn = true,
        Filter = FilterNode.Leaf("company_type", FilterOp.Equal, "Startup")
    };

    public static readonly IReadOnlyList<SearchPreset> BuiltIns =
        [RemoteDotNetUk, SeniorFintech, StartupEngineer];
}
