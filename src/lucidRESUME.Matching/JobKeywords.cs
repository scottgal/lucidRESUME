namespace lucidRESUME.Matching;

/// <summary>
/// Single source of truth for keyword tables shared between
/// <see cref="AspectExtractor"/> and <see cref="JobFilterExecutor"/>.
/// Add or modify keyword groups here only.
/// </summary>
internal static class JobKeywords
{
    /// <summary>
    /// Industry keyword groups. Multiple industries may match a single job
    /// (e.g. "Fintech SaaS") - callers should NOT break on first match.
    /// </summary>
    public static readonly (string[] Keywords, string Industry)[] Industries =
    [
        (["fintech", "financial technology"], "Fintech"),
        (["health", "medical", "nhs", "pharma"], "Healthcare"),
        (["defence", "defense", "military", "government"], "Defence"),
        (["gambling", "betting", "casino", "gaming"], "Gambling"),
        (["e-commerce", "ecommerce", "retail"], "E-commerce"),
        (["saas", "software as a service"], "SaaS"),
        (["consulting", "consultancy"], "Consulting"),
        (["agency"], "Agency"),
    ];

    /// <summary>Culture signal keyword groups.</summary>
    public static readonly (string[] Keywords, string Signal)[] CultureSignals =
    [
        (["on-call", "on call", "pagerduty"], "On-call"),
        (["fast-paced", "fast paced", "high-pressure"], "Fast-paced"),
        (["work-life balance", "work life balance"], "Work-life balance"),
        (["flexible hours", "flexible working", "flexibility"], "Flexible hours"),
        (["remote-first", "remote first"], "Remote-first culture"),
        (["no overtime", "sustainable pace"], "Sustainable pace"),
    ];
}