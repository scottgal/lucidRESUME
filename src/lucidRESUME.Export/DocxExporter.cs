using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Export;

/// <summary>
/// Exports a ResumeDocument to a professionally formatted DOCX file
/// using DocumentFormat.OpenXml. No external tools required.
/// </summary>
public sealed class DocxExporter : IResumeExporter
{
    public ExportFormat Format => ExportFormat.Docx;

    public Task<byte[]> ExportAsync(ResumeDocument resume, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            AddStyles(mainPart);

            var body = mainPart.Document.Body!;

            // --- Personal Info ---
            var p = resume.Personal;
            if (p.FullName != null)
                body.Append(CreateParagraph(p.FullName, "Heading1"));

            var contacts = new List<string>();
            if (p.Email != null) contacts.Add(p.Email);
            if (p.Phone != null) contacts.Add(p.Phone);
            if (p.Location != null) contacts.Add(p.Location);
            if (p.LinkedInUrl != null) contacts.Add(p.LinkedInUrl);
            if (p.GitHubUrl != null) contacts.Add(p.GitHubUrl);
            if (contacts.Count > 0)
                body.Append(CreateParagraph(string.Join("  |  ", contacts), fontSize: 18, color: "666666"));

            body.Append(CreateHorizontalRule());

            // --- Summary ---
            if (!string.IsNullOrWhiteSpace(p.Summary))
            {
                body.Append(CreateParagraph("Summary", "Heading2"));
                body.Append(CreateParagraph(p.Summary));
            }

            // --- Experience ---
            if (resume.Experience.Count > 0)
            {
                body.Append(CreateParagraph("Experience", "Heading2"));
                foreach (var exp in resume.Experience)
                {
                    body.Append(CreateExperienceHeader(exp));
                    var dates = FormatDateRange(exp.StartDate, exp.EndDate, exp.IsCurrent);
                    if (!string.IsNullOrEmpty(dates))
                        body.Append(CreateParagraph(dates, fontSize: 18, color: "888888", italic: true));
                    if (!string.IsNullOrEmpty(exp.Location))
                        body.Append(CreateParagraph(exp.Location, fontSize: 18, color: "888888"));
                    if (exp.Technologies.Count > 0)
                        body.Append(CreateParagraph($"Technologies: {string.Join(", ", exp.Technologies)}", fontSize: 18, color: "2E74B5", italic: true));
                    foreach (var a in exp.Achievements)
                        body.Append(CreateBullet(a));
                    body.Append(CreateParagraph("")); // spacing
                }
            }

            // --- Education ---
            if (resume.Education.Count > 0)
            {
                body.Append(CreateParagraph("Education", "Heading2"));
                foreach (var edu in resume.Education)
                {
                    var title = new[] { edu.Degree, edu.FieldOfStudy, edu.Institution }
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    body.Append(CreateParagraph(string.Join(" — ", title), bold: true));
                    var dates = FormatDateRange(edu.StartDate, edu.EndDate, false);
                    if (!string.IsNullOrEmpty(dates))
                        body.Append(CreateParagraph(dates, fontSize: 18, color: "888888", italic: true));
                }
            }

            // --- Skills ---
            if (resume.Skills.Count > 0)
            {
                body.Append(CreateParagraph("Skills", "Heading2"));
                foreach (var g in resume.Skills.GroupBy(s => s.Category ?? "General"))
                {
                    body.Append(CreateSkillGroup(g.Key, g.Select(s => s.Name).ToList()));
                }
            }

            // --- Certifications ---
            if (resume.Certifications.Count > 0)
            {
                body.Append(CreateParagraph("Certifications", "Heading2"));
                foreach (var c in resume.Certifications)
                    body.Append(CreateBullet($"{c.Name} — {c.Issuer}" + (c.IssuedDate.HasValue ? $" ({c.IssuedDate.Value.Year})" : "")));
            }

            // --- Projects ---
            if (resume.Projects.Count > 0)
            {
                body.Append(CreateParagraph("Projects", "Heading2"));
                foreach (var proj in resume.Projects)
                {
                    body.Append(CreateParagraph(proj.Name, bold: true));
                    if (!string.IsNullOrWhiteSpace(proj.Description))
                        body.Append(CreateParagraph(proj.Description, fontSize: 20));
                    if (proj.Technologies.Count > 0)
                        body.Append(CreateParagraph(string.Join(", ", proj.Technologies), fontSize: 18, color: "2E74B5"));
                }
            }

            // Set page margins
            body.Append(new SectionProperties(
                new PageMargin
                {
                    Top = 720, Right = 720u, Bottom = 720, Left = 720u,
                    Header = 360u, Footer = 360u
                }));
        }

