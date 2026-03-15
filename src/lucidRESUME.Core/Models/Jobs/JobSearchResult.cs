using lucidRESUME.Core.Interfaces;

namespace lucidRESUME.Core.Models.Jobs;

public sealed class JobSearchResult
{
    public JobDescription Job { get; init; } = null!;
    public MatchResult? Match { get; init; }
    public string AdapterName { get; init; } = "";
    public double Score => Match?.Score ?? 0;
}
