using lucidRESUME.Core.Models.Tracking;

namespace lucidRESUME.EmailTracker.Matching;

public sealed class MatchResult
{
    public JobApplication? Application { get; init; }
    public float Confidence { get; init; }
    public string? MatchReason { get; init; }
}

/// <summary>
/// Matches a scanned email to an existing JobApplication by sender domain, subject, or recruiter email.
/// </summary>
public static class EmailApplicationMatcher
{
    public static MatchResult Match(ScannedEmail email, IReadOnlyList<JobApplication> applications)
    {
        if (applications.Count == 0)
            return new MatchResult();

        var bestMatch = (Application: (JobApplication?)null, Confidence: 0f, Reason: "");
        var senderDomain = ExtractDomain(email.From);

        foreach (var app in applications)
        {
            var confidence = 0f;
            var reason = "";

            // Recruiter email exact match (strongest signal)
            if (!string.IsNullOrEmpty(app.Contact.RecruiterEmail) &&
                email.From.Equals(app.Contact.RecruiterEmail, StringComparison.OrdinalIgnoreCase))
            {
                confidence = 1.0f;
                reason = "recruiter email match";
            }
            else
            {
                // Company domain match
                if (!string.IsNullOrEmpty(app.CompanyName) && !string.IsNullOrEmpty(senderDomain))
                {
                    var normalizedCompany = NormalizeCompany(app.CompanyName);
                    var normalizedDomain = senderDomain.Split('.')[0]; // "google" from "google.com"

                    if (normalizedDomain.Contains(normalizedCompany, StringComparison.OrdinalIgnoreCase) ||
                        normalizedCompany.Contains(normalizedDomain, StringComparison.OrdinalIgnoreCase))
                    {
                        confidence += 0.6f;
                        reason = "domain match";
                    }
                }

                // Job title in subject
                if (!string.IsNullOrEmpty(app.JobTitle) &&
                    email.Subject.Contains(app.JobTitle, StringComparison.OrdinalIgnoreCase))
                {
                    confidence += 0.3f;
                    reason += (reason.Length > 0 ? " + " : "") + "title in subject";
                }

                // Company name in subject/body
                if (!string.IsNullOrEmpty(app.CompanyName) &&
                    (email.Subject.Contains(app.CompanyName, StringComparison.OrdinalIgnoreCase) ||
                     email.BodyPreview.Contains(app.CompanyName, StringComparison.OrdinalIgnoreCase)))
                {
                    confidence += 0.2f;
                    reason += (reason.Length > 0 ? " + " : "") + "company in text";
                }
            }

            if (confidence > bestMatch.Confidence)
                bestMatch = (app, confidence, reason);
        }

        return new MatchResult
        {
            Application = bestMatch.Confidence >= 0.5f ? bestMatch.Application : null,
            Confidence = bestMatch.Confidence,
            MatchReason = bestMatch.Reason
        };
    }

    private static string? ExtractDomain(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex >= 0 ? email[(atIndex + 1)..] : null;
    }

    private static string NormalizeCompany(string company) =>
        company.Replace(" ", "")
               .Replace(",", "")
               .Replace(".", "")
               .Replace("Inc", "", StringComparison.OrdinalIgnoreCase)
               .Replace("Ltd", "", StringComparison.OrdinalIgnoreCase)
               .Replace("LLC", "", StringComparison.OrdinalIgnoreCase)
               .Replace("Corp", "", StringComparison.OrdinalIgnoreCase)
               .Trim()
               .ToLowerInvariant();
}
