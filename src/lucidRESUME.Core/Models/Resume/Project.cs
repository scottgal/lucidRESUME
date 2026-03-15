namespace lucidRESUME.Core.Models.Resume;

public sealed class Project
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<string> Technologies { get; set; } = [];
    public string? Url { get; set; }
    public DateOnly? Date { get; set; }
}
