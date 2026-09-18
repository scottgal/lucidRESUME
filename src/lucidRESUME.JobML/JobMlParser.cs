using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace lucidRESUME.JobML;

public sealed class JobMlParseException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class JobMlParser
{
    private static readonly Regex JobMlFence = new(
        @"(?ms)^[ \t]*```jobml[ \t]*\n(?<yaml>.*?)[ \t]*```[ \t]*(?:\n|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LegacyVersion = new(
        "(?m)^jobml:\\s*[\\\"']?(?<version>[^\\s\\\"']+)[\\\"']?\\s*$",
        RegexOptions.Compiled);
    private static readonly Regex LegacyEvidenceKey = new(
        "(?m)^(?<indent>[ \\t]+)evidence:(?<value>.*)$",
        RegexOptions.Compiled);
    private static readonly Regex MachineAreaWrapper = new(
        @"(?ms)\n---[ \t]*\n+[ \t]*## MACHINE AREA[ \t]*\n+.*?\[What is this\?\]\([^\r\n]+\)[ \t]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public JobMlFile Parse(string source)
    {
        var normalized = MarkdownEvidenceIndex.NormalizeNewlines(source);
        var matches = JobMlFence.Matches(normalized);
        if (matches.Count == 0)
            throw new JobMlParseException("The document does not contain a fenced ```jobml block.");
        if (matches.Count > 1)
            throw new JobMlParseException("JobML 0.1 documents must contain exactly one fenced jobml block.");

        try
        {
            var yaml = UpgradeLegacySyntax(matches[0].Groups["yaml"].Value);
            var root = _deserializer.Deserialize<JobMlRoot>(yaml)
                       ?? throw new JobMlParseException("The JobML block is empty.");
            var markdown = normalized.Remove(matches[0].Index, matches[0].Length).TrimEnd();
            markdown = MachineAreaWrapper.Replace(markdown, "").TrimEnd();
            markdown = Regex.Replace(markdown, @"(?m)\n---[ \t]*$", "").TrimEnd();
            return new JobMlFile(markdown, root);
        }
        catch (YamlException ex)
        {
            throw new JobMlParseException($"Invalid JobML YAML at line {ex.Start.Line}: {ex.Message}", ex);
        }
    }

    private static string UpgradeLegacySyntax(string yaml)
    {
        yaml = LegacyEvidenceKey.Replace(yaml, "${indent}supported_by:${value}");
        return LegacyVersion.Replace(yaml, match =>
        {
            var version = match.Groups["version"].Value;
            var semantics = string.Join("\n", JobMlHeader.DefaultSemantics.Select(value => $"    - {value}"));
            return $"""
                jobml:
                  version: "{version}"
                  purpose: >
                    Machine-readable representation of claims made by this resume. Claims are supported by
                    human-readable prose or external evidence. Absence of a claim does not imply absence of
                    a skill or capability.
                  semantics:
                {semantics}
                """;
        });
    }

    public bool TryParse(string source, out JobMlFile? file, out string? error)
    {
        try
        {
            file = Parse(source);
            error = null;
            return true;
        }
        catch (JobMlParseException ex)
        {
            file = null;
            error = ex.Message;
            return false;
        }
    }

    public string Serialize(JobMlFile file)
    {
        var markdown = file.Markdown.TrimEnd();
        var yaml = SerializeYaml(file.Data);
        return $"{markdown}\n\n---\n\n```jobml\n{yaml}\n```\n";
    }

    public JobMlRoot ParseYaml(string yaml)
    {
        try
        {
            return _deserializer.Deserialize<JobMlRoot>(UpgradeLegacySyntax(yaml))
                   ?? throw new JobMlParseException("The JobML YAML is empty.");
        }
        catch (YamlException ex)
        {
            throw new JobMlParseException($"Invalid JobML YAML at line {ex.Start.Line}: {ex.Message}", ex);
        }
    }

    public bool TryParseYaml(string yaml, out JobMlRoot? root, out string? error)
    {
        try
        {
            root = ParseYaml(yaml);
            error = null;
            return true;
        }
        catch (JobMlParseException ex)
        {
            root = null;
            error = ex.Message;
            return false;
        }
    }

    public string SerializeYaml(JobMlRoot root) => _serializer.Serialize(root).TrimEnd();
}
