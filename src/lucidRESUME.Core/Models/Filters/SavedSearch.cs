namespace lucidRESUME.Core.Models.Filters;

public sealed class SavedSearch
{
    public string SearchId { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public FilterNode? Filter { get; set; }
    public List<SortCriterion> Sort { get; set; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastRunAt { get; set; }
    public int LastResultCount { get; set; }
    public string? OriginalPrompt { get; set; }  // what the user typed to create this
}
