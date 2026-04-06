using lucidRESUME.Core.Models.Tracking;
using lucidRESUME.EmailTracker;
using lucidRESUME.EmailTracker.Classification;

namespace lucidRESUME.EmailTracker.Tests;

public class EmailClassifierTests
{
    private static ScannedEmail MakeEmail(string subject, string body = "") => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        Subject = subject,
        From = "recruiter@example.com",
        Date = DateTimeOffset.UtcNow,
        BodyPreview = body
    };

    [Theory]
    [InlineData("Your application has been received", ApplicationStage.Applied)]
    [InlineData("Thank you for applying to Software Engineer", ApplicationStage.Applied)]
    [InlineData("We received your application for Senior Dev", ApplicationStage.Applied)]
    public void Classify_ApplicationConfirmation_ReturnsApplied(string subject, ApplicationStage expected)
    {
        var result = EmailClassifier.Classify(MakeEmail(subject));
        Assert.True(result.IsJobRelated);
        Assert.Equal(expected, result.SuggestedStage);
        Assert.True(result.Confidence >= 0.7f);
    }

    [Theory]
    [InlineData("Interview invitation — Senior Developer")]
    [InlineData("Please schedule a call for your interview")]
    [InlineData("Technical assessment for your application")]
    [InlineData("We'd like to invite you for a video call")]
    public void Classify_InterviewInvite_ReturnsInterview(string subject)
    {
        var result = EmailClassifier.Classify(MakeEmail(subject));
        Assert.True(result.IsJobRelated);
        Assert.Equal(ApplicationStage.Interview, result.SuggestedStage);
        Assert.True(result.Confidence >= 0.8f);
    }

    [Theory]
    [InlineData("Unfortunately, we have decided to move forward with other candidates")]
    [InlineData("Thank you for your interest — position has been filled")]
    [InlineData("We will not be moving forward with your application")]
    public void Classify_Rejection_ReturnsRejected(string subject)
    {
        var result = EmailClassifier.Classify(MakeEmail(subject));
        Assert.True(result.IsJobRelated);
        Assert.Equal(ApplicationStage.Rejected, result.SuggestedStage);
        Assert.True(result.Confidence >= 0.85f);
    }

    [Theory]
    [InlineData("Offer letter — Senior Software Engineer")]
    [InlineData("We are pleased to offer you the position")]
    public void Classify_Offer_ReturnsOffer(string subject)
    {
        var result = EmailClassifier.Classify(MakeEmail(subject));
        Assert.True(result.IsJobRelated);
        Assert.Equal(ApplicationStage.Offer, result.SuggestedStage);
        Assert.True(result.Confidence >= 0.9f);
    }

    [Theory]
    [InlineData("Phone screen — 30 minute call")]
    [InlineData("Recruiter call about your application")]
    public void Classify_Screening_ReturnsScreening(string subject)
    {
        var result = EmailClassifier.Classify(MakeEmail(subject));
        Assert.True(result.IsJobRelated);
        Assert.Equal(ApplicationStage.Screening, result.SuggestedStage);
    }

    [Fact]
    public void Classify_UnrelatedEmail_ReturnsNotJobRelated()
    {
        var result = EmailClassifier.Classify(MakeEmail("Your Amazon order has shipped"));
        Assert.False(result.IsJobRelated);
    }

    [Fact]
    public void Classify_GenericJobKeyword_ReturnsLowConfidence()
    {
        var result = EmailClassifier.Classify(MakeEmail("New job opportunities this week"));
        Assert.True(result.IsJobRelated);
        Assert.True(result.Confidence < 0.5f);
        Assert.Null(result.SuggestedStage);
    }

    [Fact]
    public void Classify_PatternInBody_StillDetects()
    {
        var result = EmailClassifier.Classify(MakeEmail(
            "Update on your application",
            "Unfortunately, we have decided to move forward with other candidates."));
        Assert.True(result.IsJobRelated);
        Assert.Equal(ApplicationStage.Rejected, result.SuggestedStage);
    }
}
