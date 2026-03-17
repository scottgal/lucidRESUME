using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Matching;

/// <summary>
/// Classifies a job's employer type from JD text and title.
/// First definitive match wins — a company is one type.
/// </summary>
public sealed class CompanyClassifier
{
    public CompanyType Classify(JobDescription job)
    {
        var text = ((job.Title ?? "") + " " + job.RawText).ToLowerInvariant();

        if (ContainsAny(text, "university", "college", "academic", "faculty", "lecturer",
                              "professor", "research institute", "phd programme"))
            return CompanyType.Academic;

        if (ContainsAny(text, "nhs", "gov.uk", "local authority", "council", "charity",
                              "non-profit", "nonprofit", "public sector", "civil service"))
            return CompanyType.Public;

        if (ContainsAny(text, "investment bank", "hedge fund", "asset management",
                              "financial services", "insurance", "regulated environment",
                              "auditing", "accounting firm"))
            return CompanyType.Finance;

        if (ContainsAny(text, "consultancy", "consulting firm", "advisory", "big 4",
                              "systems integrator", "professional services"))
            return CompanyType.Consultancy;

        if (ContainsAny(text, " agency", "digital agency", "creative agency", "marketing agency",
                              "advertising agency", "media agency"))
            return CompanyType.Agency;

        if (ContainsAny(text, "enterprise", "corporate", "ftse", "fortune 500", "fortune500",
                              "global company", "multinational", "plc"))
            return CompanyType.Enterprise;

        if (ContainsAny(text, "scale-up", "scaleup", "growth stage", "series c", "series d",
                              "series e", "pre-ipo", "post-series b"))
            return CompanyType.ScaleUp;

        if (ContainsAny(text, "startup", "start-up", "seed", "series a", "series b",
                              "early stage", "pre-seed", "venture-backed", "founded in 20"))
            return CompanyType.Startup;

        return CompanyType.Unknown;
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
}
