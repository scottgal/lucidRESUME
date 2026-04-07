/// <summary>
/// Generates sample DOCX resume files with different template styles for use with
/// lucidresume train --folder ./samples
/// </summary>

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

var outputDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples");
Directory.CreateDirectory(outputDir);

GenerateModernTemplate(Path.Combine(outputDir, "template-modern.docx"));
GenerateClassicTemplate(Path.Combine(outputDir, "template-classic.docx"));
GenerateMinimalTemplate(Path.Combine(outputDir, "template-minimal.docx"));
GenerateFunctionalTemplate(Path.Combine(outputDir, "template-functional.docx"));

Console.WriteLine($"Generated 4 sample DOCX templates in: {Path.GetFullPath(outputDir)}");

// ── Template generators ──────────────────────────────────────────────────────

static void GenerateModernTemplate(string path)
{
    using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
    var mainPart = doc.AddMainDocumentPart();
    mainPart.Document = new Document(new Body());
    AddStyles(mainPart, "Calibri", 22, CreateModernStyles());
    SetMargins(mainPart, top: 720, right: 720, bottom: 720, left: 720);

    var body = mainPart.Document.Body!;
    body.AppendChild(HeadingPara("Jane Smith", "Heading1"));
    body.AppendChild(TextPara("jane.smith@email.com | +44 7700 900000 | London, UK | linkedin.com/in/janesmith"));
    body.AppendChild(SectionPara("PROFESSIONAL SUMMARY", "Heading2"));
    body.AppendChild(TextPara("Results-driven software engineer with 8 years building scalable distributed systems."));
    body.AppendChild(SectionPara("EXPERIENCE", "Heading2"));
    body.AppendChild(HeadingPara("Senior Software Engineer - TechCorp, London (2020–Present)", "Heading3"));
    body.AppendChild(BulletPara("Led migration of monolith to microservices, reducing deployment time by 60%"));
    body.AppendChild(BulletPara("Mentored team of 5 engineers across 3 time zones"));
    body.AppendChild(HeadingPara("Software Engineer - StartupXYZ, London (2018–2020)", "Heading3"));
    body.AppendChild(BulletPara("Built real-time analytics dashboard processing 1M events/day"));
    body.AppendChild(SectionPara("EDUCATION", "Heading2"));
    body.AppendChild(TextPara("BSc Computer Science - University of Edinburgh, 2018"));
    body.AppendChild(SectionPara("SKILLS", "Heading2"));
    body.AppendChild(TextPara("C# · .NET · Azure · Kubernetes · PostgreSQL · React · TypeScript"));

    mainPart.Document.Save();
}

static void GenerateClassicTemplate(string path)
{
    using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
    var mainPart = doc.AddMainDocumentPart();
    mainPart.Document = new Document(new Body());
    AddStyles(mainPart, "Times New Roman", 24, CreateClassicStyles());
    SetMargins(mainPart, top: 1440, right: 1440, bottom: 1440, left: 1440);

    var body = mainPart.Document.Body!;
    body.AppendChild(HeadingPara("John A. Williams", "Heading1"));
    body.AppendChild(TextPara("john.williams@email.com | (555) 123-4567 | New York, NY"));
    body.AppendChild(SectionPara("Objective", "Heading2"));
    body.AppendChild(TextPara("To leverage 10 years of experience in financial technology to deliver robust trading systems."));
    body.AppendChild(SectionPara("Professional Experience", "Heading2"));
    body.AppendChild(HeadingPara("Lead Developer, FinanceCo Inc., New York (2019–Present)", "Heading3"));
    body.AppendChild(BulletPara("Designed FIX protocol integration handling $2B daily transaction volume"));
    body.AppendChild(HeadingPara("Senior Developer, TradeWorks Ltd, New York (2016–2019)", "Heading3"));
    body.AppendChild(BulletPara("Implemented low-latency order matching engine in C++"));
    body.AppendChild(SectionPara("Education", "Heading2"));
    body.AppendChild(TextPara("MBA Finance - Columbia Business School, 2014"));
    body.AppendChild(TextPara("BEng Electrical Engineering - MIT, 2011"));
    body.AppendChild(SectionPara("Skills", "Heading2"));
    body.AppendChild(TextPara("C++ | Java | Python | SQL | FIX Protocol | Bloomberg API"));

    mainPart.Document.Save();
}

