using lucidRESUME.AI;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Matching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI.Tests;

/// <summary>
/// End-to-end tests using real ONNX embeddings, real SQLite store,
/// and real matching logic - no mocks, no external services.
/// </summary>
public class RealDataIntegrationTests : IDisposable
{
    private readonly OnnxEmbeddingService? _embedder;
    private readonly string _dbPath;
    private readonly SqliteAppStore _store;

    public RealDataIntegrationTests()
    {
        if (HasLocalEmbeddingModel())
        {
            var opts = Options.Create(new EmbeddingOptions
            {
                OnnxModelPath = Path.Combine(AppContext.BaseDirectory, "models", "all-MiniLM-L6-v2.onnx"),
                VocabPath = Path.Combine(AppContext.BaseDirectory, "models", "vocab.txt")
            });
            _embedder = new OnnxEmbeddingService(opts, NullLogger<OnnxEmbeddingService>.Instance);
        }

        _dbPath = Path.Combine(Path.GetTempPath(), $"lucidresume_integ_{Guid.NewGuid():N}.db");
        _store = new SqliteAppStore(_dbPath);
    }

    public void Dispose()
    {
        _embedder?.Dispose();
        _store.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task SemanticTermNormalizer_MatchesRelatedSkills()
    {
        if (_embedder is null) return;

        var normalizer = new SemanticTermNormalizer(_embedder);

        var jobSkills = new List<string>
        {
            "C#", ".NET Core", "Azure", "SQL Server", "Docker",
            "Kubernetes", "REST API", "Agile", "CI/CD", "Git"
        };
        var resumeSkills = new List<string>
        {
            "C Sharp", ".NET 8", "Microsoft Azure", "MSSQL",
            "Docker containers", "K8s", "RESTful services",
            "Scrum", "GitHub Actions", "Git version control"
        };

        var matches = await normalizer.FindMatchesAsync(jobSkills, resumeSkills, 0.55f);

        var matched = matches.Where(m => m.MatchedSourceTerm is not null).ToList();
        Assert.True(matched.Count >= 5,
            $"Expected at least 5 semantic matches but got {matched.Count}: " +
            string.Join(", ", matches.Select(m => $"{m.TargetTerm}->{m.MatchedSourceTerm ?? "NONE"}({m.Similarity:F2})")));
    }

    [Fact]
    public async Task CoverageAnalyser_WithOnnxEmbeddings_FindsSemanticMatches()
    {
        if (_embedder is null) return;

        var classifier = new CompanyClassifier(Options.Create(new CompanyClassifierOptions()));
        var coverageOpts = Options.Create(new CoverageOptions
        {
            SkillSemanticThreshold = 0.60f,
            ResponsibilitySemanticThreshold = 0.55f
        });
        var analyser = new ResumeCoverageAnalyser(classifier, coverageOpts, _embedder);

        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 100);
        resume.Skills.Add(new Skill { Name = "C#" });
        resume.Skills.Add(new Skill { Name = ".NET 8" });
        resume.Skills.Add(new Skill { Name = "Microsoft Azure" });
        resume.Skills.Add(new Skill { Name = "SQL Server" });
        resume.Skills.Add(new Skill { Name = "Docker" });
        resume.Skills.Add(new Skill { Name = "REST APIs" });
        resume.Experience.Add(new WorkExperience
        {
            Company = "Acme Corp",
            Title = "Senior Developer",
            Achievements = ["Built microservices architecture using .NET and Docker",
                            "Led migration of monolith to cloud-native on Azure",
                            "Designed RESTful APIs handling 10k requests per second"]
        });

        var job = JobDescription.Create("Senior .NET Developer", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = ["C Sharp", ".NET Core", "Azure cloud", "Transact-SQL"];
        job.PreferredSkills = ["Containerization", "API design"];
        job.Responsibilities = ["Develop microservices-based applications",
                                "Design and maintain RESTful API endpoints"];

        var report = await analyser.AnalyseAsync(resume, job);

        var covered = report.Entries.Where(e => e.Evidence is not null).ToList();
        var gaps = report.Entries.Where(e => e.Evidence is null).ToList();

        Assert.True(covered.Count >= 4,
            $"Expected at least 4 covered entries but got {covered.Count}/{report.Entries.Count}. " +
            $"Gaps: {string.Join(", ", gaps.Select(g => g.Requirement.Text))}. " +
            $"Matches: {string.Join(", ", covered.Select(c => $"{c.Requirement.Text}->{c.Evidence}({c.Score:F2})"))}");
    }

    [Fact]
    public async Task SkillMatchingService_WithOnnxEmbeddings_ScoresRealisticResume()
    {
        if (_embedder is null) return;

        var classifier = new CompanyClassifier(Options.Create(new CompanyClassifierOptions()));
        var extractor = new AspectExtractor(classifier);
        var service = new SkillMatchingService(extractor, _embedder);

        var resume = ResumeDocument.Create("test.pdf", "application/pdf", 100);
        resume.Skills.Add(new Skill { Name = "Python" });
        resume.Skills.Add(new Skill { Name = "Machine Learning" });
        resume.Skills.Add(new Skill { Name = "TensorFlow" });
        resume.Skills.Add(new Skill { Name = "AWS" });
        resume.Skills.Add(new Skill { Name = "SQL" });

        var job = JobDescription.Create("ML Engineer", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = ["Python", "Deep Learning", "AWS", "SQL"];
        job.PreferredSkills = ["PyTorch or TensorFlow"];

        var profile = new Core.Models.Profile.UserProfile();
        var result = await service.MatchAsync(resume, job, profile);

        Assert.True(result.Score >= 0.4,
            $"Expected score >= 0.4 but got {result.Score:F2}. " +
            $"Matched: {string.Join(", ", result.MatchedSkills)}. " +
            $"Missing: {string.Join(", ", result.MissingSkills)}");
    }

    [Fact]
    public async Task SqliteStore_PersistsAndReloads_FullResumeWithSkills()
    {
        var state = new AppState();
        state.Resume = ResumeDocument.Create("dev-resume.pdf", "application/pdf", 50000);
        state.Resume.Skills.Add(new Skill { Name = "C#" });
        state.Resume.Skills.Add(new Skill { Name = ".NET" });
        state.Resume.Skills.Add(new Skill { Name = "Azure" });
        state.Resume.Experience.Add(new WorkExperience
        {
            Company = "BigCorp",
            Title = "Senior Developer",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2024, 6, 1),
            Achievements = ["Led team of 5", "Built API platform"]
        });
        state.Resume.Education.Add(new Education
        {
            Institution = "MIT",
            Degree = "BS Computer Science",
            EndDate = new DateOnly(2019, 6, 1)
        });
        state.Resume.Personal.FullName = "Jane Developer";
        state.Resume.Personal.Email = "jane@example.com";

        var job1 = JobDescription.Create("Senior .NET Dev at StartupCo", new JobSource { Type = JobSourceType.PastedText });
        job1.RequiredSkills = ["C#", ".NET", "Azure"];
        state.Jobs.Add(job1);

        var job2 = JobDescription.Create("Lead Engineer at BigTech", new JobSource { Type = JobSourceType.Url });
        job2.RequiredSkills = ["Python", "ML", "AWS"];
        state.Jobs.Add(job2);

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync();

        Assert.NotNull(loaded.Resume);
        Assert.Equal("dev-resume.pdf", loaded.Resume!.FileName);
        Assert.Equal("Jane Developer", loaded.Resume.Personal.FullName);
        Assert.Equal("jane@example.com", loaded.Resume.Personal.Email);
        Assert.Equal(3, loaded.Resume.Skills.Count);
        Assert.Single(loaded.Resume.Experience);
        Assert.Equal("Led team of 5", loaded.Resume.Experience[0].Achievements[0]);
        Assert.Single(loaded.Resume.Education);
        Assert.Equal(2, loaded.Jobs.Count);
        Assert.Equal(3, loaded.Jobs[0].RequiredSkills.Count);

        using var ms = new MemoryStream();
        await _store.ExportJsonAsync(ms);
        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd();
        Assert.Contains("Jane Developer", json);
        Assert.Contains("jane@example.com", json);
        Assert.Contains("Senior .NET Dev at StartupCo", json);
    }

    [Fact]
    public async Task SqliteStore_ConcurrentMutations_DontCorrupt()
    {
        await _store.SaveAsync(new AppState());

        var tasks = Enumerable.Range(0, 20).Select(i =>
            _store.MutateAsync(s =>
            {
                var job = JobDescription.Create($"Job {i}", new JobSource { Type = JobSourceType.PastedText });
                s.Jobs.Add(job);
            }));

        await Task.WhenAll(tasks);

        var loaded = await _store.LoadAsync();
        Assert.Equal(20, loaded.Jobs.Count);
    }

    [Fact]
    public async Task EmbeddingPerformance_BatchOf50Skills_ReasonableTime()
    {
        if (_embedder is null) return;

        var skills = new[]
        {
            "C#", ".NET Core", "ASP.NET", "Entity Framework", "SQL Server",
            "Azure", "Docker", "Kubernetes", "Redis", "RabbitMQ",
            "JavaScript", "TypeScript", "React", "Angular", "Node.js",
            "Python", "Django", "FastAPI", "PostgreSQL", "MongoDB",
            "AWS", "Lambda", "S3", "DynamoDB", "CloudFormation",
            "Git", "GitHub Actions", "Jenkins", "Terraform", "Ansible",
            "REST APIs", "GraphQL", "gRPC", "WebSockets", "OAuth",
            "Microservices", "Event-driven architecture", "CQRS", "Domain-driven design", "Clean architecture",
            "Unit testing", "Integration testing", "xUnit", "NUnit", "Selenium",
            "Agile", "Scrum", "Kanban", "CI/CD", "DevOps"
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var vectors = new float[skills.Length][];
        for (int i = 0; i < skills.Length; i++)
            vectors[i] = await _embedder.EmbedAsync(skills[i]);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 10000,
            $"Embedding 50 skills took {sw.ElapsedMilliseconds}ms, expected < 10000ms");

        foreach (var v in vectors)
        {
            Assert.Equal(384, v.Length);
            float mag = 0;
            foreach (var x in v) mag += x * x;
            Assert.InRange(MathF.Sqrt(mag), 0.99f, 1.01f);
        }

        // Second pass should be instant (cached)
        sw.Restart();
        for (int i = 0; i < skills.Length; i++)
        {
            var cached = await _embedder.EmbedAsync(skills[i]);
            Assert.Same(vectors[i], cached);
        }
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 50,
            $"Cached embedding lookup took {sw.ElapsedMilliseconds}ms, expected < 50ms");
    }

    private static bool HasLocalEmbeddingModel() =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "models", "all-MiniLM-L6-v2.onnx")) &&
        File.Exists(Path.Combine(AppContext.BaseDirectory, "models", "vocab.txt"));
}
