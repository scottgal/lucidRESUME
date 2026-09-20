using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;

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
        var template = ExportArtifact.Template(resume);
        var compact = ExportArtifact.CompactJobMl(resume);
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            // Include standard document metadata so office suites can identify the
            // producer and display a useful title without inspecting resume content.
            doc.PackageProperties.Creator = "lucidRESUME";
            doc.PackageProperties.Title = string.IsNullOrWhiteSpace(resume.TargetRole)
                ? "Resume"
                : $"Resume for {resume.TargetRole}";
            var extendedProperties = doc.AddExtendedFilePropertiesPart();
            extendedProperties.Properties = new Properties(
                new Application("lucidRESUME"),
                new ApplicationVersion("2.0"));

            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            AddStyles(mainPart, template);

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
            if (p.WebsiteUrl != null) contacts.Add(p.WebsiteUrl);
            if (contacts.Count > 0)
                body.Append(CreateParagraph(string.Join("  |  ", contacts), fontSize: 18, color: "555555", fontFamily: template.FontFamily));
            if (!string.IsNullOrWhiteSpace(resume.TargetRole))
                body.Append(CreateParagraph($"Target role: {resume.TargetRole}", fontSize: 20,
                    color: template.AccentHex, bold: true, fontFamily: template.FontFamily));

            body.Append(CreateHorizontalRule());

            // --- Summary ---
            if (!string.IsNullOrWhiteSpace(p.Summary))
            {
                body.Append(CreateParagraph("Summary", "Heading2"));
                var paragraph = CreateParagraph(p.Summary);
                AppendCitationMarkers(paragraph, ExportArtifact.CitationNumbers(p.Summary, compact));
                body.Append(paragraph);
            }

            // Put the searchable technical inventory before the chronology.
            if (resume.Skills.Count > 0)
            {
                body.Append(CreateParagraph("Skills", "Heading2"));
                foreach (var g in resume.Skills.GroupBy(s => s.Category ?? "General"))
                    body.Append(CreateSkillGroup(g.Key, g.Select(s => s.Name).ToList(), template));
            }

            // --- Experience ---
            if (resume.Experience.Count > 0)
            {
                body.Append(CreateParagraph("Experience", "Heading2"));
                for (var experienceIndex = 0; experienceIndex < resume.Experience.Count; experienceIndex++)
                {
                    if (resume.Experience.Count >= 10 && experienceIndex == 7)
                        body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                    var exp = resume.Experience[experienceIndex];
                    body.Append(CreateExperienceHeader(exp, template));
                    var dates = FormatDateRange(exp.StartDate, exp.EndDate, exp.IsCurrent);
                    if (!string.IsNullOrEmpty(dates))
                        body.Append(CreateParagraph(dates, fontSize: 18, color: "888888", italic: true));
                    if (!string.IsNullOrEmpty(exp.Location))
                        body.Append(CreateParagraph(exp.Location, fontSize: 18, color: "888888"));
                    if (exp.Technologies.Count > 0)
                        body.Append(CreateParagraph($"Technologies: {string.Join(", ", exp.Technologies)}", fontSize: 18, color: template.AccentHex, italic: true, fontFamily: template.FontFamily));
                    foreach (var a in exp.Achievements)
                    {
                        var paragraph = CreateBullet(a);
                        AppendCitationMarkers(paragraph, ExportArtifact.CitationNumbers(a, compact));
                        body.Append(paragraph);
                    }
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
                    {
                        var paragraph = CreateParagraph(proj.Description, fontSize: 20);
                        AppendCitationMarkers(paragraph, ExportArtifact.CitationNumbers(proj.Description, compact));
                        body.Append(paragraph);
                    }
                    if (proj.Technologies.Count > 0)
                        body.Append(CreateParagraph(string.Join(", ", proj.Technologies), fontSize: 18, color: template.AccentHex, fontFamily: template.FontFamily));
                }
            }

            AppendReferences(mainPart, body, compact, template);

            // Set page margins
            var margin = (int)Math.Round(template.PageMarginPoints * 20);
            body.Append(new SectionProperties(
                new PageMargin
                {
                    Top = margin,
                    Right = (uint)margin,
                    Bottom = margin,
                    Left = (uint)margin,
                    Header = 360u,
                    Footer = 360u
                }));
        }

        return Task.FromResult(ms.ToArray());
    }

    private static void AddStyles(MainDocumentPart mainPart, ResumeTemplate template)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new Style(
                new StyleName { Val = "Heading1" },
                new StyleRunProperties(
                    new RunFonts { Ascii = template.FontFamily, HighAnsi = template.FontFamily },
                    new Bold(),
                    new Color { Val = template.AccentHex },
                    new FontSize { Val = "48" } // 24pt
                )
            )
            { Type = StyleValues.Paragraph, StyleId = "Heading1" },
            new Style(
                new StyleName { Val = "Heading2" },
                new StyleParagraphProperties(
                    new SpacingBetweenLines { Before = "200", After = "60" }
                ),
                new StyleRunProperties(
                    new RunFonts { Ascii = template.FontFamily, HighAnsi = template.FontFamily },
                    new Bold(),
                    new Color { Val = template.AccentHex },
                    new FontSize { Val = ((int)Math.Round(template.SectionFontSize * 2)).ToString() }
                )
            )
            { Type = StyleValues.Paragraph, StyleId = "Heading2" }
        );
    }

    private static Paragraph CreateParagraph(string text, string? styleId = null, int fontSize = 22,
        string? color = null, bool bold = false, bool italic = false, string? fontFamily = null)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        var rp = new RunProperties();
        if (fontFamily != null) rp.Append(new RunFonts { Ascii = fontFamily, HighAnsi = fontFamily });
        if (bold) rp.Append(new Bold());
        if (italic) rp.Append(new Italic());
        if (color != null) rp.Append(new Color { Val = color });
        if (fontSize != 22) rp.Append(new FontSize { Val = fontSize.ToString() });
        if (rp.HasChildren) run.PrependChild(rp);

        var para = new Paragraph(run);
        if (styleId != null)
            para.PrependChild(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        return para;
    }

    private static Paragraph CreateExperienceHeader(WorkExperience exp, ResumeTemplate template)
    {
        var para = new Paragraph();
        var titleRun = new Run(new RunProperties(new Bold(), new FontSize { Val = "24" }),
            new Text(exp.Title ?? "") { Space = SpaceProcessingModeValues.Preserve });
        var sepRun = new Run(new RunProperties(new FontSize { Val = "24" }),
            new Text(" — ") { Space = SpaceProcessingModeValues.Preserve });
        var compRun = new Run(new RunProperties(new Color { Val = template.AccentHex }, new FontSize { Val = "24" }),
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

    private static Paragraph CreateSkillGroup(string category, List<string> skills, ResumeTemplate template)
    {
        var para = new Paragraph();
        para.Append(new Run(new RunProperties(new Bold(), new Color { Val = template.AccentHex }, new FontSize { Val = "20" }),
            new Text($"{category}: ") { Space = SpaceProcessingModeValues.Preserve }));
        para.Append(new Run(new RunProperties(new FontSize { Val = "20" }),
            new Text(string.Join(", ", skills)) { Space = SpaceProcessingModeValues.Preserve }));
        return para;
    }

    private static void AppendCitationMarkers(Paragraph paragraph, IReadOnlyList<int> numbers)
    {
        if (numbers.Count == 0) return;
        paragraph.Append(new Run(new Text(" ")));
        for (var index = 0; index < numbers.Count; index++)
        {
            if (index > 0) paragraph.Append(new Run(new Text(", ")));
            var number = numbers[index];
            paragraph.Append(new Hyperlink(
                new Run(
                    new RunProperties(
                        new Color { Val = "0563C1" },
                        new Underline { Val = UnderlineValues.Single }),
                    new Text($"[{number}]")))
            {
                Anchor = $"ref-{number}",
                History = OnOffValue.FromBoolean(true)
            });
        }
    }

    private static void AppendReferences(MainDocumentPart mainPart, Body body,
        CJobMlProjection? compact, ResumeTemplate template)
    {
        if (compact is null || compact.References.Count == 0) return;

        body.Append(CreateParagraph("References", "Heading2"));
        body.Append(CreateParagraph(CJobMlProjector.SemanticPreamble,
            fontSize: 16, color: "666666", fontFamily: template.FontFamily));

        if (Uri.TryCreate(compact.CompleteLedger, UriKind.Absolute, out var completeLedger))
        {
            var relationship = mainPart.AddHyperlinkRelationship(completeLedger, true);
            var paragraph = CreateParagraph("Full JobML: ", fontSize: 16, color: "666666", fontFamily: template.FontFamily);
            paragraph.Append(new Hyperlink(
                new Run(new RunProperties(new Color { Val = template.AccentHex }, new Underline { Val = UnderlineValues.Single }),
                    new Text(completeLedger.ToString())))
            { Id = relationship.Id });
            body.Append(paragraph);
        }

        foreach (var reference in compact.References)
        {
            var paragraph = new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { After = "80" },
                    new Indentation { Left = "360", Hanging = "360" }));
            var bookmarkId = (10_000 + reference.Number).ToString();
            paragraph.Append(new BookmarkStart { Id = bookmarkId, Name = $"ref-{reference.Number}" });

            var uriToken = string.IsNullOrWhiteSpace(reference.Evidence.Uri)
                ? null
                : $"<{reference.Evidence.Uri}>";
            var text = reference.PlainText;
            if (uriToken is not null)
                text = text.Replace(uriToken + ".", "", StringComparison.Ordinal).TrimEnd();
            paragraph.Append(new Run(new RunProperties(new FontSize { Val = "16" }),
                new Text(text + (uriToken is null ? "" : " ")) { Space = SpaceProcessingModeValues.Preserve }));

            if (Uri.TryCreate(reference.Evidence.Uri, UriKind.Absolute, out var uri))
            {
                var relationship = mainPart.AddHyperlinkRelationship(uri, true);
                paragraph.Append(new Hyperlink(
                    new Run(new RunProperties(new Color { Val = template.AccentHex },
                            new FontSize { Val = "16" }, new Underline { Val = UnderlineValues.Single }),
                        new Text(uri.ToString())))
                { Id = relationship.Id });
            }
            paragraph.Append(new BookmarkEnd { Id = bookmarkId });
            body.Append(paragraph);
        }
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
