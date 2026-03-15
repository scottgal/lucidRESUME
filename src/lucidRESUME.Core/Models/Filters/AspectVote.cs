namespace lucidRESUME.Core.Models.Filters;

public sealed class AspectVote
{
    public AspectType AspectType { get; set; }
    public string AspectValue { get; set; } = "";
    public int Score { get; set; }  // accumulated: +1 per up, -1 per down, clamped to [-5, 5]
    public DateTimeOffset LastVoted { get; set; }
}
