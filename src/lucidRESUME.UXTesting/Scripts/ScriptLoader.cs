using Newtonsoft.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace lucidRESUME.UXTesting.Scripts;

public static class ScriptLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static UXScript LoadFromYaml(string path)
    {
        var yaml = File.ReadAllText(path);
        var script = Deserializer.Deserialize<UXScript>(yaml);
        if (string.IsNullOrEmpty(script.Name))
            script.Name = Path.GetFileNameWithoutExtension(path);
        return script;
    }

    public static UXScript LoadFromJson(string path)
    {
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<UXScript>(json) 
            ?? throw new InvalidDataException($"Failed to parse {path}");
    }

    public static void SaveAsYaml(UXScript script, string path)
    {
        var yaml = Serializer.Serialize(script);
        File.WriteAllText(path, yaml);
    }

    public static void SaveAsJson(UXScript script, string path)
    {
        var json = JsonConvert.SerializeObject(script, Formatting.Indented);
        File.WriteAllText(path, json);
    }

    public static IEnumerable<UXScript> LoadFromDirectory(string directory, string pattern = "*.ux.yaml")
    {
        foreach (var file in Directory.GetFiles(directory, pattern))
        {
            yield return LoadFromYaml(file);
        }
    }
}