static void GenerateMinimalTemplate(string path)
{
    using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
    var mainPart = doc.AddMainDocumentPart();
    mainPart.Document = new Document(new Body());
    AddStyles(mainPart, "Arial", 20, CreateMinimalStyles());
    SetMargins(mainPart, top: 900, right: 900, bottom: 900, left: 900);

    var body = mainPart.Document.Body!;
    body.AppendChild(HeadingPara("Alex Chen", "Title"));
    body.AppendChild(TextPara("alex.chen@email.com · github.com/alexchen · San Francisco, CA"));
    body.AppendChild(SectionPara("About", "Subtitle"));
    body.AppendChild(TextPara("Full-stack engineer specializing in developer tooling and platform engineering."));
    body.AppendChild(SectionPara("Work", "Subtitle"));
    body.AppendChild(TextPara("Platform Engineer · CloudBase Inc. · 2021–Present"));
    body.AppendChild(BulletPara("Maintained internal CI/CD platform serving 200+ engineers"));
    body.AppendChild(TextPara("Software Engineer · DevTools Co. · 2019–2021"));
    body.AppendChild(BulletPara("Shipped CLI toolchain used by 50,000 developers"));
    body.AppendChild(SectionPara("Education", "Subtitle"));
    body.AppendChild(TextPara("BS Computer Science · Stanford University · 2019"));
    body.AppendChild(SectionPara("Stack", "Subtitle"));
    body.AppendChild(TextPara("Go · Rust · TypeScript · Kubernetes · Terraform · PostgreSQL"));

    mainPart.Document.Save();
}

static void GenerateFunctionalTemplate(string path)
{
    using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
    var mainPart = doc.AddMainDocumentPart();
    mainPart.Document = new Document(new Body());
    AddStyles(mainPart, "Georgia", 22, CreateFunctionalStyles());
    SetMargins(mainPart, top: 1080, right: 1080, bottom: 1080, left: 1080);

    var body = mainPart.Document.Body!;
    body.AppendChild(HeadingPara("Maria Garcia", "Heading1"));
    body.AppendChild(TextPara("maria.garcia@email.com | Madrid, Spain | +34 600 000 000"));
    body.AppendChild(SectionPara("Core Competencies", "Heading2"));
    body.AppendChild(TextPara("Project Management · Agile/Scrum · Stakeholder Communication · Budget Planning"));
    body.AppendChild(SectionPara("Key Achievements", "Heading2"));
    body.AppendChild(BulletPara("Delivered €5M digital transformation project 3 months ahead of schedule"));
    body.AppendChild(BulletPara("Reduced operational costs by 30% through process automation initiative"));
    body.AppendChild(SectionPara("Career History", "Heading2"));
    body.AppendChild(HeadingPara("Product Director - GlobalTech SA, Madrid (2018–Present)", "Heading3"));
    body.AppendChild(HeadingPara("Product Manager - InnovateCo, Barcelona (2015–2018)", "Heading3"));
    body.AppendChild(SectionPara("Education", "Heading2"));
    body.AppendChild(TextPara("MBA - IE Business School, Madrid, 2015"));
    body.AppendChild(TextPara("BA Business Administration - Universidad Complutense, 2012"));

    mainPart.Document.Save();
}

// ── Helpers ──────────────────────────────────────────────────────────────────

static void AddStyles(MainDocumentPart mainPart, string fontName, int sizeHp, Styles styles)
{
    var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
    stylesPart.Styles = styles;
    stylesPart.Styles.Save();

    // Document defaults
    var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
    settingsPart.Settings = new Settings();
    settingsPart.Settings.Save();
}

