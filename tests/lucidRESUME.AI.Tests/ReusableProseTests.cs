using System.Net;
using System.Text;
using System.Text.Json;
using lucidRESUME.Compiler;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

public sealed class ReusableProseTests
{
    [Fact]
    public async Task Polished_accounts_survive_restart_and_job_change_but_not_changed_skills_facts_or_model()
    {
        var path = Path.Combine(Path.GetTempPath(), "lucid-prose-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new EchoHandler();
            var options = Options.Create(new OllamaOptions { CompositionEnabled = true, Model = "example:latest" });
            var claim = new JobMlClaim
            {
                Id = "claim",
                Subject = "experience:role",
                Statement = "Built C# APIs.",
                Concepts = new() { Skills = ["C#"] }
            };
            var packet = new EvidencePacket("role", "Engineer", "Edit precisely", 40,
                [new SelectedClaim(claim, "Engineer", claim.Statement, ["e1"], 1, [])], [], "experience");
            var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow, [], [packet], [], [], "test");
            var block = new CompositionBlock("role", claim.Statement, ["claim"], ["e1"]);
            var request = new CompositionPassRequest(CompositionPass.Tighten, "First job", manifest, [block], [block]);
            OllamaResumeCompositionProvider Provider() => new(new HttpClient(handler), options, proseStore: new ReusableProseStore(path));
            await Provider().RunPassAsync(request);
            await Provider().RunPassAsync(request with { JobDescription = "Another job" });
            Assert.Equal(1, handler.ChatCalls);
            var saved = JsonSerializer.Deserialize<ReusableProseAccount>(await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(path))))!;
            Assert.Contains("C#", saved.Skills);
            Assert.Contains("e1", saved.EvidenceIds);
            Assert.Contains("experience:role", saved.SubjectIds);
            Assert.Equal("Built C# APIs.", saved.SourceText);
            Assert.Empty(saved.JobBrief);

            claim.Concepts.Skills.Add("SQL");
            await Provider().RunPassAsync(request);
            Assert.Equal(2, handler.ChatCalls);
            var changed = block with { Text = "Built C# APIs for billing." };
            await Provider().RunPassAsync(request with { SourceBlocks = [changed], CurrentBlocks = [changed] });
            Assert.Equal(3, handler.ChatCalls);
            handler.Digest = "updated-model";
            await Provider().RunPassAsync(request);
            Assert.Equal(4, handler.ChatCalls);

            await Provider().RunPassAsync(request with { Pass = CompositionPass.HumanVoice });
            await Provider().RunPassAsync(request with { Pass = CompositionPass.HumanVoice, JobDescription = "Another job" });
            Assert.Equal(6, handler.ChatCalls);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task Rejected_wording_is_not_saved_as_prose_or_regenerated_for_unchanged_inputs()
    {
        var path = Path.Combine(Path.GetTempPath(), "lucid-rejected-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new EchoHandler { Rewrite = "Led 99 engineers." };
            var options = Options.Create(new OllamaOptions { CompositionEnabled = true, Model = "example:latest" });
            var claim = new JobMlClaim { Id = "claim", Subject = "role", Statement = "Built C# APIs." };
            var packet = new EvidencePacket("role", "Engineer", "Edit", 40,
                [new SelectedClaim(claim, "Engineer", claim.Statement, ["e1"], 1, [])], [], "experience");
            var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow, [], [packet], [], [], "test");
            var block = new CompositionBlock("role", claim.Statement, ["claim"], ["e1"]);
            var request = new CompositionPassRequest(CompositionPass.Tighten, "Job", manifest, [block], [block]);
            var provider = new OllamaResumeCompositionProvider(new HttpClient(handler), options, proseStore: new ReusableProseStore(path));
            await provider.RunPassAsync(request);
            var replay = await provider.RunPassAsync(request);
            Assert.Equal(1, handler.ChatCalls);
            Assert.Equal(block.Text, Assert.Single(replay.Blocks).Text);
            Assert.Contains(replay.Warnings, warning => warning.Contains("numeric fact"));
            var account = JsonSerializer.Deserialize<ReusableProseAccount>(await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(path))))!;
            Assert.True(account.RetainedSource);
            Assert.Equal(block.Text, account.Text);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task Openai_reuses_job_neutral_prose_and_sends_the_job_brief_once_for_tailoring()
    {
        var path = Path.Combine(Path.GetTempPath(), "lucid-cloud-prose-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new OpenAiEchoHandler();
            var claim = new JobMlClaim { Id = "claim", Subject = "experience:role", Statement = "Built C# APIs.", Concepts = new() { Skills = ["C#"] } };
            var packet = new EvidencePacket("role", "Engineer", "Edit", 40,
                [new SelectedClaim(claim, "Engineer", claim.Statement, ["e1"], 1, [])], ["r1"], "experience");
            var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow,
                [new CompilerRequirement("r1", "C#", RequirementKind.Required, "C#")], [packet], [], [], "test");
            var block = new CompositionBlock("role", claim.Statement, ["claim"], ["e1"]);
            var request = new CompositionPassRequest(CompositionPass.Tighten, "First brief", manifest, [block], [block]);
            var provider = new OpenAiResumeCompositionProvider(new HttpClient(handler),
                Options.Create(new OpenAiOptions { ApiKey = "test-only", BaseUrl = "https://example.test/v1", Model = "test-model" }),
                new ReusableProseStore(path));
            await provider.RunPassAsync(request);
            await provider.RunPassAsync(request with { JobDescription = "Another brief" });
            Assert.Equal(1, handler.Calls);
            Assert.DoesNotContain("First brief", handler.LastInput);
            await provider.RunPassAsync(request with { Pass = CompositionPass.HumanVoice });
            Assert.Equal(2, handler.Calls);
            Assert.Contains("First brief", handler.LastInput);
            using var input = JsonDocument.Parse(handler.LastInput);
            Assert.Equal("First brief", input.RootElement.GetProperty("target_role").GetString());
            Assert.Empty(input.RootElement.GetProperty("current_draft").EnumerateArray());
            Assert.False(input.RootElement.GetProperty("section_briefs")[0].TryGetProperty("allowed_emphasis", out _));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    private sealed class OpenAiEchoHandler : HttpMessageHandler
    {
        public int Calls;
        public string LastInput = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            LastInput = payload.RootElement.GetProperty("input").GetString()!;
            using var input = JsonDocument.Parse(LastInput);
            var blocks = input.RootElement.GetProperty("immutable_human_sources");
            var output = JsonSerializer.Serialize(new { Blocks = blocks, Warnings = Array.Empty<string>() });
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { output_text = output }), Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public void Editing_brief_retains_requirements_without_repeating_the_entire_advert()
    {
        var job = new lucidRESUME.Core.Models.Jobs.JobDescription
        { Title = "AI Engineer", RequiredSkills = ["Evaluation", "Agent safety"], RawText = new string('x', 20_000) };
        var brief = ResumeProjectionEditor.BuildJobBrief(job);
        Assert.Contains("Evaluation", brief);
        Assert.Contains("Agent safety", brief);
        Assert.True(brief.Length < 3100);
        Assert.DoesNotContain(job.RawText, brief);
    }

    private sealed class EchoHandler : HttpMessageHandler
    {
        public int ChatCalls;
        public string? Rewrite;
        public string Digest = "original-model";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object body;
            if (request.Method == HttpMethod.Get)
                body = new { models = new[] { new { name = "example:latest", digest = Digest } } };
            else
            {
                ChatCalls++;
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                using var prompt = JsonDocument.Parse(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
                var sections = prompt.RootElement.GetProperty("sections").EnumerateArray().Select(section => new
                { sectionId = section.GetProperty("sectionId").GetString(), text = Rewrite ?? section.GetProperty("immutable_human_source").GetString() }).ToArray();
                body = new { message = new { content = JsonSerializer.Serialize(new { sections }) } };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        }
    }
}
