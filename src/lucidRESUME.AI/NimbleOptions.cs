namespace lucidRESUME.AI;

public sealed class NimbleOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "nimble";
    public double AcceptanceProbability { get; set; } = 0.80;
    public double MinimumMargin { get; set; } = 0.20;
    public int MaxStateCharacters { get; set; } = 4000;
    public bool RedactContactDetails { get; set; } = true;
}
