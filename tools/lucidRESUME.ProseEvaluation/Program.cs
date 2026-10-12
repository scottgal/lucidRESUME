using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using lucidRESUME.AI;
using lucidRESUME.Compiler;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;
using Microsoft.Extensions.Options;

// Synthetic inputs only. Use an isolated output directory; no application store is opened.
var output = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Supply an output directory, then optional Ollama model and base URL.");
var model = args.Length > 1 ? args[1] : "qwen3.5:0.8b";
var endpoint = args.Length > 2 ? args[2] : "http://127.0.0.1:11434";
if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback) throw new ArgumentException("Evaluation requires a local Ollama endpoint.");
Directory.CreateDirectory(output);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var identity = await http.GetFromJsonAsync<JsonElement>(endpoint + "/api/tags");
var digest = identity.GetProperty("models").EnumerateArray().FirstOrDefault(item => item.GetProperty("name").GetString() == model);
var provider = new OllamaResumeCompositionProvider(http, Options.Create(new OllamaOptions
{
    CompositionEnabled = true, Model = model, BaseUrl = endpoint, NumCtx = 4096
}), proseStore: new ReusableProseStore(Path.Combine(output, "cache")));
var validator = new CompositionValidator();
var composer = new ResumeCompositionOrchestrator([provider], validator);
(string Id, string Text, string Skill)[] fixtures =
[
    ("assisted", "Helped the team add a C# retry queue to the billing service; its effect was not yet measured.", "C#"),
    ("leadership", "Led the API team and reviewed PostgreSQL recovery designs before the production release.", "PostgreSQL"),
    ("prototype", "Built a Python prototype for retrieval experiments; it was not deployed to production.", "Python"),
    ("intended", "Designed an OpenTelemetry tracing plan intended to reduce incident investigation time; the outcome remained unknown.", "OpenTelemetry"),
    ("qualitative", "Maintained C# background workers and simplified recovery procedures for the operations team.", "C#"),
    ("measured", "Implemented PostgreSQL query changes that reduced the measured test latency from 240 ms to 180 ms.", "PostgreSQL")
];
var synthesis = args.Contains("--synthesis");
if (synthesis) fixtures =
[
    ("retry-synthesis", "Built C# billing retry queues with durable PostgreSQL storage. Added billing retry audit trails so the operations team could inspect failed attempts. The effect on recovery time was not measured.", "C#"),
    ("evaluation-synthesis", "Led the API team and reviewed PostgreSQL recovery designs before the production release. Introduced recovery drills using recorded failure cases; the drills ran in a test environment and did not measure production availability.", "PostgreSQL")
];
var totalCases = fixtures.Length * 6;
var rows = new List<object>();
var cases = new List<(ProjectionManifest Manifest, string Brief)>();
foreach (var fixture in fixtures)
foreach (var tone in Enum.GetValues<ResumeTone>())
foreach (var detail in new[] { ResumeDetail.Brief, ResumeDetail.Thorough })
{
    var facts = synthesis ? fixture.Text.Split(". ", StringSplitOptions.RemoveEmptyEntries).Select(text => text.TrimEnd('.') + ".").ToList() : new List<string> { fixture.Text };
    var selected = facts.Select((fact, index) => new SelectedClaim(new JobMlClaim
    { Id = fixture.Id + "-" + index, Subject = "synthetic-role", Statement = fact, Concepts = new() { Skills = [fixture.Skill] } },
        "Synthetic role", fixture.Text, ["source-" + fixture.Id + "-" + index], 1, [])).ToList();
    var packet = new EvidencePacket("#role:p1", "Synthetic role", $"Synthesize the selected facts into one natural resume paragraph. Use {tone} expression and {detail} detail. Preserve the candidate's action, object, actual ownership, status, uncertainty and named skills. Omit unsupported results. Never upgrade helped to led or intended to achieved.",
        detail == ResumeDetail.Brief ? 65 : 100, selected, [], "experience");
    var manifest = new ProjectionManifest("synthetic-v1", "synthetic-job-v1", DateTimeOffset.UtcNow, [], [packet], [], [], "none");
    var brief = fixture.Id is "prototype" or "intended" or "leadership" ? "Applied AI engineering: evaluation, safe actions and observability." : "Backend engineering: reliable APIs, database queries and recovery.";
    cases.Add((manifest, brief));
    var timer = Stopwatch.StartNew();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var result = await composer.ComposeAsync(manifest, brief, new() { ComposeProse = true, CompositionProvider = "ollama" }, timeout.Token);
    var original = new CompositionBlock(packet.SectionId, fixture.Text, selected.Select(item => item.Claim.Id).ToList(), selected.SelectMany(item => item.EvidenceIds).ToList());
    var hardErrors = validator.Validate(new(result.Blocks, []), [original], manifest);
    rows.Add(new { fixture = fixture.Id, tone = tone.ToString(), detail = detail.ToString(), source = fixture.Text,
        text = result.Blocks.Single().Text, result.Used, result.Warnings, elapsed_ms = timer.ElapsedMilliseconds,
        unchanged_source = result.Blocks.Single().Text == fixture.Text, hard_errors = hardErrors });
    await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"{rows.Count}/{totalCases} {fixture.Id} {tone} {detail}: {(result.Used ? "edited" : "source retained")}; {timer.ElapsedMilliseconds} ms; {hardErrors.Count} hard errors");
}
var cacheMessages = new List<string>();
var progress = new InlineProgress(cacheMessages);
var repeatTimer = Stopwatch.StartNew();
await composer.ComposeAsync(cases[0].Manifest, cases[0].Brief, new() { ComposeProse = true, CompositionProvider = "ollama" }, progress: progress);
await File.WriteAllTextAsync(Path.Combine(output, "run.json"), JsonSerializer.Serialize(new
{
    model, endpoint, model_metadata = digest, cases = rows.Count, repeated_case_ms = repeatTimer.ElapsedMilliseconds,
    repeat_progress = cacheMessages, evaluation = "Automated regression only; human clarity/naturalness/tone ratings remain uncollected. No NLI model is enabled."
}, new JsonSerializerOptions { WriteIndented = true }));

sealed class InlineProgress(List<string> messages) : IProgress<string>
{
    public void Report(string value) => messages.Add(value);
}
