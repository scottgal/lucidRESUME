using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Export;
using lucidRESUME.JobML;
using lucidRESUME.Matching;

namespace lucidRESUME.Core.Tests;

public sealed class CareerRecordJobMlBuilderTests
{
    [Fact]
    public async Task Literal_link_refresh_preserves_prose_review_decisions_and_source_links()
    {
        var resume = ResumeDocument.Create("original.md", "text/markdown", 0);
        resume.Personal.FullName = "Author";
        resume.Experience.Add(new() { Company = "Example", Title = "Engineer", Achievements =
            ["Delivered Azure Functions and SignalR.", "Evaluated WPF for a prototype."] });
        var embedder = new DeterministicEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var builder = new CareerRecordJobMlBuilder(new SkillLedgerBuilder(embedder, taxonomy), taxonomy, embedder);
        var file = await builder.BuildAsync(resume);
        var accepted = file.Data.Claims.Single(claim => claim.Statement == "Delivered Azure Functions and SignalR.");
        accepted.Concepts!.Skills.Clear();
        var rejected = file.Data.Claims.Single(claim => claim.Statement == "Evaluated WPF for a prototype.");
        rejected.Review = "rejected";
        rejected.Concepts!.Skills.Clear();
        file = new(file.Markdown + "\n\nCustom author prose.", file.Data);
        resume.JobMlSource = new JobMlParser().Serialize(file);
        Assert.True(CareerRecordJobMlBuilder.RefreshLiteralConceptLinks(resume));
        var refreshed = new JobMlParser().Parse(resume.JobMlSource);
        Assert.Equal(file.Markdown, refreshed.Markdown);
        Assert.Equal(accepted.Evidence.Select(item => item.Id), refreshed.Data.Claims.Single(item => item.Id == accepted.Id).Evidence.Select(item => item.Id));
        Assert.NotEmpty(refreshed.Data.Claims.Single(item => item.Id == accepted.Id).Concepts!.Skills);
        var untouched = refreshed.Data.Claims.Single(item => item.Id == rejected.Id);
        Assert.Equal("rejected", untouched.Review);
        Assert.Empty(untouched.Concepts!.Skills);
        Assert.False(CareerRecordJobMlBuilder.RefreshLiteralConceptLinks(resume));
        Assert.DoesNotContain(JobMlProcessor.Validate(refreshed), item => item.Severity == JobMlDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Author_edits_with_multiple_labels_for_one_source_keep_one_catalogue_identity()
    {
        var resume = ResumeDocument.Create("imported.docx", "text/markdown", 0);
        resume.Personal.FullName = "Author";
        resume.Personal.Summary = "Writes production software.";
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(resume);
        ledger.Evidence[0].SourceName = "Author-reviewed career prose";
        var embedder = new DeterministicEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var builder = new CareerRecordJobMlBuilder(new SkillLedgerBuilder(embedder, taxonomy), taxonomy, embedder);
        var file = await builder.BuildAsync(resume);
        var source = Assert.Single(file.Data.Sources);
        Assert.Contains("Author-reviewed career prose", source.Name);
        Assert.Contains("imported.docx", source.Name);
        Assert.All(file.Data.Claims.SelectMany(claim => claim.Evidence).Where(evidence => evidence.SourceId is not null),
            evidence => Assert.Equal(source.Id, evidence.SourceId));
        Assert.DoesNotContain(JobMlProcessor.Validate(file), item => item.Severity == JobMlDiagnosticSeverity.Error);
        Assert.Equal("Author-reviewed career prose", ledger.Evidence[0].SourceName);
    }
    [Fact]
    public void Provider_identity_guid_is_stable_and_case_insensitive()
    {
        Assert.Equal(
            EvidenceLedgerBuilder.StableGuid("github:https://github.com/scottgal/lucidresume"),
            EvidenceLedgerBuilder.StableGuid(" GITHUB:https://github.com/ScottGal/lucidRESUME "));
        Assert.NotEqual(
            EvidenceLedgerBuilder.StableGuid("github:https://github.com/scottgal/lucidresume"),
            EvidenceLedgerBuilder.StableGuid("github:https://github.com/scottgal/lucidrag"));
    }

    [Fact]
    public async Task Career_record_round_trips_with_sources_vectors_and_role_centroids()
    {
        var resume = ResumeDocument.Create("source.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 123);
        resume.Personal.FullName = "Jane Smith";
        resume.Personal.Email = "jane@example.com";
        resume.Personal.Phone = "+44 1234 567890";
        resume.Personal.ContactPreference = "please email in the first instance";
        resume.Experience.Add(new WorkExperience
        {
            Company = "Example Ltd",
            Title = "Head of Engineering",
            StartDate = new DateOnly(2020, 1, 1),
            IsCurrent = true,
            Achievements = ["Led a TypeScript engineering team through platform change on AWS."],
            Technologies = ["TypeScript", "AWS"]
        });
        resume.Projects.Add(new Project
        {
            Name = "Evidence compiler",
            Description = "Built a provenance-aware career evidence compiler.\n\nA second paragraph remains evidence, not an entity heading.",
            Technologies = ["C#"],
            Url = "https://github.com/example/evidence-compiler",
            EvidenceMetadata = new Dictionary<string, string>
            {
                ["provider"] = "github",
                ["repository_class"] = "sustained-product",
                ["revision"] = new string('a', 40),
                ["has_tests"] = "true"
            }
        });
        resume.Projects.Add(new Project
        {
            Name = "Observed upstream fork",
            Description = "An imported fork observation, not reviewed personal project evidence.",
            Url = "https://github.com/example/upstream-fork",
            EvidenceMetadata = new Dictionary<string, string>
            {
                ["provider"] = "github",
                ["fork"] = "true",
                ["eligible_for_personal_evidence"] = "false"
            }
        });
        resume.Skills.AddRange([new Skill { Name = "TypeScript" }, new Skill { Name = "AWS" }]);
        var embedder = new DeterministicEmbedder();
        var taxonomy = new SkillTaxonomyService(embedder);
        var builder = new CareerRecordJobMlBuilder(new SkillLedgerBuilder(embedder, taxonomy), taxonomy, embedder);

        var record = await builder.BuildAsync(resume);
        var serialized = new JobMlParser().Serialize(record);
        var reparsed = new JobMlParser().Parse(serialized);
        var errors = JobMlProcessor.Validate(reparsed)
            .Where(item => item.Severity == JobMlDiagnosticSeverity.Error).ToList();

        Assert.Empty(errors);
        Assert.Equal("career_record", reparsed.Data.Header.Profile);
        Assert.Contains("# Jane Smith", reparsed.Markdown);
        Assert.Contains("+44 1234 567890 - please email in the first instance -", reparsed.Markdown);
        Assert.Contains(reparsed.Data.Claims, claim => claim.Type == "achievement");
        Assert.Contains(reparsed.Data.Sources, source => source.Name == "source.docx");
        var repository = Assert.Single(reparsed.Data.Sources, source => source.Type == "repository");
        Assert.Contains(reparsed.Data.Claims.SelectMany(claim => claim.Evidence),
            evidence => evidence.Type == "repository" && evidence.SourceId == repository.Id);
        Assert.All(reparsed.Data.Concepts.Where(concept => concept.Embedding is not null),
            concept => Assert.Equal(4, concept.Embedding!.Vector.Count));
        Assert.Equal(4, reparsed.Data.RoleCentroids.Count);
        Assert.All(reparsed.Data.RoleCentroids, centroid => Assert.Equal(4, centroid.Vector.Count));
        Assert.Contains("semantic_spaces:", serialized);
        Assert.Contains("role_centroids:", serialized);
        Assert.Contains("source_id:", serialized);
        Assert.Contains("lucidresume.github:", serialized);
        Assert.Contains("repository_class: sustained-product", serialized);
        Assert.Contains(new string('a', 40), serialized);
        Assert.Contains("Observed upstream fork", serialized);
        Assert.Contains("fork: true", serialized);
        Assert.Contains("eligible_for_personal_evidence: false", serialized);
        Assert.DoesNotContain("observations:", serialized);
        Assert.DoesNotContain(reparsed.Data.Claims,
            claim => claim.Statement.Contains("Observed upstream fork", StringComparison.Ordinal));
        Assert.Contains("## Evidence compiler {#entity-project-", reparsed.Markdown);
        Assert.DoesNotContain("## Built a provenance-aware", reparsed.Markdown);
        Assert.DoesNotContain(JobMlProcessor.Validate(reparsed), diagnostic => diagnostic.Code == "JML011");
    }

    private sealed class DeterministicEmbedder : IEmbeddingService, IEmbeddingSpaceDescriptor
    {
        public string ModelId => "test/four-dimensional-v1";
        public string Normalization => "l2";
        public string? ModelDigest => "sha256:test";

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            var vector = new float[4];
            for (var index = 0; index < text.Length; index++) vector[index % vector.Length] += text[index] % 17;
            var magnitude = MathF.Sqrt(vector.Sum(value => value * value));
            if (magnitude > 0)
                for (var index = 0; index < vector.Length; index++) vector[index] /= magnitude;
            return Task.FromResult(vector);
        }

        public float CosineSimilarity(float[] a, float[] b) => a.Zip(b).Sum(pair => pair.First * pair.Second);
    }
}
