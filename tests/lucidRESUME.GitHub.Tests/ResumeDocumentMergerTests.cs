using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;
using System.Security.Cryptography;
using System.Text;

namespace lucidRESUME.GitHub.Tests;

/// <summary>
/// Deterministic embedding test double. Do not use string.GetHashCode here: it is
/// process-randomized and made semantic-match tests intermittently merge unrelated skills.
/// </summary>
sealed class TestEmbeddingService : IEmbeddingService
{
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));
        var emb = new float[hash.Length];
        for (var i = 0; i < hash.Length; i++)
            emb[i] = (hash[i] - 127.5f) / 127.5f;
        var norm = MathF.Sqrt(emb.Sum(x => x * x));
        if (norm > 0) for (var i = 0; i < 8; i++) emb[i] /= norm;
        return Task.FromResult(emb);
    }

    public float CosineSimilarity(float[] a, float[] b)
    {
        var dot = a.Zip(b, (x, y) => x * y).Sum();
        var normA = MathF.Sqrt(a.Sum(x => x * x));
        var normB = MathF.Sqrt(b.Sum(x => x * x));
        return normA > 0 && normB > 0 ? dot / (normA * normB) : 0;
    }
}

sealed class FailingEmbeddingService : IEmbeddingService
{
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        throw new InvalidDataException("simulated corrupt model");

    public float CosineSimilarity(float[] a, float[] b) => throw new NotSupportedException();
}

public class ResumeDocumentMergerTests
{
    private readonly ResumeDocumentMerger _merger = new(new TestEmbeddingService());

