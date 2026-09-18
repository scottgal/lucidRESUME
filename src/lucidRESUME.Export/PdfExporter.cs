using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace lucidRESUME.Export;

/// <summary>
/// Exports a ResumeDocument to a professionally formatted PDF using QuestPDF.
/// Pure C#, cross-platform, no external tools.
/// </summary>
public sealed class PdfExporter : IResumeExporter
{
    static PdfExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ExportFormat Format => ExportFormat.Pdf;

    public Task<byte[]> ExportAsync(ResumeDocument resume, CancellationToken ct = default)
    {
        var template = ExportArtifact.Template(resume);
        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(template.PageMarginPoints);
                // QuestPDF bundles Lato. Using it keeps PDF output deterministic on
                // clean machines and CI instead of depending on system fonts.
                page.DefaultTextStyle(x => x.FontSize(template.BodyFontSize).FontFamily("Lato"));

                page.Header().Element(c => ComposeHeader(c, resume.Personal, template));
                page.Content().Element(c => ComposeContent(c, resume, template));
                page.Footer().AlignCenter().Text(t =>
                {
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();

        return Task.FromResult(bytes);
    }

    private static void ComposeHeader(IContainer container, PersonalInfo p, ResumeTemplate template)
    {
        container.Column(col =>
        {
            if (p.FullName != null)
                col.Item().Text(p.FullName).FontSize(22).Bold().FontColor($"#{template.AccentHex}");

            var contacts = new List<string>();
            if (p.Email != null) contacts.Add(p.Email);
            if (p.Phone != null) contacts.Add(p.Phone);
            if (p.Location != null) contacts.Add(p.Location);
            if (p.LinkedInUrl != null) contacts.Add(p.LinkedInUrl);
            if (p.GitHubUrl != null) contacts.Add(p.GitHubUrl);
            if (p.WebsiteUrl != null) contacts.Add(p.WebsiteUrl);
            if (contacts.Count > 0)
                col.Item().Text(string.Join("  |  ", contacts)).FontSize(8).FontColor(Colors.Grey.Medium);

            col.Item().PaddingTop(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
        });
    }

    private static void ComposeContent(IContainer container, ResumeDocument resume, ResumeTemplate template)
    {
        container.PaddingTop(8).Column(col =>
        {
            // Summary
            if (!string.IsNullOrWhiteSpace(resume.Personal.Summary))
            {
                col.Item().Element(c => SectionHeading(c, "Summary", template));
                col.Item().Text(resume.Personal.Summary).FontSize(9).LineHeight(1.4f);
                col.Item().PaddingBottom(8);
            }

            // Skills near the top makes the human-facing pages easy to scan and
            // prevents a nearly empty skills-only second page.
            if (resume.Skills.Count > 0)
            {
                col.Item().Element(c => SectionHeading(c, "Skills", template));
                foreach (var g in resume.Skills.GroupBy(s => s.Category ?? "General"))
                {
                    col.Item().Row(row =>
                    {
                        row.ConstantItem(130).Text(g.Key).Bold().FontSize(8).FontColor($"#{template.AccentHex}");
                        row.RelativeItem().Text(string.Join(", ", g.Select(s => s.Name))).FontSize(8);
                    });
                }
                col.Item().PaddingBottom(6);
            }

            // Experience
            if (resume.Experience.Count > 0)
            {
                col.Item().Element(c => SectionHeading(c, "Experience", template));
                for (var experienceIndex = 0; experienceIndex < resume.Experience.Count; experienceIndex++)
                {
                    if (resume.Experience.Count >= 10 && experienceIndex == 7)
                        col.Item().PageBreak();
                    var exp = resume.Experience[experienceIndex];
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text(t =>
                        {
                            t.Span(exp.Title ?? "").Bold();
                            t.Span(" — ");
                            t.Span(exp.Company ?? "").FontColor($"#{template.AccentHex}");
                        });
                        var dates = FormatDates(exp.StartDate, exp.EndDate, exp.IsCurrent);
                        if (!string.IsNullOrEmpty(dates))
                            row.ConstantItem(120).AlignRight().Text(dates).FontSize(8).FontColor(Colors.Grey.Medium);
                    });

                    if (!string.IsNullOrEmpty(exp.Location))
                        col.Item().Text(exp.Location).FontSize(8).FontColor(Colors.Grey.Medium);

                    if (exp.Technologies.Count > 0)
                        col.Item().Text(string.Join(", ", exp.Technologies))
                            .FontSize(8).Italic().FontColor($"#{template.AccentHex}");

                    foreach (var a in exp.Achievements)
                        col.Item().PaddingLeft(12).Row(row =>
                        {
                            row.ConstantItem(8).Text("•").FontSize(8);
                            row.RelativeItem().Text(a).FontSize(9).LineHeight(1.3f);
                        });

                    col.Item().PaddingBottom(6);
                }
            }

            // Education
            if (resume.Education.Count > 0)
            {
                col.Item().Element(c => SectionHeading(c, "Education", template));
                foreach (var edu in resume.Education)
                {
                    var parts = new[] { edu.Degree, edu.FieldOfStudy, edu.Institution }
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text(string.Join(" — ", parts)).Bold().FontSize(9);
                        var dates = FormatDates(edu.StartDate, edu.EndDate, false);
                        if (!string.IsNullOrEmpty(dates))
                            row.ConstantItem(100).AlignRight().Text(dates).FontSize(8).FontColor(Colors.Grey.Medium);
                    });
                    col.Item().PaddingBottom(4);
                }
            }

            // Certifications
            if (resume.Certifications.Count > 0)
            {
                col.Item().Element(c => SectionHeading(c, "Certifications", template));
                foreach (var c in resume.Certifications)
                    col.Item().Text($"• {c.Name} — {c.Issuer}" +
                        (c.IssuedDate.HasValue ? $" ({c.IssuedDate.Value.Year})" : "")).FontSize(9);
                col.Item().PaddingBottom(6);
            }

            // Projects
            if (resume.Projects.Count > 0)
            {
                col.Item().Element(c => SectionHeading(c, "Projects", template));
                foreach (var proj in resume.Projects)
                {
                    col.Item().Text(proj.Name).Bold().FontSize(9);
                    if (!string.IsNullOrWhiteSpace(proj.Description))
                        col.Item().Text(proj.Description).FontSize(8);
                    if (proj.Technologies.Count > 0)
                        col.Item().Text(string.Join(", ", proj.Technologies))
                            .FontSize(8).Italic().FontColor($"#{template.AccentHex}");
                    col.Item().PaddingBottom(4);
                }
            }

            AppendMachineArea(col, resume, template);
        });
    }

    private static void SectionHeading(IContainer container, string title, ResumeTemplate template)
    {
        container.PaddingBottom(4).Column(col =>
        {
            col.Item().Text(title).FontSize(template.SectionFontSize).Bold().FontColor($"#{template.AccentHex}");
            col.Item().LineHorizontal(0.5f).LineColor($"#{template.AccentHex}");
        });
    }

    private static void AppendMachineArea(ColumnDescriptor col, ResumeDocument resume, ResumeTemplate template)
    {
        var yaml = ExportArtifact.JobMlYaml(resume);
        if (string.IsNullOrWhiteSpace(yaml)) return;

        col.Item().PageBreak();
        col.Item().Text("MACHINE AREA").FontSize(template.SectionFontSize).Bold().FontColor($"#{template.AccentHex}");
        col.Item().Text("Machine-readable evidence and claim links. This is not replacement resume prose.")
            .FontSize(8).FontColor(Colors.Grey.Darken1);
        col.Item().Hyperlink(ExportArtifact.MachineArticleUrl).Text("What is this?")
            .FontSize(8).FontColor($"#{template.AccentHex}").Underline();
        col.Item().PaddingTop(8).Text(yaml).FontFamily("Lato").FontSize(6.5f).LineHeight(1.15f)
            .FontColor(Colors.Grey.Darken2);
    }

    private static string FormatDates(DateOnly? start, DateOnly? end, bool isCurrent)
    {
        var s = start?.ToString("MMM yyyy") ?? "";
        var e = isCurrent ? "Present" : end?.ToString("MMM yyyy") ?? "";
        return s != "" || e != "" ? $"{s} – {e}" : "";
    }
}
