namespace lucidRESUME.AI;

public sealed class JevOptions
{
    /// <summary>Explicit opt-in. No resume text leaves the machine when false.</summary>
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.typesafe.ai";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "jev-1.13.0";
    public double AcceptanceProbability { get; set; } = 0.80;
    public double MinimumMargin { get; set; } = 0.20;
    public int MaxStateCharacters { get; set; } = 4000;
    public bool RedactContactDetails { get; set; } = true;
}