        return Task.FromResult(ms.ToArray());
    }

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new Style(
                new StyleName { Val = "Heading1" },
                new StyleRunProperties(
                    new Bold(),
                    new FontSize { Val = "48" }, // 24pt
                    new Color { Val = "2E74B5" }
                )
            ) { Type = StyleValues.Paragraph, StyleId = "Heading1" },
            new Style(
                new StyleName { Val = "Heading2" },
                new StyleRunProperties(
                    new Bold(),
                    new FontSize { Val = "28" }, // 14pt
                    new Color { Val = "2E74B5" }
                ),
                new StyleParagraphProperties(
                    new SpacingBetweenLines { Before = "200", After = "60" }
                )
            ) { Type = StyleValues.Paragraph, StyleId = "Heading2" }
        );
    }

    private static Paragraph CreateParagraph(string text, string? styleId = null, int fontSize = 22,
        string? color = null, bool bold = false, bool italic = false)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        var rp = new RunProperties();
        if (fontSize != 22) rp.Append(new FontSize { Val = fontSize.ToString() });
        if (color != null) rp.Append(new Color { Val = color });
        if (bold) rp.Append(new Bold());
        if (italic) rp.Append(new Italic());
        if (rp.HasChildren) run.PrependChild(rp);

        var para = new Paragraph(run);
        if (styleId != null)
            para.PrependChild(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        return para;
    }

    private static Paragraph CreateExperienceHeader(WorkExperience exp)
    {
        var para = new Paragraph();
        var titleRun = new Run(new RunProperties(new Bold(), new FontSize { Val = "24" }),
            new Text(exp.Title ?? "") { Space = SpaceProcessingModeValues.Preserve });
        var sepRun = new Run(new RunProperties(new FontSize { Val = "24" }),
            new Text(" — ") { Space = SpaceProcessingModeValues.Preserve });
        var compRun = new Run(new RunProperties(new FontSize { Val = "24" }, new Color { Val = "2E74B5" }),
            new Text(exp.Company ?? "") { Space = SpaceProcessingModeValues.Preserve });
        para.Append(titleRun, sepRun, compRun);
        return para;
    }

    private static Paragraph CreateBullet(string text)
    {
        var para = new Paragraph(
            new ParagraphProperties(
                new Indentation { Left = "360", Hanging = "180" }),
            new Run(new RunProperties(new FontSize { Val = "20" }),
                new Text($"•  {text}") { Space = SpaceProcessingModeValues.Preserve }));
        return para;
    }

    private static Paragraph CreateSkillGroup(string category, List<string> skills)
    {
        var para = new Paragraph();
        para.Append(new Run(new RunProperties(new Bold(), new FontSize { Val = "20" }, new Color { Val = "2E74B5" }),
            new Text($"{category}: ") { Space = SpaceProcessingModeValues.Preserve }));
        para.Append(new Run(new RunProperties(new FontSize { Val = "20" }),
            new Text(string.Join(", ", skills)) { Space = SpaceProcessingModeValues.Preserve }));
        return para;
    }

    private static Paragraph CreateHorizontalRule()
    {
        return new Paragraph(
            new ParagraphProperties(
                new ParagraphBorders(
                    new BottomBorder { Val = BorderValues.Single, Size = 6, Color = "CCCCCC", Space = 1u })),
            new Run(new Text("")));
    }

    private static string FormatDateRange(DateOnly? start, DateOnly? end, bool isCurrent)
    {
        var s = start?.ToString("MMM yyyy") ?? "";
        var e = isCurrent ? "Present" : end?.ToString("MMM yyyy") ?? "";
        return s != "" || e != "" ? $"{s} – {e}" : "";
    }
}