static void SetMargins(MainDocumentPart mainPart, uint top, uint right, uint bottom, uint left)
{
    var body = mainPart.Document.Body!;
    var sectPr = body.AppendChild(new SectionProperties());
    sectPr.AppendChild(new PageMargin
    {
        Top = (int)top,
        Right = right,
        Bottom = (int)bottom,
        Left = left
    });
}

static Paragraph HeadingPara(string text, string styleId) =>
    new(
        new ParagraphProperties(new ParagraphStyleId { Val = styleId }),
        new Run(new Text(text)));

static Paragraph SectionPara(string text, string styleId) =>
    new(
        new ParagraphProperties(new ParagraphStyleId { Val = styleId }),
        new Run(new Text(text)));

static Paragraph TextPara(string text) =>
    new(new Run(new Text(text)));

static Paragraph BulletPara(string text) =>
    new(
        new ParagraphProperties(
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 1 })),
        new Run(new Text("• " + text)));

// ── Style factories ──────────────────────────────────────────────────────────

static Styles CreateModernStyles() =>
    CreateStyles("Calibri", 22, [
        ("Heading1", "Name Header", true, false, 32, "2E74B5"),
        ("Heading2", "Section Header", true, false, 24, "2E74B5"),
        ("Heading3", "Job Title", true, true, 22, "000000"),
        ("Normal", "Body Text", false, false, 22, null),
    ]);

static Styles CreateClassicStyles() =>
    CreateStyles("Times New Roman", 24, [
        ("Heading1", "Name", true, false, 32, null),
        ("Heading2", "Section", true, false, 26, null),
        ("Heading3", "Role", true, true, 24, null),
        ("Normal", "Normal", false, false, 24, null),
    ]);

static Styles CreateMinimalStyles() =>
    CreateStyles("Arial", 20, [
        ("Title", "Name", true, false, 28, null),
        ("Subtitle", "Section", false, false, 20, "595959"),
        ("Normal", "Normal", false, false, 20, null),
    ]);

static Styles CreateFunctionalStyles() =>
    CreateStyles("Georgia", 22, [
        ("Heading1", "Candidate Name", true, false, 30, null),
        ("Heading2", "Section Heading", true, false, 24, "4472C4"),
        ("Heading3", "Role Heading", false, true, 22, null),
        ("Normal", "Normal", false, false, 22, null),
    ]);

static Styles CreateStyles(
    string font, int defaultSizeHp,
    (string id, string name, bool bold, bool italic, int size, string? color)[] styleSpecs)
{
    var styles = new Styles();

    // Document defaults
    var docDefaults = new DocDefaults(
        new RunPropertiesDefault(
            new RunPropertiesBaseStyle(
                new RunFonts { Ascii = font },
                new FontSize { Val = defaultSizeHp.ToString() })));
    styles.AppendChild(docDefaults);

    // Normal (base) style always first
    bool hasNormal = styleSpecs.Any(s => s.id == "Normal");
    if (!hasNormal)
        styles.AppendChild(CreateStyle("Normal", "Normal", false, false, defaultSizeHp, font, null));

    foreach (var (id, name, bold, italic, size, color) in styleSpecs)
        styles.AppendChild(CreateStyle(id, name, bold, italic, size, font, color));

    return styles;
}

static Style CreateStyle(string id, string name, bool bold, bool italic, int sizeHp, string font, string? hexColor)
{
    var style = new Style { Type = StyleValues.Paragraph, StyleId = id };
    style.AppendChild(new StyleName { Val = name });

    var rpr = new StyleRunProperties();
    rpr.AppendChild(new RunFonts { Ascii = font });
    if (bold) rpr.AppendChild(new Bold());
    if (italic) rpr.AppendChild(new Italic());
    rpr.AppendChild(new FontSize { Val = sizeHp.ToString() });
    if (hexColor is not null) rpr.AppendChild(new Color { Val = hexColor });
    style.AppendChild(rpr);

    return style;
}