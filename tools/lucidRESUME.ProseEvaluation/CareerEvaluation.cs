using System.Diagnostics;
using System.Text.Json;
using lucidRESUME.AI;
using lucidRESUME.Compiler;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Matching;
using lucidRESUME.JobML;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class CareerEvaluation
{
    public static async Task RunAsync(string output, ResumeCompositionOrchestrator composer)
    {
        var source = ResumeDocument.Create("synthetic-career.md", "text/markdown", 0);
        source.Personal.FullName = "Alex Example";
        source.Personal.Summary = "Hands-on software engineer building reliable C# services and internal AI agent runtimes.";
        source.Experience =
        [
            new() { Title = "Applied AI Engineer", Company = "Example Operations", StartDate = new(2025, 1, 1), IsCurrent = true,
                Technologies = ["C#", "Azure OpenAI", "Qdrant", "PostgreSQL"], Achievements =
                ["Built a C# support-agent runtime using Azure OpenAI and Qdrant retrieval for internal operations.",
                 "Added per-tool authorization and idempotency keys before allowing the internal agent to update ticket state.",
                 "Curated offline evaluation cases from failed tool calls; the production effect remains unknown.",
                 "Designed PostgreSQL reporting queries and database backup recovery drills."] },
            new() { Title = "Senior Software Engineer", Company = "Example Commerce", StartDate = new(2018, 1, 1), EndDate = new(2024, 12, 31),
                Technologies = ["C#", "PostgreSQL"], Achievements =
                ["Optimised PostgreSQL queries: measured test latency fell from 240 ms to 180 ms.",
                 "Built C# billing retry queues and audit trails so the operations team could inspect failed attempts.",
                 "Led the API team and reviewed PostgreSQL recovery designs before the production release.",
                 "Implemented accessible keyboard navigation for the operations administration console."] },
            new() { Title = "Research Developer", Company = "Example Lab", StartDate = new(2015, 1, 1), EndDate = new(2017, 12, 31),
                Technologies = ["Python", "FastAPI", "PyTorch"], Achievements =
                ["Built a Python/FastAPI document-classification prototype using PyTorch; it was never deployed to production."] }
        ];
        var jobs = new[]
        {
            (Id: "applied-ai", Job: new JobDescription { Title = "Head of Applied AI Engineering", Company = "Synthetic AI Employer", RequiredYearsExperience = 10,
                RequiredSkills = ["C#", "Azure OpenAI", "Qdrant"],
                Responsibilities = ["Own agent tool authorization, idempotency and state transitions.", "Build offline evaluation cases from failed tool calls.", "Lead a small engineering team and personally review architecture."],
                RawText = "Hands-on applied AI leadership. Own agent architecture, retrieval, authorization, idempotency and tool execution. Build evaluation datasets from failed tool calls. Lead a small engineering team and personally review architecture. Production learning and safe state changes matter." }),
            (Id: "database-platform", Job: new JobDescription { Title = "Database Platform Engineer", Company = "Synthetic Platform Employer", RequiredYearsExperience = 8,
                RequiredSkills = ["C#", "PostgreSQL"],
                Responsibilities = ["Optimise PostgreSQL queries and test latency.", "Design database backups and recovery drills."],
                RawText = "Design PostgreSQL reporting queries, database backups and recovery drills. Optimise measured test latency. Build reliable C# services. Explain performance evidence and operational recovery." })
        };
        var embedder = new LexicalOnlyEmbedder();
        using var store = new SqliteAppStore(Path.Combine(output, "synthetic-career.db"), proseEnricher: new CareerProseAnalyser(embedder));
        await store.SaveAsync(new AppState { Resumes = [source] });
        source = (await store.LoadAsync()).CompleteCareer!.Transcript;
        var fullText = new SearchRecorder(store);
        var taxonomy = new SkillTaxonomyService(embedder);
        var options = Options.Create(new TailoringOptions { ComposeProse = true, Provider = "ollama" });
        var compressor = new SemanticCompressor(new SkillLedgerBuilder(embedder, taxonomy), new JdSkillLedgerBuilder(embedder),
            new SkillLedgerMatcher(embedder), embedder, taxonomy, options, NullLogger<SemanticCompressor>.Instance,
            new ResumeProjectionEditor(composer, options), fullText);
        var originalRoles = JsonSerializer.Serialize(source.Experience);
        var rows = new List<object>();
        var selectedByJob = new Dictionary<string, HashSet<string>>();
        foreach (var (id, job) in jobs)
        {
            fullText.Reset();
            var timer = Stopwatch.StartNew();
            var progress = new List<string>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var result = await compressor.CompressAsync(source, job, timeout.Token, new InlineProgress(progress),
                generationOptions: new() { Length = ResumeLength.Standard, Detail = ResumeDetail.Thorough, Tone = ResumeTone.Direct });
            var findings = new List<string>();
            if (!progress.Any(message => message.Contains("local model") || message.Contains("Reusing")))
                findings.Add("No local prose model call or cached composition was observed.");
            var index = MarkdownEvidenceIndex.Create(result.Markdown);
            var ledger = EvidenceLedgerBuilder.EnsureCurrent(source);
            foreach (var binding in result.Projection.Projection!.Blocks)
            {
                if (!index.TryGet(binding.ProseRef, out _)) findings.Add("Broken prose binding: " + binding.ProseRef);
                if (!ledger.Claims.Any(claim => claim.Id == binding.ClaimId && claim.Review == "accepted"))
                    findings.Add("Unaccepted or missing claim: " + binding.ClaimId);
            }
            foreach (var role in result.Projection.Experience)
            {
                var original = source.Experience.Single(item => item.Id == role.Id);
                var permittedSkills = original.Technologies.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var foreignSkill in source.Experience.SelectMany(item => item.Technologies).Distinct().Where(skill => !permittedSkills.Contains(skill)))
                    if (role.Achievements.Any(text => text.Contains(foreignSkill, StringComparison.OrdinalIgnoreCase)))
                        findings.Add($"Cross-role technology transfer into {role.Company}: {foreignSkill}");
            }
            if (JsonSerializer.Serialize(source.Experience) != originalRoles) findings.Add("The source career was mutated.");
            if (id == "applied-ai" && (!result.Markdown.Contains("Azure OpenAI") || !result.Markdown.Contains("idempotency")))
                findings.Add("AI projection lost core supported requirements.");
            if (id == "database-platform" && (!result.Markdown.Contains("PostgreSQL") || !result.Markdown.Contains("recovery drills")))
                findings.Add("Database projection lost core supported requirements.");
            if (id == "database-platform" && (result.Projection.Personal.Summary?.Contains("AI agent", StringComparison.OrdinalIgnoreCase) == true ||
                result.Projection.Personal.Summary?.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) != true))
                findings.Add("Database summary reused the off-target personal introduction or lost database evidence.");
            var allowedGlobalSkills = job.RequiredSkills.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var skill in result.Projection.Skills.Where(skill => !allowedGlobalSkills.Contains(skill.Name)))
                findings.Add("Incidental role tool reached the global skills list: " + skill.Name);
            selectedByJob[id] = result.Projection.Projection.Blocks.Where(binding => binding.ProseRef.StartsWith("#experience-"))
                .Select(binding => binding.ClaimId).ToHashSet();
            await File.WriteAllTextAsync(Path.Combine(output, $"career-{id}.md"), result.Markdown);
            await File.WriteAllTextAsync(Path.Combine(output, $"career-{id}.json"), JsonSerializer.Serialize(result.Projection,
                new JsonSerializerOptions { WriteIndented = true }));
            rows.Add(new { job = id, elapsed_ms = timer.ElapsedMilliseconds, selected_claims = selectedByJob[id].Order().ToArray(),
                provider = "ollama", native_queries = fullText.Queries, native_hits = fullText.Hits,
                required_years = job.RequiredYearsExperience, role_detail = result.Projection.Experience.Select(role => new { role.Title, role.IsCompact, bullets = role.Achievements.Count }), result.Projection.GenerationWarnings, progress, quality_findings = findings });
            await File.WriteAllTextAsync(Path.Combine(output, "career-results.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Career {id}: {timer.ElapsedMilliseconds} ms; {findings.Count} quality findings");
        }
        var distinctSelection = !selectedByJob[jobs[0].Id].SetEquals(selectedByJob[jobs[1].Id]);
        await File.WriteAllTextAsync(Path.Combine(output, "career-run.json"), JsonSerializer.Serialize(new
        {
            embedding_mode = "lexical fallback; no vector model or skill proficiency inference",
            retrieval = "SeekStorm BM25F with exact role facets; canonical career only; lexical fallback (no vector model)",
            jobs_select_different_evidence = distinctSelection,
            validator_policy = CompositionValidator.PolicyVersion
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!distinctSelection) throw new InvalidDataException("Contrasting jobs selected identical role evidence.");
    }

    private sealed class SearchRecorder(IProseSearch search) : IProseSearch
    {
        public int Queries { get; private set; }
        public int Hits { get; private set; }
        public void Reset() { Queries = 0; Hits = 0; }
        public Task PrepareAsync(CareerProseIndex index, CancellationToken ct = default) => search.PrepareAsync(index, ct);
        public Task<IReadOnlyList<ProseSearchHit>> SearchAsync(string revision, string query, int maximum = 64, CancellationToken ct = default)
            => SearchAsync(new ProseSearchQuery(revision, query, Maximum: maximum), ct);
        public async Task<IReadOnlyList<ProseSearchHit>> SearchAsync(ProseSearchQuery query, CancellationToken ct = default)
        {
            Queries++;
            var hits = await search.SearchAsync(query, ct);
            Hits += hits.Count;
            return hits;
        }
    }

    private sealed class LexicalOnlyEmbedder : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
        public float CosineSimilarity(float[] a, float[] b) => 0;
    }
}
