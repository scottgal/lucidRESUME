using lucidRESUME.JobML;

namespace lucidRESUME.Compiler.Tests;

public sealed class ProseQualityTests
{
    [Fact]
    public void Test_limitations_do_not_move_past_a_separate_experiment_with_an_ambiguous_subject()
    {
        const string source = "In an offline test, manual review found fewer errors. The test did not measure availability. A separate pilot showed no change in resolution time.";
        var errors = Validate(source, "In an offline test, manual review found fewer errors. A separate pilot showed no change in resolution time. The test did not measure availability.");
        Assert.Contains(errors, error => error.Contains("detached a test limitation"));
        Assert.Empty(Validate(source, source));
    }

    [Fact]
    public void Being_asked_to_achieve_a_result_does_not_establish_its_completion()
    {
        const string source = "Asked to stabilise an internal support agent after its pilot. Added retries using C#.";
        var errors = Validate(source, "Stabilised an internal support agent after its pilot. Added retries using C#.");
        Assert.Contains(errors, error => error.Contains("assigned objective"));
        Assert.Empty(Validate(source, source));
    }

    [Theory]
    [InlineData("Supported an ASP.NET Core applicaiton using Azure.", "Supported an ASP.NET Core application using Azure.", "application")]
    [InlineData("Supported a large distrubted service on Azure.", "Supported a large distributed service on Azure.", "distributed")]
    [InlineData("Built services to meet customer requirement.", "Built services to meet customer requirements.", "requirements")]
    public void Harmless_spelling_and_plural_edits_do_not_become_invented_job_facts(string source, string edit, string requirement)
    {
        var packet = Packet(source, []);
        var manifest = Manifest(packet) with { Requirements = [new("r", requirement, RequirementKind.Required, requirement)] };
        var block = new CompositionBlock("role", source, ["claim"], ["evidence"]);
        Assert.Empty(new CompositionValidator().Validate(new([block with { Text = edit }], []), [block], manifest));
    }

    [Theory]
    [InlineData("Built a prototype. Proposed a human review queue; the project was cancelled before implementation.", true)]
    [InlineData("Built a prototype. Proposed a human review queue; the project was cancelled before the queue was implemented.", false)]
    public void Cancellation_keeps_the_unimplemented_object_separate_from_the_built_prototype(string edited, bool rejected)
    {
        var errors = Validate("Built a prototype. Proposed a human review queue, but the project was cancelled before that queue was implemented.", edited);
        if (rejected) Assert.Contains(errors, error => error.Contains("what was not implemented"));
        else Assert.Empty(errors);
    }

    [Fact]
    public void Passive_non_deployment_does_not_become_a_dangling_action() =>
        Assert.Contains(Validate("Built a prototype; it was never deployed to production.",
            "Built a prototype, never deploying to production."), error => error.Contains("dangling action"));

    [Fact]
    public void Measured_latency_keeps_its_measurement_status() =>
        Assert.Contains(Validate("Optimised PostgreSQL queries: measured test latency fell from 240 ms to 180 ms.",
            "Optimised PostgreSQL queries, reducing test latency from 240 ms to 180 ms."), error => error.Contains("measured status"));

    [Fact]
    public void Measurement_parser_does_not_read_servers_as_seconds()
    {
        var relation = Assert.Single(ProseNumericTransitions.Extract("From 2 to 4 servers."));
        Assert.Equal(new ProseNumericTransitions.Transition("2", "", "4", ""), relation);
    }

    [Fact]
    public void Measured_result_does_not_become_the_purpose_of_the_engineering_action()
    {
        var errors = Validate("Optimised PostgreSQL queries: measured test latency fell from 240 ms to 180 ms.",
            "To measure test latency, PostgreSQL queries were optimised from 240 ms to 180 ms.");
        Assert.Contains(errors, error => error.Contains("unsupported purpose"));
    }
    [Theory]
    [InlineData("Optimised PostgreSQL queries: test latency fell from 180 ms to 240 ms. Memory use rose from 180 MB to 240 MB.", true)]
    [InlineData("Optimised PostgreSQL queries: test latency fell from 240 MB to 180 MB. Memory use rose from 180 ms to 240 ms.", true)]
    [InlineData("Optimised PostgreSQL queries: test latency fell to 180 ms from 240 ms. Memory use rose from 180 MB to 240 MB.", false)]
    [InlineData("Memory use rose from 180 MB to 240 MB. Optimised PostgreSQL queries: test latency fell from 240 milliseconds to 180 milliseconds.", false)]
    public void Measurement_direction_and_units_survive_sentence_reordering(string edited, bool rejected)
    {
        const string source = "Optimised PostgreSQL queries: test latency fell from 240 ms to 180 ms. Memory use rose from 180 MB to 240 MB.";
        var errors = Validate(source, edited);
        if (rejected) Assert.Contains(errors, error => error.Contains("directed measurement"));
        else Assert.Empty(errors);
    }

