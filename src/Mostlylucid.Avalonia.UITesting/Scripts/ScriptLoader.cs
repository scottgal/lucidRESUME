using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mostlylucid.Avalonia.UITesting.Scripts;

public static class ScriptLoader
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static UIScript LoadFromYaml(string path)
    {
        var yaml = File.ReadAllText(path);
        var script = YamlDeserializer.Deserialize<UIScript>(yaml);
        if (string.IsNullOrEmpty(script.Name))
            script.Name = Path.GetFileNameWithoutExtension(path);
        return script;
    }

    public static UIScript ParseYaml(string yaml)
    {
        return YamlDeserializer.Deserialize<UIScript>(yaml);
    }

    public static UIScript LoadFromJson(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<UIScript>(json, JsonOptions)
            ?? throw new InvalidDataException($"Failed to parse {path}");
    }

    public static UIScript ParseJson(string json)
    {
        return JsonSerializer.Deserialize<UIScript>(json, JsonOptions)
            ?? throw new InvalidDataException("Failed to parse JSON");
    }

    public static void SaveAsYaml(UIScript script, string path)
    {
        var yaml = YamlSerializer.Serialize(script);
        File.WriteAllText(path, yaml);
    }

    public static void SaveAsJson(UIScript script, string path)
    {
        var json = JsonSerializer.Serialize(script, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static string ToYaml(UIScript script)
    {
        return YamlSerializer.Serialize(script);
    }

    public static string ToJson(UIScript script)
    {
        return JsonSerializer.Serialize(script, JsonOptions);
    }

    public static IEnumerable<UIScript> LoadFromDirectory(string directory, string pattern = "*.yaml")
    {
        foreach (var file in Directory.GetFiles(directory, pattern))
        {
            yield return LoadFromYaml(file);
        }
    }
}
