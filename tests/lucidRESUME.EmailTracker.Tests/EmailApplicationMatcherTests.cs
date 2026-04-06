using lucidRESUME.Core.Models.Tracking;
using lucidRESUME.EmailTracker;
using lucidRESUME.EmailTracker.Matching;

namespace lucidRESUME.EmailTracker.Tests;

public class EmailApplicationMatcherTests
{
    private static ScannedEmail MakeEmail(string from, string subject = "Update") => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        Subject = subject,
        From = from,
        Date = DateTimeOffset.UtcNow,
        BodyPreview = ""
    };

    private static JobApplication MakeApp(string company, string title, string? recruiterEmail = null) => new()
    {
        ApplicationId = Guid.NewGuid(),
        JobId = Guid.NewGuid(),
        CompanyName = company,
        JobTitle = title,
        Contact = new ContactInfo { RecruiterEmail = recruiterEmail }
    };

    [Fact]
    public void Match_RecruiterEmailExact_ReturnsFullConfidence()
    {
        var apps = new[] { MakeApp("Acme Corp", "Developer", "jane@acme.com") };
        var email = MakeEmail("jane@acme.com");

        var result = EmailApplicationMatcher.Match(email, apps);
        Assert.NotNull(result.Application);
        Assert.Equal(1.0f, result.Confidence);
        Assert.Contains("recruiter", result.MatchReason!);
    }

    [Fact]
    public void Match_DomainMatch_ReturnsMediumConfidence()
    {
        var apps = new[] { MakeApp("Google", "SRE") };
        var email = MakeEmail("noreply@google.com");

        var result = EmailApplicationMatcher.Match(email, apps);
        Assert.NotNull(result.Application);
        Assert.True(result.Confidence >= 0.6f);
        Assert.Contains("domain", result.MatchReason!);
    }

    [Fact]
    public void Match_DomainPlusTitleInSubject_ReturnsHighConfidence()
    {
        var apps = new[] { MakeApp("Microsoft", "Senior Developer") };
        var email = MakeEmail("careers@microsoft.com", "Senior Developer — Interview Invite");

        var result = EmailApplicationMatcher.Match(email, apps);
        Assert.NotNull(result.Application);
        Assert.True(result.Confidence >= 0.9f);
    }

    [Fact]
    public void Match_NoMatch_ReturnsNull()
    {
        var apps = new[] { MakeApp("Acme Corp", "Developer") };
        var email = MakeEmail("newsletter@techcrunch.com", "Weekly tech news");

        var result = EmailApplicationMatcher.Match(email, apps);
        Assert.Null(result.Application);
    }

    [Fact]
    public void Match_EmptyApplications_ReturnsNull()
    {
        var email = MakeEmail("recruiter@bigco.com");
        var result = EmailApplicationMatcher.Match(email, []);
        Assert.Null(result.Application);
    }

    [Fact]
    public void Match_CompanyNameInBody_AddsConfidence()
    {
        var apps = new[] { MakeApp("Anthropic", "Engineer") };
        var email = new ScannedEmail
        {
            MessageId = "1",
            Subject = "Your application update",
            From = "hr@someportal.com",
            Date = DateTimeOffset.UtcNow,
            BodyPreview = "Thank you for your interest in Anthropic."
        };

        var result = EmailApplicationMatcher.Match(email, apps);
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public void Match_BestMatchWins_WhenMultipleApps()
    {
        var apps = new[]
        {
            MakeApp("Amazon", "SDE"),
            MakeApp("Google", "SRE"),
        };
        var email = MakeEmail("noreply@google.com", "SRE Interview");

        var result = EmailApplicationMatcher.Match(email, apps);
        Assert.NotNull(result.Application);
        Assert.Equal("Google", result.Application!.CompanyName);
    }

    [Fact]
    public void Match_CompanyNormalization_IgnoresSuffixes()
    {
        var apps = new[] { MakeApp("Acme Inc.", "Developer") };
        var email = MakeEmail("hr@acme.com");

        var result = EmailApplicationMatcher.Match(email, apps);
        Assert.NotNull(result.Application);
    }
}