    [Theory]
    [InlineData("Manual review found fewer tool-selection errors in an offline test.", true)]
    [InlineData("Manual review found fewer tool-selection errors in an offline test; the production effect is not yet established.", false)]
    public void Unknown_production_effect_cannot_disappear_from_a_measured_offline_result(string edited, bool rejected)
    {
        var errors = Validate("Manual review found fewer tool-selection errors in an offline test; the production effect remains unknown.", edited);
        if (rejected) Assert.Contains(errors, error => error.Contains("unknown outcome"));
        else Assert.Empty(errors);
    }

    [Fact]
    public void Sentence_final_short_number_is_not_mistaken_for_novel_content() =>
        Assert.Empty(Validate("Manual review found 14 fewer errors.", "Manual review found fewer errors: 14."));

    [Theory]
    [InlineData("deployed", "deploy")]
    [InlineData("shipped", "ship")]
    [InlineData("launched", "launch")]
    [InlineData("evaluated", "evaluate")]
    public void Personal_delivery_does_not_become_directing_engineers_to_deliver(string past, string present)
    {
        var errors = Validate($"Led three engineers through the release plan and personally {past} the C# service.",
            $"Led three engineers to {present} the C# service through the release plan.");
        Assert.Contains(errors, error => error.Contains("personal action"));
    }

    [Theory]
    [InlineData("Led three engineers through the release plan and personally wrote most adapters.", true)]
    [InlineData("Led three engineers through the release plan. Most adapters were written by the team.", false)]
    public void Team_construction_preserves_the_actor_in_active_or_passive_voice(string edited, bool rejected)
    {
        var errors = Validate("Led three engineers through the release plan. The team wrote most adapters.", edited);
        if (rejected) Assert.Contains(errors, error => error.Contains("team's explicit contribution"));
        else Assert.Empty(errors);
    }

    [Fact]
    public async Task Editing_restores_the_spelling_of_source_bound_technical_names()
    {
        const string source = "Built a Python prototype using PyTorch.";
        var packet = Packet(source, ["Python", "PyTorch"]);
        var result = await new ResumeCompositionOrchestrator([new CasingProvider()], new CompositionValidator())
            .ComposeAsync(Manifest(packet), "Python engineer", new() { ComposeProse = true });
        Assert.Equal(source, Assert.Single(result.Blocks).Text);
        Assert.True(result.Used);
    }

    [Theory]
    [InlineData("Designed PostgreSQL reporting queries and database backup recovery drills.", true)]
    [InlineData("DESIGNED PostgreSQL reporting queries and database backup recovery drills!", true)]
    [InlineData("Developed PostgreSQL reporting queries and database backup recovery drills.", true)]
    [InlineData("Software engineer who designed PostgreSQL reporting queries and database backup recovery drills.", true)]
    [InlineData("Experience with PostgreSQL reporting and database recovery.", false)]
    public void Summary_rejects_complete_repeated_sentences_but_allows_shared_technical_topics(string edited, bool repeats)
    {
        const string original = "Designed PostgreSQL reporting queries and database backup recovery drills.";
        var packet = Packet(original, ["PostgreSQL"]) with { Kind = "summary", DistinctFrom = [original] };
        var block = new CompositionBlock("role", original, ["claim"], ["evidence"]);
        var errors = new CompositionValidator().Validate(new([block with { Text = edited }], []), [block], Manifest(packet));
        Assert.Equal(repeats, errors.Any(error => error.Contains("repeats a complete or near-complete experience sentence")));
    }

    [Theory]
    [InlineData("safely")]
    [InlineData("securely")]
    [InlineData("reliably")]
    public void Controls_do_not_become_unverified_quality_assurances(string assurance)
    {
        var errors = Validate("Added per-tool authorization before allowing the agent to update ticket state.",
            $"Added per-tool authorization before allowing the agent to update ticket state {assurance}.");
        Assert.Contains(errors, error => error.Contains("unsupported assurance"));
    }

    private static IReadOnlyList<string> Validate(string source, string edited)
    {
        var packet = Packet(source, []);
        var block = new CompositionBlock("role", source, ["claim"], ["evidence"]);
        return new CompositionValidator().Validate(new([block with { Text = edited }], []), [block], Manifest(packet));
    }

    private static EvidencePacket Packet(string text, List<string> skills) => new("role", "Synthetic role", "Edit", 120,
        [new SelectedClaim(new JobMlClaim { Id = "claim", Subject = "role", Statement = text, Concepts = new() { Skills = skills } },
            "Synthetic role", text, ["evidence"], 1, [])], [], "experience");

    private static ProjectionManifest Manifest(EvidencePacket packet) => new("source", "job", DateTimeOffset.UtcNow, [], [packet], [], [], "none");

    private sealed class CasingProvider : IResumeCompositionProvider
    {
        public string ProviderId => "case-test";
        public bool IsAvailable => true;
        public Task<CompositionDraft> RunPassAsync(CompositionPassRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompositionDraft(request.CurrentBlocks.Select(block => block with
            { Text = block.Text.Replace("PyTorch", "Pytorch") }).ToList(), []));
    }
}
