using lucidRESUME.Core.Interfaces;
using lucidRESUME.Extraction.Recognizers;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucidRESUME.Extraction.Tests.Recognizers;

public class ResumeRecognizerDetectorTests
{
    private readonly ResumeRecognizerDetector _detector = new(NullLogger<ResumeRecognizerDetector>.Instance);

    [Fact]
    public async Task DetectAsync_FindsEmailInText()
    {
        var context = new DetectionContext("Contact me at john.smith@example.com for more info.");
        var entities = await _detector.DetectAsync(context);
        Assert.Contains(entities, e => e.Classification == "Email");
    }

    [Fact]
    public async Task DetectAsync_FindsPhoneInText()
    {
        var context = new DetectionContext("Call me on +44 7700 900123");
        var entities = await _detector.DetectAsync(context);
        Assert.Contains(entities, e => e.Classification == "PhoneNumber");
    }

    [Fact]
    public async Task DetectAsync_FindsDateRangeInExperience()
    {
        var context = new DetectionContext("Senior Developer, Acme Corp, January 2020 - March 2024");
        var entities = await _detector.DetectAsync(context);
        Assert.Contains(entities, e => e.Classification == "DateRange" || e.Classification == "Date");
    }
}
