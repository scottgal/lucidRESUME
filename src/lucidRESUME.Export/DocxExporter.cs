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

            AppendContactLines(body, p, template);
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
                {
                    var skills = g.Select(s => s.Name).ToList();
                    var paragraph = CreateSkillGroup(g.Key, skills, template);
                    AppendCitationMarkers(paragraph,
                        ExportArtifact.CitationNumbers($"{g.Key}: {string.Join(", ", skills)}", compact));
                    body.Append(paragraph);
                }
            }

            // Role-relevant engineering work is selected before employment by the
            // compiler, so preserve that emphasis in conventional exports too.
            if (resume.Projects.Count > 0)
            {
                body.Append(CreateParagraph("Selected Engineering", "Heading2"));
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
                        body.Append(CreateParagraph(string.Join(", ", proj.Technologies), fontSize: 18,
                            color: template.AccentHex, fontFamily: template.FontFamily));
                }
            }

            // --- Experience ---
            if (resume.Experience.Count > 0)
            {
                var secondPageExperienceIndex = ExportLayoutPolicy.SecondPageExperienceIndex(resume);
                if (secondPageExperienceIndex == 0)
                    body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                body.Append(CreateParagraph("Experience", "Heading2"));
                for (var experienceIndex = 0; experienceIndex < resume.Experience.Count; experienceIndex++)
                {
                    if (experienceIndex == secondPageExperienceIndex && experienceIndex > 0)
                        body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                    var exp = resume.Experience[experienceIndex];
                    if (exp.IsCompact && (experienceIndex == 0 || !resume.Experience[experienceIndex - 1].IsCompact))
                        body.Append(CreateParagraph("Additional consulting, contract and earlier experience",
                            bold: true));
                    var roleParagraphs = new List<Paragraph>();
                    var dates = FormatDateRange(exp.StartDate, exp.EndDate, exp.IsCurrent);
                    var header = CreateExperienceHeader(exp, dates, template);
                    if (exp.IsCompact)
                        AppendCitationMarkers(header, ExportArtifact.CitationNumbers(
                            exp.Achievements.FirstOrDefault() ?? string.Empty, compact));
                    roleParagraphs.Add(header);
                    if (exp.Technologies.Count > 0)
                        roleParagraphs.Add(CreateParagraph($"Technologies: {string.Join(", ", exp.Technologies)}", fontSize: 18, color: template.AccentHex, italic: true, fontFamily: template.FontFamily));
                    foreach (var a in exp.IsCompact ? [] : exp.Achievements)
                    {
                        var paragraph = CreateBullet(a);
                        AppendCitationMarkers(paragraph, ExportArtifact.CitationNumbers(a, compact));
                        roleParagraphs.Add(paragraph);
                    }
                    for (var paragraphIndex = 0; paragraphIndex < roleParagraphs.Count; paragraphIndex++)
                    {
                        KeepTogether(roleParagraphs[paragraphIndex], paragraphIndex < roleParagraphs.Count - 1);
                        body.Append(roleParagraphs[paragraphIndex]);
                    }
                    body.Append(CreateParagraph("", fontSize: exp.IsCompact ? 8 : 22)); // spacing
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
                    var paragraph = CreateParagraph(string.Join(" - ", title), bold: true);
                    AppendCitationMarkers(paragraph,
                        ExportArtifact.EducationCitationNumbers(edu, compact));
                    body.Append(paragraph);
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
                    body.Append(CreateBullet($"{c.Name} - {c.Issuer}" + (c.IssuedDate.HasValue ? $" ({c.IssuedDate.Value.Year})" : "")));
            }

            if (resume.Publications.Count > 0)
            {
                body.Append(CreateParagraph("Selected Recent Publications", "Heading2"));
                var paragraph = new Paragraph();
                for (var publicationIndex = 0; publicationIndex < resume.Publications.Count; publicationIndex++)
                {
                    var publication = resume.Publications[publicationIndex];
                    if (publicationIndex > 0)
                        paragraph.Append(new Run(new RunProperties(new FontSize { Val = "20" }),
                            new Text(" · ") { Space = SpaceProcessingModeValues.Preserve }));
                    if (Uri.TryCreate(publication.Url, UriKind.Absolute, out var uri))
                    {
                        var relationship = mainPart.AddHyperlinkRelationship(uri, true);
                        paragraph.Append(new Hyperlink(
                            new Run(new RunProperties(
                                    new Color { Val = template.AccentHex },
                                    new Underline { Val = UnderlineValues.Single },
                                    new FontSize { Val = "20" }),
                                new Text(publication.Name)))
                        { Id = relationship.Id });
                    }
                    else
                    {
                        paragraph.Append(new Run(new RunProperties(new FontSize { Val = "20" }),
                            new Text(publication.Name) { Space = SpaceProcessingModeValues.Preserve }));
                    }
                }
                body.Append(paragraph);
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
            { Type = StyleValues.Paragraph, StyleId = "Heading2" },
            new Style(
                new StyleName { Val = "Heading 3" },
                new StyleParagraphProperties(
                    new KeepNext(),
                    new SpacingBetweenLines { Before = "80", After = "20" }),
                new StyleRunProperties(
                    new RunFonts { Ascii = template.FontFamily, HighAnsi = template.FontFamily },
                    new Bold(),
                    new Color { Val = template.AccentHex },
                    new FontSize { Val = "24" })
            )
            { Type = StyleValues.Paragraph, StyleId = "Heading3" },
            new Style(
                new StyleName { Val = "List Bullet" },
                new StyleParagraphProperties(
                    new Indentation { Left = "360", Hanging = "180" })
            )
            { Type = StyleValues.Paragraph, StyleId = "ListBullet" }
        );

        AddNumbering(mainPart);
    }

    private static void AddNumbering(MainDocumentPart mainPart)
    {
        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
        numberingPart.Numbering = new Numbering(
            new AbstractNum(
                new MultiLevelType { Val = MultiLevelValues.SingleLevel },
                new Level(
                    new NumberingFormat { Val = NumberFormatValues.Bullet },
                    new LevelText { Val = "•" },
                    new LevelJustification { Val = LevelJustificationValues.Left },
                    new PreviousParagraphProperties(
                        new Indentation { Left = "360", Hanging = "180" }),
                    new NumberingSymbolRunProperties(
                        new RunFonts { Ascii = "Arial", HighAnsi = "Arial" }))
                { LevelIndex = 0 })
            { AbstractNumberId = 1 },
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
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

    private static void AppendContactLines(Body body, PersonalInfo personal, ResumeTemplate template)
    {
        if (!string.IsNullOrWhiteSpace(personal.Email))
            body.Append(CreateParagraph(personal.Email, fontSize: 18, color: "555555",
                fontFamily: template.FontFamily));

        if (!string.IsNullOrWhiteSpace(personal.Phone))
            body.Append(CreateParagraph(string.IsNullOrWhiteSpace(personal.ContactPreference)
                ? personal.Phone
                    : $"{personal.Phone} - {personal.ContactPreference} -", fontSize: 18, color: "555555",
                fontFamily: template.FontFamily));
        if (!string.IsNullOrWhiteSpace(personal.Location))
            body.Append(CreateParagraph(personal.Location, fontSize: 18, color: "555555",
                fontFamily: template.FontFamily));

        var profiles = string.Join("  |  ", new[] { personal.LinkedInUrl, personal.GitHubUrl, personal.WebsiteUrl }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(profiles))
            body.Append(CreateParagraph(profiles, fontSize: 18, color: "555555", fontFamily: template.FontFamily));
    }

    private static Paragraph CreateExperienceHeader(WorkExperience experience, string dates, ResumeTemplate template)
    {
        var paragraph = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "Heading3" }));

        void Add(string value, bool bold = false, string? color = null)
        {
            var properties = new RunProperties(
                new RunFonts { Ascii = template.FontFamily, HighAnsi = template.FontFamily });
            if (bold) properties.Append(new Bold());
            if (color is not null) properties.Append(new Color { Val = color });
            properties.Append(new FontSize { Val = "22" });
            paragraph.Append(new Run(properties,
                new Text(value) { Space = SpaceProcessingModeValues.Preserve }));
        }

        if (!string.IsNullOrWhiteSpace(experience.Title)) Add(experience.Title, bold: true);
        if (!string.IsNullOrWhiteSpace(experience.Title) && !string.IsNullOrWhiteSpace(experience.Company)) Add(" | ");
        if (!string.IsNullOrWhiteSpace(experience.Company)) Add(experience.Company, bold: true, color: template.AccentHex);
        if (!string.IsNullOrWhiteSpace(experience.Location)) Add($" | {experience.Location}", color: "666666");
        if (!string.IsNullOrWhiteSpace(dates)) Add($" | {dates}", color: "666666");
        return paragraph;
    }

    private static Paragraph CreateBullet(string text)
    {
        var para = new Paragraph(
            new ParagraphProperties(
                new ParagraphStyleId { Val = "ListBullet" },
                new NumberingProperties(
                    new NumberingLevelReference { Val = 0 },
                    new NumberingId { Val = 1 })),
            new Run(new RunProperties(new FontSize { Val = "20" }),
                new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return para;
    }

    private static void KeepTogether(Paragraph paragraph, bool keepWithNext)
    {
        var properties = paragraph.ParagraphProperties;
        if (properties is null)
        {
            properties = new ParagraphProperties();
            paragraph.PrependChild(properties);
        }
        var style = properties.GetFirstChild<ParagraphStyleId>();
        if (keepWithNext)
        {
            var keepNext = new KeepNext();
            if (style is null) properties.PrependChild(keepNext);
            else properties.InsertAfter(keepNext, style);
            properties.InsertAfter(new KeepLines(), keepNext);
        }
        else if (style is null)
        {
            properties.PrependChild(new KeepLines());
        }
        else
        {
            properties.InsertAfter(new KeepLines(), style);
        }
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
        for (var index = 0; index < numbers.Count; index++)
        {
            var number = numbers[index];
            paragraph.Append(new Hyperlink(
                new Run(
                    new RunProperties(
                        new Color { Val = "0563C1" },
                        new Underline { Val = UnderlineValues.Single }),
                    new Text(index == 0 ? $"\u00A0[{number}]" : $",\u00A0[{number}]")
                    { Space = SpaceProcessingModeValues.Preserve }))
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

        if (Uri.TryCreate(compact.FullJobMl, UriKind.Absolute, out var completeLedger))
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
        return s != "" || e != "" ? $"{s} - {e}" : "";
    }
}
