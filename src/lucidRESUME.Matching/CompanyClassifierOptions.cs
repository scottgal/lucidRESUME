namespace lucidRESUME.Matching;

public sealed class CompanyTypeRule
{
    /// <summary>Must match a <see cref="lucidRESUME.Core.Models.Jobs.CompanyType"/> enum name.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>If any keyword appears in (title + JD text), this rule fires. Order matters — first match wins.</summary>
    public string[] Keywords { get; set; } = [];
}

public sealed class CompanyClassifierOptions
{
    /// <summary>
    /// Ordered list of classification rules. First matching rule wins.
    /// Override in appsettings.json to add/reorder signals.
    /// </summary>
    public List<CompanyTypeRule> Rules { get; set; } =
    [
        new() { Type = "Academic",    Keywords = ["university", "college", "academic", "faculty", "lecturer",
                                                   "professor", "research institute", "phd programme"] },
        new() { Type = "Public",      Keywords = ["nhs", "gov.uk", "local authority", "council", "charity",
                                                   "non-profit", "nonprofit", "public sector", "civil service"] },
        new() { Type = "Finance",     Keywords = ["investment bank", "hedge fund", "asset management",
                                                   "financial services", "insurance", "regulated environment",
                                                   "auditing", "accounting firm"] },
        new() { Type = "Consultancy", Keywords = ["consultancy", "consulting firm", "advisory", "big 4",
                                                   "systems integrator", "professional services"] },
        new() { Type = "Agency",      Keywords = [" agency", "digital agency", "creative agency",
                                                   "marketing agency", "advertising agency", "media agency"] },
        new() { Type = "Enterprise",  Keywords = ["enterprise", "corporate", "ftse", "fortune 500",
                                                   "fortune500", "global company", "multinational", "plc"] },
        new() { Type = "ScaleUp",     Keywords = ["scale-up", "scaleup", "growth stage", "series c",
                                                   "series d", "series e", "pre-ipo", "post-series b"] },
        new() { Type = "Startup",     Keywords = ["startup", "start-up", "seed", "series a", "series b",
                                                   "early stage", "pre-seed", "venture-backed", "founded in 20"] },
    ];
}