    [Fact]
    public async Task PreviewMerge_WhenEmbeddingModelFails_UsesDeterministicFallback()
    {
        var target = ResumeDocument.Create("base.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Acme Ltd",
            Title = "Engineer",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2022, 1, 1)
        });
        target.Skills.Add(new Skill { Name = "C#" });

        var incoming = ResumeDocument.Create("variant.docx", "application/docx", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Acme Ltd",
            Title = "Senior Engineer",
            StartDate = new DateOnly(2020, 2, 1),
            EndDate = new DateOnly(2022, 1, 1)
        });
        incoming.Experience.Add(new WorkExperience { Company = "Different Company", Title = "Consultant" });
        incoming.Skills.Add(new Skill { Name = "C#" });
        incoming.Skills.Add(new Skill { Name = "Kubernetes" });

        var preview = await new ResumeDocumentMerger(new FailingEmbeddingService())
            .PreviewMergeAsync(target, incoming, "variant.docx");

        Assert.Single(preview.MergedExperience);
        Assert.Single(preview.NewExperience);
        Assert.Single(preview.UpdatedSkills);
        Assert.Single(preview.NewSkills);
    }

    [Fact]
    public async Task PreviewMerge_FirstImportRejectsPlaceholderAndInvertedRoleByDefault()
    {
        var incoming = ResumeDocument.Create("template.docx", "application/docx", 100);
        incoming.Personal.FullName = "Alex Example";
        incoming.Personal.Summary = "Desired Job Title: Engineering Lead";
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Example Ltd",
            Title = "Engineering Lead",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2025, 2, 1)
        });

        var target = ResumeDocumentMerger.CreateReviewTarget(incoming);
        var preview = await _merger.PreviewMergeAsync(target, incoming, "template.docx");

        var summary = Assert.Single(preview.PersonalInfoChanges,
            change => change.FieldName == nameof(PersonalInfo.Summary));
        Assert.True(summary.IsConflict);
        Assert.False(summary.IsAccepted);
        Assert.False(Assert.Single(preview.NewExperience).IsAccepted);
        Assert.Contains(preview.Anomalies, anomaly => anomaly.Type == AnomalyType.PlaceholderContent);
        Assert.Contains(preview.Anomalies, anomaly => anomaly.Type == AnomalyType.InvertedDateRange);
    }

    [Fact]
    public async Task PreviewMerge_ConflictingIdentityFieldRequiresExplicitAcceptance()
    {
        var target = ResumeDocument.Create("reviewed.md", "text/markdown", 100);
        target.Personal.Email = "reviewed@example.com";
        var incoming = ResumeDocument.Create("old.docx", "application/docx", 100);
        incoming.Personal.Email = "stale@example.com";

        var preview = await _merger.PreviewMergeAsync(target, incoming, "old.docx");

        var email = Assert.Single(preview.PersonalInfoChanges);
        Assert.True(email.IsConflict);
        Assert.False(email.IsAccepted);
        ResumeDocumentMerger.ApplyPreview(target, preview);
        Assert.Equal("reviewed@example.com", target.Personal.Email);
    }

    [Fact]
    public async Task PreviewMerge_ReviewedTranscriptOverridesStaleContactAndRoleDates()
    {
        var target = ResumeDocument.Create("old.docx", "application/docx", 100);
        target.Personal.Email = "old@example.com";
        target.Experience.Add(new WorkExperience
        {
            Company = "ZenChef Limited",
            Title = "Lead Contract Developer",
            StartDate = new DateOnly(2024, 10, 1),
            IsCurrent = true
        });
        var transcript = ResumeDocument.Create("reviewed.md", "text/markdown", 100);
        transcript.Personal.Email = "reviewed@example.com";
        transcript.Experience.Add(new WorkExperience
        {
            Company = "ZenChef Ltd / Formitable",
            Title = "Lead Contract Developer",
            StartDate = new DateOnly(2024, 10, 1),
            EndDate = new DateOnly(2026, 5, 1),
            IsCurrent = false,
            Achievements = ["Took technical ownership of the acquired production platform."]
        });

        var preview = await _merger.PreviewMergeAsync(target, transcript, "reviewed.md",
            incomingIsAuthoritative: true);
        ResumeDocumentMerger.ApplyPreview(target, preview);

        Assert.Equal("reviewed@example.com", target.Personal.Email);
        var role = Assert.Single(target.Experience);
        Assert.Equal("ZenChef Ltd / Formitable", role.Company);
        Assert.Equal(new DateOnly(2026, 5, 1), role.EndDate);
        Assert.False(role.IsCurrent);
        Assert.Single(role.Achievements);
    }

    [Fact]
    public async Task ReviewedMarkdownTranscript_ParsesAndOverlaysRawResumeEndToEnd()
    {
        var target = ResumeDocument.Create("old.docx", "application/docx", 100);
        target.Personal.Email = "stale@example.com";
        target.Experience.Add(new WorkExperience
        {
            Company = "Example Platform Ltd",
            Title = "Lead Engineer",
            StartDate = new DateOnly(2024, 10, 1),
            IsCurrent = true
        });
        const string markdown = """
            # Alex Example

            alex@example.com | 07498 479614 - please email in the first instance - | Glasgow, United Kingdom

            ## Summary

            Hands-on engineering leader.

            ## Experience

            ### Lead Engineer | Example Platform Ltd
            *Oct 2024 - May 2026*

            - Took end-to-end ownership of a distributed production platform.
            """;
        var transcript = ResumeDocument.Create("complete-transcript.md", "text/markdown", markdown.Length);
        MarkdownSectionParser.PopulateSections(transcript, markdown);

        var preview = await _merger.PreviewMergeAsync(target, transcript, transcript.FileName,
            incomingIsAuthoritative: true);
        ResumeDocumentMerger.ApplyPreview(target, preview);

        Assert.Equal("alex@example.com", target.Personal.Email);
        Assert.Equal("please email in the first instance", target.Personal.ContactPreference);
        var role = Assert.Single(target.Experience);
        Assert.Equal(new DateOnly(2026, 5, 1), role.EndDate);
        Assert.False(role.IsCurrent);
        Assert.Contains(role.Achievements, text => text.Contains("end-to-end ownership"));
    }

    [Fact]
    public async Task ApplyPreview_RebuildsLedgerFromAcceptedItems()
    {
        var incoming = ResumeDocument.Create("first.docx", "application/docx", 100);
        incoming.Personal.FullName = "Alex Example";
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Example Ltd",
            Title = "Engineering Lead",
            StartDate = new DateOnly(2022, 1, 1),
            EndDate = new DateOnly(2025, 1, 1),
            Achievements = ["Led delivery of a production platform migration."]
        });

        var target = ResumeDocumentMerger.CreateReviewTarget(incoming);
        var preview = await _merger.PreviewMergeAsync(target, incoming, "first.docx");
        ResumeDocumentMerger.ApplyPreview(target, preview);

        Assert.Single(target.Experience);
        Assert.NotEmpty(target.EvidenceLedger.Claims);
        Assert.Contains(target.Experience[0].ImportSources, source => source == "first.docx");
    }

    [Fact]
    public async Task MergeInto_MergesExperienceByCompanyAndDateOverlap()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            Title = "Developer",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2023, 6, 1),
        });

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            Title = "Senior Developer",
            StartDate = new DateOnly(2020, 3, 1),
            EndDate = new DateOnly(2023, 5, 1),
        });

        await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Single(target.Experience);
        Assert.Equal("Senior Developer", target.Experience[0].Title);
        Assert.Contains("LinkedIn", target.Experience[0].ImportSources);
    }

    [Fact]
    public async Task MergeInto_AddsNewExperience()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2023, 1, 1),
        });

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Totally Different Inc",
            StartDate = new DateOnly(2023, 6, 1),
        });

        await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Equal(2, target.Experience.Count);
    }

    [Fact]
    public async Task MergeInto_DetectsNameMismatch()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Personal.FullName = "John Smith";

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Personal.FullName = "Jonathan Smith";

        var anomalies = await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Contains(anomalies, a => a.Type == AnomalyType.NameMismatch);
        Assert.Equal("John Smith", target.Personal.FullName);
    }

    [Fact]
    public async Task MergeInto_MergesSkillsByName()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Skills.Add(new Skill { Name = "C#", Category = "Language" });

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Skills.Add(new Skill { Name = "C#", EndorsementCount = 5 });
        incoming.Skills.Add(new Skill { Name = "Docker" });

        await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Equal(2, target.Skills.Count);
        var csharp = target.Skills.First(s => s.Name == "C#");
        Assert.Equal("Language", csharp.Category);
        Assert.Contains("LinkedIn", csharp.ImportSources);
    }

    [Fact]
    public async Task MergeInto_ReplacesImplausibleEducationRangeWithPlausibleEvidence()
    {
        var target = ResumeDocument.Create("derived.md", "text/markdown", 100);
        target.Education.Add(new Education
        {
            Institution = "University of Stirling",
            Degree = "BSc (Hons) Psychology",
            StartDate = new DateOnly(1992, 9, 1),
            EndDate = new DateOnly(1992, 10, 1)
        });
        var incoming = ResumeDocument.Create("source.docx", "application/docx", 100);
        incoming.Education.Add(new Education
        {
            Institution = "University of Stirling",
            Degree = "BSc (Hons) Psychology",
            StartDate = new DateOnly(1992, 9, 1),
            EndDate = new DateOnly(1996, 6, 1)
        });

        var anomalies = await _merger.MergeIntoAsync(target, incoming, "source.docx");

        var education = Assert.Single(target.Education);
        Assert.Equal(new DateOnly(1992, 9, 1), education.StartDate);
        Assert.Equal(new DateOnly(1996, 6, 1), education.EndDate);
        Assert.Contains(anomalies, anomaly => anomaly.Type == AnomalyType.DateMismatch);
    }

    [Fact]
    public async Task MergeInto_DoesNotSilentlyChooseBetweenTwoPlausibleEducationRanges()
    {
        var target = ResumeDocument.Create("first.docx", "application/docx", 100);
        target.Education.Add(new Education
        {
            Institution = "Example University",
            StartDate = new DateOnly(2018, 9, 1),
            EndDate = new DateOnly(2021, 6, 1)
        });
        var incoming = ResumeDocument.Create("second.docx", "application/docx", 100);
        incoming.Education.Add(new Education
        {
            Institution = "Example University",
            StartDate = new DateOnly(2017, 9, 1),
            EndDate = new DateOnly(2021, 6, 1)
        });

        var anomalies = await _merger.MergeIntoAsync(target, incoming, "second.docx");

        var education = Assert.Single(target.Education);
        Assert.Equal(new DateOnly(2018, 9, 1), education.StartDate);
        Assert.Equal(new DateOnly(2021, 6, 1), education.EndDate);
        Assert.Contains(anomalies, anomaly => anomaly.Type == AnomalyType.DateMismatch);
    }

    [Fact]
    public async Task MergeInto_FillsPersonalInfoGaps()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Personal.FullName = "John Smith";
        target.Personal.Email = "john@test.com";

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Personal.FullName = "John Smith";
        incoming.Personal.Phone = "+44 123 456";
        incoming.Personal.Location = "London";

        var anomalies = await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Empty(anomalies);
        Assert.Equal("+44 123 456", target.Personal.Phone);
        Assert.Equal("London", target.Personal.Location);
    }

    [Fact]
    public async Task MergeInto_CopiesContactPreference()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        var incoming = ResumeDocument.Create("reviewed.md", "text/markdown", 100);
        incoming.Personal.ContactPreference = "please email in the first instance";

        await _merger.MergeIntoAsync(target, incoming, "reviewed.md");

        Assert.Equal("please email in the first instance", target.Personal.ContactPreference);
    }

    [Fact]
    public async Task MergeInto_ExplicitEndDateCannotBeReopenedByStalePresentMarker()
    {
        var target = ResumeDocument.Create("reviewed.md", "text/markdown", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Zenchef",
            Title = "Lead Developer",
            StartDate = new DateOnly(2024, 10, 1),
            EndDate = new DateOnly(2026, 5, 1),
            IsCurrent = false
        });
        var incoming = ResumeDocument.Create("old-cv.docx", "application/docx", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Zenchef Ltd",
            Title = "Lead Developer",
            StartDate = new DateOnly(2024, 10, 1),
            IsCurrent = true
        });

        await _merger.MergeIntoAsync(target, incoming, "old-cv.docx");

        var role = Assert.Single(target.Experience);
        Assert.Equal(new DateOnly(2026, 5, 1), role.EndDate);
        Assert.False(role.IsCurrent);
    }

    [Fact]
    public async Task MergeInto_ExplicitIncomingEndDateClosesCurrentRole()
    {
        var target = ResumeDocument.Create("old-cv.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Zenchef Ltd",
            Title = "Lead Developer",
            StartDate = new DateOnly(2024, 10, 1),
            IsCurrent = true
        });
        var incoming = ResumeDocument.Create("reviewed.md", "text/markdown", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Zenchef",
            Title = "Lead Developer",
            StartDate = new DateOnly(2024, 10, 1),
            EndDate = new DateOnly(2026, 5, 1),
            IsCurrent = false
        });

        await _merger.MergeIntoAsync(target, incoming, "reviewed.md");

        var role = Assert.Single(target.Experience);
        Assert.Equal(new DateOnly(2026, 5, 1), role.EndDate);
        Assert.False(role.IsCurrent);
    }

    [Fact]
    public async Task MergeInto_MergesAchievements()
    {
        var target = ResumeDocument.Create("resume.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2023, 1, 1),
            Achievements = ["Built a platform", "Led a team"],
        });

        var incoming = ResumeDocument.Create("linkedin.zip", "application/zip", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2023, 1, 1),
            Achievements = ["Built a platform", "Reduced costs by 30%"],
        });

        await _merger.MergeIntoAsync(target, incoming, "LinkedIn");

        Assert.Single(target.Experience);
        Assert.Equal(3, target.Experience[0].Achievements.Count);
    }

    [Fact]
    public async Task MergeInto_MergesReorderedDevelopmentLeadTitlesWithoutWideningConflictingDate()
    {
        var target = ResumeDocument.Create("new.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Dell",
            Title = "Development Lead",
            StartDate = new DateOnly(2011, 1, 1),
            EndDate = new DateOnly(2011, 7, 1)
        });
        var incoming = ResumeDocument.Create("old.docx", "application/docx", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Dell Limited",
            Title = "Lead Developer",
            StartDate = new DateOnly(2010, 1, 1),
            EndDate = new DateOnly(2010, 8, 1)
        });

        var anomalies = await _merger.MergeIntoAsync(target, incoming, "old.docx");

        Assert.Single(target.Experience);
        Assert.Equal(new DateOnly(2011, 1, 1), target.Experience[0].StartDate);
        Assert.Equal(new DateOnly(2011, 7, 1), target.Experience[0].EndDate);
        Assert.Contains(anomalies, anomaly => anomaly.Type == AnomalyType.DateMismatch);
    }

    [Fact]
    public async Task MergeInto_PreservesConsecutiveDistinctRolesAtSameCompany()
    {
        var target = ResumeDocument.Create("base.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "Microsoft",
            Title = "Application Development Consultant",
            StartDate = new DateOnly(2005, 6, 1),
            EndDate = new DateOnly(2007, 1, 1)
        });
        var incoming = ResumeDocument.Create("base.docx", "application/docx", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Microsoft",
            Title = "Program Manager",
            StartDate = new DateOnly(2007, 1, 1),
            EndDate = new DateOnly(2009, 10, 1)
        });

        await _merger.MergeIntoAsync(target, incoming, "base.docx");

        Assert.Equal(2, target.Experience.Count);
    }

    [Fact]
    public async Task MergeInto_ResolvesMissingCompanyFreelanceVariantIntoNamedBusiness()
    {
        var target = ResumeDocument.Create("old.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Title = "Freelance Developer",
            StartDate = new DateOnly(2012, 1, 1),
            IsCurrent = true
        });
        var incoming = ResumeDocument.Create("reviewed.docx", "application/docx", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "Mostlylucid Limited",
            Title = "Freelance Consultant / Lead Developer",
            StartDate = new DateOnly(2012, 1, 1),
            IsCurrent = true
        });

        await _merger.MergeIntoAsync(target, incoming, "reviewed.docx");

        var role = Assert.Single(target.Experience);
        Assert.Equal("Mostlylucid Limited", role.Company);
        Assert.Equal("Freelance Consultant / Lead Developer", role.Title);
    }

    [Fact]
    public async Task MergeInto_MergesAdjacentCompanyNameTransposition()
    {
        var target = ResumeDocument.Create("correct.docx", "application/docx", 100);
        target.Experience.Add(new WorkExperience
        {
            Company = "BlackID",
            Title = "Web Developer",
            StartDate = new DateOnly(1999, 2, 1),
            EndDate = new DateOnly(2000, 6, 1)
        });
        var incoming = ResumeDocument.Create("typo.docx", "application/docx", 100);
        incoming.Experience.Add(new WorkExperience
        {
            Company = "BalckID",
            Title = "Web Developer",
            StartDate = new DateOnly(1999, 2, 1),
            EndDate = new DateOnly(2000, 6, 1)
        });

        await _merger.MergeIntoAsync(target, incoming, "typo.docx");

        Assert.Single(target.Experience);
        Assert.Equal("BlackID", target.Experience[0].Company);
    }
}
