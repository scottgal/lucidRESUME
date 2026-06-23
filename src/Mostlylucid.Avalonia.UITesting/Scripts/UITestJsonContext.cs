using System.Text.Json.Serialization;

namespace Mostlylucid.Avalonia.UITesting.Scripts;

// Source-generated JSON serialization context for the harness's POCO
// surface. Lets consumers PublishTrimmed/PublishAot the host app without
// the reflection-based JsonSerializer overloads silently dropping
// fields. Used by ScriptLoader.LoadFromJson / SaveAsJson and by the
// test result writeout in UITestingExtensions.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UIScript))]
[JsonSerializable(typeof(UIAction))]
[JsonSerializable(typeof(UITestResult))]
[JsonSerializable(typeof(UIActionResult))]
[JsonSerializable(typeof(ActionType))]
[JsonSerializable(typeof(List<UIAction>))]
[JsonSerializable(typeof(List<UIActionResult>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class UITestJsonContext : JsonSerializerContext
{
}
