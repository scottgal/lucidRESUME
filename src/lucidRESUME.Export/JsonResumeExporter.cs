using System.Text.Json;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Export.JsonResume;

namespace lucidRESUME.Export;

public sealed class JsonResumeExporter : IResumeExporter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ExportFormat Format => ExportFormat.JsonResume;

    public Task<byte[]> ExportAsync(ResumeDocument resume, CancellationToken ct = default)
    {
        var root = MapToJsonResume(resume);
        var json = JsonSerializer.SerializeToUtf8Bytes(root, JsonOpts);
        return Task.FromResult(json);
    }

    private static JsonResumeRoot MapToJsonResume(ResumeDocument r) => new()
    {
        Basics = new JsonResumeBasics
        {
            Name = r.Personal.FullName,
            Email = r.Personal.Email,
            Phone = r.Personal.Phone,
            Summary = r.Personal.Summary,
            Url = r.Personal.WebsiteUrl,
            Location = r.Personal.Location != null ? new JsonResumeLocation(r.Personal.Location, null) : null,
            Profiles = BuildProfiles(r.Personal)
        },
        Work = r.Experience.Select(e => new JsonResumeWork(
            e.Company, e.Title,
            e.StartDate?.ToString("yyyy-MM-dd"), e.EndDate?.ToString("yyyy-MM-dd"),
            null, e.Achievements)).ToList(),
        Education = r.Education.Select(e => new JsonResumeEducation(
            e.Institution, e.FieldOfStudy, e.Degree,
            e.StartDate?.ToString("yyyy-MM-dd"), e.EndDate?.ToString("yyyy-MM-dd"),
            e.Gpa?.ToString())).ToList(),
        Skills = r.Skills.GroupBy(s => s.Category ?? "General")
            .Select(g => new JsonResumeSkill(g.Key, null, g.Select(s => s.Name).ToList())).ToList(),
        Certificates = r.Certifications.Select(c => new JsonResumeCertificate(
            c.Name, c.IssuedDate?.ToString("yyyy-MM-dd"), c.Issuer, c.CredentialUrl)).ToList(),
        Projects = r.Projects.Select(p => new JsonResumeProject(
            p.Name, p.Description, p.Technologies, p.Url)).ToList()
    };

    private static List<JsonResumeProfile> BuildProfiles(PersonalInfo p)
    {
        var profiles = new List<JsonResumeProfile>();
        if (p.LinkedInUrl != null) profiles.Add(new JsonResumeProfile("LinkedIn", p.LinkedInUrl, null));
        if (p.GitHubUrl != null) profiles.Add(new JsonResumeProfile("GitHub", p.GitHubUrl, null));
        return profiles;
    }
}
