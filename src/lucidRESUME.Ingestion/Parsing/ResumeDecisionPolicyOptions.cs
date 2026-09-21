namespace lucidRESUME.Ingestion.Parsing;

public sealed class ResumeDecisionPolicyOptions
{
    public double AcceptanceProbability { get; set; } = 0.80;
    public double MinimumMargin { get; set; } = 0.20;
}
