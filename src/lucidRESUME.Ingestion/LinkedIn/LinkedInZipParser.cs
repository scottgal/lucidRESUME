using System.Globalization;
using System.IO.Compression;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.Ingestion.LinkedIn;

/// <summary>
/// Parses a LinkedIn data export ZIP archive into a ResumeDocument.
/// Reads: Profile.csv, Positions.csv, Education.csv, Skills.csv, Projects.csv,
/// Email Addresses.csv, PhoneNumbers.csv, Endorsement_Received_Info.csv.
/// </summary>
public sealed class LinkedInZipParser
{
    private readonly ILogger<LinkedInZipParser> _logger;

    public LinkedInZipParser(ILogger<LinkedInZipParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detect whether a file is a LinkedIn data export ZIP.
    /// </summary>
    public static bool IsLinkedInExport(string filePath)
    {
        if (!filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            return zip.Entries.Any(e => e.FullName is "Profile.csv" or "Positions.csv" or "Skills.csv");
        }
        catch { return false; }
    }

    public async Task<ResumeDocument> ParseAsync(string zipPath, CancellationToken ct = default)
    {
        var resume = ResumeDocument.Create(Path.GetFileName(zipPath), "application/x-linkedin-export", new FileInfo(zipPath).Length);

        using var zip = ZipFile.OpenRead(zipPath);
        var csvs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
        {
            await using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            csvs[entry.FullName] = await reader.ReadToEndAsync(ct);
        }

        ParseProfile(csvs, resume);
        ParsePositions(csvs, resume);
        ParseEducation(csvs, resume);
        ParseSkills(csvs, resume);
        ParseProjects(csvs, resume);
        ParseEmails(csvs, resume);
        ParsePhones(csvs, resume);
        ParseEndorsements(csvs, resume);

        // Build plain text summary for downstream NER/matching
        resume.PlainText = BuildPlainText(resume);

        _logger.LogInformation("LinkedIn import: {Skills} skills, {Positions} positions, {Education} education entries",
            resume.Skills.Count, resume.Experience.Count, resume.Education.Count);

        return resume;
    }

    private static void ParseProfile(Dictionary<string, string> csvs, ResumeDocument resume)
    {
        if (!csvs.TryGetValue("Profile.csv", out var csv)) return;
        var rows = ParseCsv(csv);
        if (rows.Count == 0) return;
        var row = rows[0];

        resume.Personal.FullName = Concat(Get(row, "First Name"), Get(row, "Last Name"));
        resume.Personal.Location = Get(row, "Geo Location");
        resume.Personal.Summary = Get(row, "Summary");
        resume.Personal.WebsiteUrl = Get(row, "Websites")?.Replace("[COMPANY:", "").TrimEnd(']');
        resume.Personal.LinkedInUrl = "https://linkedin.com/in/"; // will be enriched if available
    }

    private static void ParsePositions(Dictionary<string, string> csvs, ResumeDocument resume)
    {
        if (!csvs.TryGetValue("Positions.csv", out var csv)) return;
        foreach (var row in ParseCsv(csv))
        {
            var exp = new WorkExperience
            {
                Company = Get(row, "Company Name") ?? "",
                Title = Get(row, "Title") ?? "",
                Location = Get(row, "Location"),
                StartDate = ParseMonthYear(Get(row, "Started On")),
                EndDate = ParseMonthYear(Get(row, "Finished On")),
            };
            exp.IsCurrent = exp.EndDate is null;

            var desc = Get(row, "Description");
            if (!string.IsNullOrEmpty(desc))
            {
                // Split description into bullet points
                var lines = desc.Split(['\n', '•', '·'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim().TrimStart('-', ' '))
                    .Where(l => l.Length > 10)
                    .ToList();
                exp.Achievements = lines;
            }

            resume.Experience.Add(exp);
        }
    }

    private static void ParseEducation(Dictionary<string, string> csvs, ResumeDocument resume)
    {
        if (!csvs.TryGetValue("Education.csv", out var csv)) return;
        foreach (var row in ParseCsv(csv))
        {
            resume.Education.Add(new Education
            {
                Institution = Get(row, "School Name") ?? "",
                Degree = Get(row, "Degree Name"),
                FieldOfStudy = Get(row, "Notes"),
                StartDate = ParseYear(Get(row, "Start Date")),
                EndDate = ParseYear(Get(row, "End Date")),
            });
        }
    }

    private static void ParseSkills(Dictionary<string, string> csvs, ResumeDocument resume)
    {
        if (!csvs.TryGetValue("Skills.csv", out var csv)) return;
        foreach (var row in ParseCsv(csv))
        {
            var name = Get(row, "Name");
            if (!string.IsNullOrEmpty(name))
                resume.Skills.Add(new Skill { Name = name });
        }
    }

    private static void ParseProjects(Dictionary<string, string> csvs, ResumeDocument resume)
    {
        if (!csvs.TryGetValue("Projects.csv", out var csv)) return;
        foreach (var row in ParseCsv(csv))
        {
            resume.Projects.Add(new Project
            {
                Name = Get(row, "Title") ?? "",
                Description = Get(row, "Description"),
                Url = Get(row, "Url"),
                Date = ParseMonthYear(Get(row, "Started On")),
            });
        }
    }

    private static void ParseEmails(Dictionary<string, string> csvs, ResumeDocument resume)
    {
        if (!csvs.TryGetValue("Email Addresses.csv", out var csv)) return;
        var rows = ParseCsv(csv);
        if (rows.Count > 0)
            resume.Personal.Email = Get(rows[0], "Email Address");
    }

    private static void ParsePhones(Dictionary<string, string> csvs, ResumeDocument resume)
    {
        if (!csvs.TryGetValue("PhoneNumbers.csv", out var csv)) return;
        var rows = ParseCsv(csv);
        if (rows.Count > 0)
            resume.Personal.Phone = Get(rows[0], "Number");
    }

    private static void ParseEndorsements(Dictionary<string, string> csvs, ResumeDocument resume)
    {
        // Endorsement counts can boost skill confidence later
        if (!csvs.TryGetValue("Endorsement_Received_Info.csv", out var csv)) return;
        var endorsements = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ParseCsv(csv))
        {
            var skill = Get(row, "Skill Name");
            if (skill != null)
                endorsements[skill] = endorsements.GetValueOrDefault(skill) + 1;
        }

        // Set YearsExperience as endorsement count proxy (capped)
        foreach (var skill in resume.Skills)
        {
            if (endorsements.TryGetValue(skill.Name, out var count))
                skill.YearsExperience = Math.Min(count, 99); // store endorsement count for now
        }
    }

    private static string BuildPlainText(ResumeDocument resume)
    {
        var parts = new List<string>();
        if (resume.Personal.FullName != null) parts.Add(resume.Personal.FullName);
        if (resume.Personal.Summary != null) parts.Add(resume.Personal.Summary);
        foreach (var exp in resume.Experience)
        {
            parts.Add($"{exp.Title} at {exp.Company}");
            parts.AddRange(exp.Achievements);
        }
        foreach (var skill in resume.Skills)
            parts.Add(skill.Name);
        return string.Join("\n", parts);
    }

    // --- CSV helpers ---

    private static List<Dictionary<string, string>> ParseCsv(string csv)
    {
        var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count < 2) return [];

        var headers = SplitCsvLine(lines[0]);
        var rows = new List<Dictionary<string, string>>();

        for (var i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var values = SplitCsvLine(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var j = 0; j < Math.Min(headers.Count, values.Count); j++)
            {
                if (!string.IsNullOrWhiteSpace(values[j]))
                    row[headers[j]] = values[j];
            }
            rows.Add(row);
        }
        return rows;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var inQuotes = false;
        var field = new System.Text.StringBuilder();

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(field.ToString().Trim());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }
        fields.Add(field.ToString().Trim());
        return fields;
    }

    private static string? Get(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static string? Concat(string? a, string? b) =>
        (a, b) switch
        {
            (not null, not null) => $"{a} {b}",
            (not null, _) => a,
            (_, not null) => b,
            _ => null
        };

    private static DateOnly? ParseMonthYear(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (DateTime.TryParseExact(value.Trim(), ["MMM yyyy", "MMMM yyyy"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    private static DateOnly? ParseYear(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (int.TryParse(value.Trim(), out var year) && year > 1900 && year < 2100)
            return new DateOnly(year, 1, 1);
        return null;
    }
}
