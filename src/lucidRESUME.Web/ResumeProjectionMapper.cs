using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using lucidRESUME.Compiler;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.JobML;

namespace lucidRESUME.Web;

internal static class ResumeProjectionMapper
{
    public static ResumeDocument Build(CompilationResult result, bool includeCitations = true, int minimumPages = 2)
    {
        var resume = ResumeDocument.Create("tailored.md", "text/markdown",
            Encoding.UTF8.GetByteCount(result.HumanMarkdown));
        resume.SetDoclingOutput(result.HumanMarkdown, null, null);
        resume.CanonicalMarkdown = result.HumanMarkdown;
        resume.JobMlSource = result.FullJobMlMarkdown;
        resume.JobMlRevision = result.Manifest.SourceRevision;
        resume.TargetRole = result.Manifest.TargetTitle;
        resume.IncludeCompactJobMl = includeCitations;
        resume.MinimumOutputPages = Math.Clamp(minimumPages, 1, 2);
        MarkdownSectionParser.PopulateSections(resume, result.HumanMarkdown);
        PopulateSections(resume, result.ProjectedJobMl, result.Manifest);
        return resume;
    }

    private static void PopulateSections(ResumeDocument resume, JobMlFile projection,
        ProjectionManifest manifest)
    {
        // Export from the selected evidence bindings, not by re-inferring structure
        // from the rendered text. The Markdown parser remains useful for contact data.
        resume.Personal.Summary = null;
        resume.Experience.Clear();
        resume.Projects.Clear();
        resume.Publications.Clear();
        resume.Education.Clear();

        var index = MarkdownEvidenceIndex.Create(projection.Markdown);
        var claimsBySubject = projection.Data.Claims
            .GroupBy(claim => claim.Subject, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var packetsBySection = manifest.Sections
            .ToDictionary(packet => packet.SectionId, StringComparer.OrdinalIgnoreCase);
        var ordered = projection.Data.Entities.Select(entity =>
        {
            var claims = claimsBySubject.GetValueOrDefault(entity.Id) ?? [];
            var passages = claims.SelectMany(claim => claim.Evidence)
                .Where(evidence => evidence.Type == "prose" && !string.IsNullOrWhiteSpace(evidence.Ref))
                .Select(evidence => index.TryGet(evidence.Ref!, out var passage) ? passage : null)
                .Where(passage => passage is not null)
                .Cast<ProsePassage>()
                .DistinctBy(passage => (passage.SourceStart, passage.SourceLength))
                .OrderBy(passage => passage.SourceStart)
                .ToList();
            return new
            {
                Entity = entity,
                Claims = claims,
                Passages = passages,
                Packet = FindPacket(entity.Source, claims, packetsBySection),
                Start = passages.FirstOrDefault()?.SourceStart ?? int.MaxValue
            };
        }).OrderBy(item => item.Start);

        foreach (var item in ordered)
        {
            var prose = string.Join(" ", item.Passages.Select(passage => passage.Text));
            if (string.IsNullOrWhiteSpace(prose)) continue;
            if (item.Claims.Any(claim => claim.Type == "summary"))
            {
                resume.Personal.Summary = prose;
                continue;
            }

            if (item.Entity.Type == "project")
            {
                resume.Projects.Add(new Project { Name = item.Entity.Name, Description = prose });
                continue;
            }

            if (item.Entity.Type == "education")
            {
                var education = SplitEducationHeading(item.Entity.Name);
                resume.Education.Add(new Education
                {
                    Degree = education.Degree,
                    Institution = education.Institution,
                    Highlights = [prose]
                });
                continue;
            }

            if (item.Entity.Type == "publication")
            {
                var url = Regex.Match(prose, @"https?://\S+", RegexOptions.CultureInvariant).Value
                    .TrimEnd('.', ',', ';', ')');
                resume.Publications.Add(new Project
                {
                    Name = item.Entity.Name,
                    Description = string.IsNullOrWhiteSpace(url) ? prose : null,
                    Url = string.IsNullOrWhiteSpace(url) ? null : url
                });
                continue;
            }

            if (item.Entity.Type != "experience") continue;
            var role = item.Entity.Name.Split(" · ", 2, StringSplitOptions.TrimEntries);
            var experience = new WorkExperience
            {
                Title = role[0],
                Company = role.Length > 1 ? role[1] : null,
                Achievements = [prose],
                IsCompact = item.Packet?.Kind.Equals("additional_experience",
                    StringComparison.OrdinalIgnoreCase) == true
            };
            ApplyDateRange(experience, item.Packet?.Heading);
            resume.Experience.Add(experience);
        }
    }

    private static EvidencePacket? FindPacket(string? source,
        IReadOnlyList<JobMlClaim> claims,
        IReadOnlyDictionary<string, EvidencePacket> packetsBySection)
    {
        if (!string.IsNullOrWhiteSpace(source))
        {
            var sectionId = source.Trim().TrimStart('#').Split(':', 2)[0];
            if (packetsBySection.TryGetValue(sectionId, out var sourced)) return sourced;
        }

        var claimIds = claims.Select(claim => claim.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return packetsBySection.Values.FirstOrDefault(packet =>
            packet.Claims.Any(item => claimIds.Contains(item.Claim.Id)));
    }

    private static void ApplyDateRange(WorkExperience experience, string? heading)
    {
        if (string.IsNullOrWhiteSpace(heading)) return;
        var match = Regex.Match(heading,
            @"\|\s*(?<start>[A-Za-z]{3,9}\s+\d{4})\s*[-–]\s*(?<end>[A-Za-z]{3,9}\s+\d{4}|Present)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !TryParseMonth(match.Groups["start"].Value, out var start)) return;

        experience.StartDate = start;
        experience.IsCurrent = match.Groups["end"].Value.Equals("Present", StringComparison.OrdinalIgnoreCase);
        if (!experience.IsCurrent && TryParseMonth(match.Groups["end"].Value, out var end))
            experience.EndDate = end;
    }

    private static bool TryParseMonth(string value, out DateOnly date)
    {
        if (DateTime.TryParseExact(value.Trim(), ["MMM yyyy", "MMMM yyyy"],
                CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }

        date = default;
        return false;
    }

    private static (string? Degree, string Institution) SplitEducationHeading(string heading)
    {
        var parts = heading.Split('|', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (null, heading);
    }
}
