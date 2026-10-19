using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;

namespace lucidRESUME.Export;

/// <summary>Replace edited career sections while retaining unrelated author prose and external evidence.</summary>
public static class CareerProseEditor
{
    public static JobMlFile Merge(ResumeDocument previous, JobMlFile rebuilt, IReadOnlySet<string> subjects)
    {
        var parser = new JobMlParser();
        if (string.IsNullOrWhiteSpace(previous.JobMlSource) ||
            !parser.TryParse(previous.JobMlSource, out var original, out _))
        {
            var narrative = previous.CanonicalMarkdown ?? previous.RawMarkdown;
            return string.IsNullOrWhiteSpace(narrative) ? rebuilt : new JobMlFile(
                rebuilt.Markdown + "\n\n## Previous career narrative\n\n" + narrative, rebuilt.Data);
        }

        var file = original!;
        var markdown = file.Markdown;
        if (subjects.Contains("personal"))
        {
            var heading = Regex.Match(rebuilt.Markdown, @"(?m)^# [^\r\n]+$");
            if (heading.Success) markdown = new Regex(@"(?m)^# [^\r\n]+$").Replace(markdown, _ => heading.Value, 1);
        }
        foreach (var subject in subjects)
        {
            var id = CareerRecordJobMlBuilder.StableEntityId(subject);
            var replacement = Section(rebuilt.Markdown, id);
            if (replacement is null) throw new InvalidOperationException("The edited career section could not be rebuilt.");
            var oldEntity = file.Data.Entities.SingleOrDefault(entity => entity.Id == id);
            var old = Section(markdown, id);
            // Legacy aliases need deliberate reconciliation, rather than duplicate role identities.
            if (oldEntity is null && file.Data.Claims.Any(claim => claim.Evidence.Any(evidence =>
                    (evidence.Id ?? "").Contains(subject + ":", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("This role has legacy evidence links. Open the advanced evidence editor to reconcile it before editing.");

            var oldClaims = file.Data.Claims.Where(claim => claim.Subject == id).ToList();
            var extra = old?.Value ?? "";
            if (old is not null)
            {
                extra = Regex.Replace(extra, @"\A[^\n]*(?:\n|$)", "");
                foreach (var claim in oldClaims)
                    extra = Regex.Replace(extra,
                        @"(?ms)^\s*<p\s+id=[""']" + Regex.Escape(claim.Id) + @"[""']\s*>.*?</p>\s*", "");
            }
            var inserted = replacement.Value.TrimEnd() + (string.IsNullOrWhiteSpace(extra) ? "\n\n" : "\n\n" + extra.Trim() + "\n\n");
            markdown = old is null ? markdown.TrimEnd() + "\n\n" + inserted
                : markdown.Remove(old.Index, old.Length).Insert(old.Index, inserted);

            var replacements = rebuilt.Data.Claims.Where(claim => claim.Subject == id).ToList();
            foreach (var claim in replacements)
            {
                var unchanged = oldClaims.FirstOrDefault(oldClaim => oldClaim.Type == claim.Type && oldClaim.Statement == claim.Statement);
                if (unchanged is null) continue;
                foreach (var evidence in unchanged.Evidence.Where(evidence => !string.IsNullOrWhiteSpace(evidence.Uri)))
                    if (claim.Evidence.All(item => item.Uri != evidence.Uri)) claim.Evidence.Add(evidence);
            }
            file.Data.Claims.RemoveAll(claim => claim.Subject == id);
            file.Data.Claims.AddRange(replacements);
            file.Data.Entities.RemoveAll(entity => entity.Id == id);
            file.Data.Entities.Add(rebuilt.Data.Entities.Single(entity => entity.Id == id));
        }
        foreach (var source in rebuilt.Data.Sources)
            if (file.Data.Sources.All(item => item.Id != source.Id)) file.Data.Sources.Add(source);
        foreach (var concept in rebuilt.Data.Concepts)
            if (file.Data.Concepts.All(item => item.Id != concept.Id)) file.Data.Concepts.Add(concept);
        foreach (var space in rebuilt.Data.SemanticSpaces)
            if (file.Data.SemanticSpaces.All(item => item.Id != space.Id)) file.Data.SemanticSpaces.Add(space);
        if (rebuilt.Data.Extensions is not null)
        {
            file.Data.Extensions ??= [];
            foreach (var extension in rebuilt.Data.Extensions) file.Data.Extensions[extension.Key] = extension.Value;
        }
        return new JobMlFile(markdown.TrimEnd(), file.Data);
    }

    private static Match? Section(string markdown, string id)
    {
        var heading = Regex.Match(markdown, @"(?m)^(?<level>#{1,6}) .+\{#" + Regex.Escape(id) + @"\}[^\r\n]*$");
        if (!heading.Success) return null;
        var rest = markdown[(heading.Index + heading.Length)..];
        var next = Regex.Match(rest, @"(?m)^#{1," + heading.Groups["level"].Length + @"} ");
        var length = next.Success ? heading.Length + next.Index : markdown.Length - heading.Index;
        return new Regex(@"(?s)\G.{" + length + "}", RegexOptions.None, TimeSpan.FromSeconds(1)).Match(markdown, heading.Index);
    }
}
