using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;

namespace lucidRESUME.Export;

/// <summary>Append reviewed interview prose without replacing the author's existing narrative.</summary>
public static class RoleInterviewTranscriptUpdater
{
    public static JobMlFile Merge(ResumeDocument previous, JobMlFile rebuilt, Guid roleId, string reviewedProse)
    {
        var parser = new JobMlParser();
        if (string.IsNullOrWhiteSpace(previous.JobMlSource) ||
            !parser.TryParse(previous.JobMlSource, out var original, out _))
        {
            var human = previous.CanonicalMarkdown ?? previous.RawMarkdown;
            return string.IsNullOrWhiteSpace(human) ? rebuilt : new JobMlFile(
                rebuilt.Markdown + "\n\n## Original career narrative\n\n" + human.Trim(), rebuilt.Data);
        }

        var claim = rebuilt.Data.Claims.Single(c => c.Type == "achievement" && c.Statement == reviewedProse &&
            c.Subject == CareerRecordJobMlBuilder.StableEntityId($"experience:{roleId:N}"));
        var originalRole = previous.Experience.Single(role => role.Id == roleId);
        var additions = rebuilt.Data.Claims.Where(candidate => candidate.Subject == claim.Subject &&
            candidate.Type == "technology" && !originalRole.Technologies.Contains(candidate.Statement, StringComparer.OrdinalIgnoreCase))
            .Prepend(claim).ToList();
        var entity = rebuilt.Data.Entities.Single(e => e.Id == claim.Subject);
        if (original!.Data.Entities.All(e => e.Id != entity.Id))
        {
            var linkedSubjects = original.Data.Claims.Where(c => c.Evidence.Any(e =>
                    (e.Id ?? "").Contains($"experience:{roleId:N}:", StringComparison.OrdinalIgnoreCase) ||
                    (e.Ref ?? "").Contains($"experience:{roleId:N}:", StringComparison.OrdinalIgnoreCase)))
                .Select(c => c.Subject).Distinct().ToList();
            var aliases = original.Data.Entities.Where(e => e.Type == "experience" &&
                (linkedSubjects.Contains(e.Id) || Normalize(e.Name) == Normalize(entity.Name))).ToList();
            if (aliases.Count > 1)
                throw new InvalidOperationException("Multiple transcript roles match this experience. Reconcile their identities in the JobML editor first.");
            if (aliases.Count == 1)
            {
                entity = aliases[0];
                foreach (var addition in additions) addition.Subject = entity.Id;
            }
            else original.Data.Entities.Add(entity);
        }
        // Refresh our portable career metadata while retaining extensions owned by other tools.
        // Raw interview answers belong in the complete record even when their reviewed claim already exists.
        if (rebuilt.Data.Extensions is not null)
        {
            original.Data.Extensions ??= new Dictionary<string, object?>();
            foreach (var extension in rebuilt.Data.Extensions)
                original.Data.Extensions[extension.Key] = extension.Value;
        }
        if (original.Data.Claims.Any(c => c.Id == claim.Id)) return original;
        foreach (var addition in additions)
            if (original.Data.Claims.All(existing => existing.Id != addition.Id)) original.Data.Claims.Add(addition);
        foreach (var source in rebuilt.Data.Sources)
            if (original.Data.Sources.All(s => s.Id != source.Id)) original.Data.Sources.Add(source);
        foreach (var concept in rebuilt.Data.Concepts)
            if (original.Data.Concepts.All(c => c.Id != concept.Id)) original.Data.Concepts.Add(concept);
        foreach (var space in rebuilt.Data.SemanticSpaces)
            if (original.Data.SemanticSpaces.All(s => s.Id != space.Id)) original.Data.SemanticSpaces.Add(space);

        var paragraph = string.Concat(additions.Select(addition => $"\n\n<p id=\"{addition.Id}\">\n{addition.Statement}\n</p>\n\n"));
        var markdown = original.Markdown;
        var heading = Regex.Match(markdown, @"(?m)^(?<level>#{1,6}) .+\{#" + Regex.Escape(entity.Id) + @"\}[^\r\n]*$");
        if (heading.Success)
        {
            var level = heading.Groups["level"].Length;
            var nextHeading = Regex.Match(markdown[(heading.Index + heading.Length)..], @"(?m)^#{1," + level + @"} ");
            var insertion = nextHeading.Success ? heading.Index + heading.Length + nextHeading.Index : markdown.Length;
            markdown = markdown.Insert(insertion, paragraph);
        }
        else markdown += $"\n\n## {entity.Name} {{#{entity.Id}}}" + paragraph;
        return new JobMlFile(markdown, original.Data);
    }

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
}
