using System.Text.RegularExpressions;

namespace lucidRESUME.Core.Models.Jobs;

public sealed record JobSourceSection(string Text, string Section);

/// <summary>Source boundaries shared by ingestion and writing; section labels never establish candidate evidence.</summary>
public static class JobTextSections
{
    public static string? SectionKind(string line)
    {
        var text = line.Trim().TrimStart('#').Replace("**", "").Trim().TrimEnd(':').ToLowerInvariant()
            .Replace('’', '\'');
        return text switch
        {
            "who you are" or "who thrives here" or "you'll likely have" or "you will likely have" or
            "essential" or "essential skills" or "what we need" or "required skills" or "must have" or
            "what we're looking for" or "what we are looking for" or "requirements" or "required qualifications" or
            "minimum qualifications" or "qualifications" or "what you'll bring" => "required",
            "what sets you apart" or "nice to have" or "nice-to-have" or "preferred qualifications" or
            "preferred skills" or "bonus" or "desirable" => "preferred",
            "what success looks like" or "what you'll work on" or
            "what you'll be doing" or "what you will be doing" or "what you'll own" or "what you will own" or "what you'll do" or "what you will do" or
            "responsibilities" or "key responsibilities" => "responsibility",
            "why consider it?" or "the details" or "interested?" or "the opportunity" or
            "about the role" or "about us" or "about the company" or "the environment" or
            "location and working style" or "location" or "working style" or "benefits" or "compensation" or
            "compensation and benefits" or "what we offer" or "how to apply" or "equal opportunities" => "ignore",
            _ => null
        };
    }

    public static IReadOnlyList<JobSourceSection> Read(string text)
    {
        var result = new List<JobSourceSection>();
        var section = "unspecified";
        var normalized = Regex.Replace(text, @"(?im)(?:^|(?<=[.!?])[ \t]+)(?=(?:nice[- ]to[- ]have|preferred qualifications|benefits|essential)\b)", "\n");
        foreach (var raw in normalized.Split('\n'))
        {
            var line = raw.Trim().TrimStart('#', '*', '•', ' ').Trim();
            if (line.Length == 0) continue;
            var kind = SectionKind(line);
            if (kind is not null) { section = kind; continue; }
            var header = Regex.Match(line, @"^(nice[- ]to[- ]have|preferred qualifications|benefits|essential|what we need|what you'll be doing)\b\s*:?[ \t]*", RegexOptions.IgnoreCase);
            if (header.Success && SectionKind(header.Groups[1].Value) is { } inlineKind)
            { section = inlineKind; line = line[header.Length..].Trim(); }
            if (line.Length == 0) continue;
            var local = Regex.IsMatch(line, @"^(?:preferably|ideally|bonus|desirable)\b", RegexOptions.IgnoreCase) ? "preferred" : section;
            result.Add(new(line, local));
        }
        return result;
    }
}
