using lucidRESUME.Compiler;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.JobML;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Compiler.Tests;

public sealed class CompilerTests
{
    [Fact]
    public async Task Snapshot_store_rejects_drift_and_publishes_immutable_revision()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucidresume-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemJobMlSnapshotStore(Options.Create(new JobMlCompilerOptions
            { SnapshotDirectory = directory }));
            var snapshot = await store.PublishAsync(Fixture.Source);
            var current = await store.GetCurrentAsync();
            var versioned = await store.GetAsync(snapshot.Revision);

            Assert.Equal(64, snapshot.Revision.Length);
            Assert.Equal(snapshot.Revision, current?.Revision);
            Assert.Equal(snapshot.Source, versioned?.Source);
            await Assert.ThrowsAsync<JobMlPublicationException>(() =>
                store.PublishAsync(Fixture.Source.Replace("15 engineer", "50 engineer")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Compiler_selects_human_master_prose_and_reports_real_gap()
    {
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            Fixture.Source, file, JobMlProcessor.Validate(file));
        var orchestrator = new ResumeCompositionOrchestrator([], new CompositionValidator());
        var compiler = new JobMlResumeCompiler(new FakeJobParser(), orchestrator);

        var result = await compiler.CompileAsync(snapshot,
            "VP Engineering. Must have TypeScript and AWS. Terraform is required.");

        Assert.Contains("Led a 15 engineer", result.HumanMarkdown);
        Assert.Contains("**VP Engineering**", result.HumanMarkdown);
        Assert.Contains("## Core Skills", result.HumanMarkdown);
        Assert.DoesNotContain("[Engineering Leadership](#", result.HumanMarkdown);
        Assert.Contains("**Platform engineering:**", result.HumanMarkdown);
        Assert.Contains("TypeScript", result.HumanMarkdown);
        Assert.Contains("AWS", result.HumanMarkdown);
        var skillLinks = System.Text.RegularExpressions.Regex.Matches(result.HumanMarkdown,
            @"\[(TypeScript|AWS)\]\(#(?<anchor>section-[^)]+)\)");
        Assert.Equal(2, skillLinks.Count);
        foreach (System.Text.RegularExpressions.Match link in skillLinks)
            Assert.Contains("{#" + link.Groups["anchor"].Value + "}", result.HumanMarkdown);
        Assert.Contains("## Experience", result.HumanMarkdown);
        Assert.Contains("### VP Engineering, Example Ltd", result.HumanMarkdown);
        Assert.Contains("Terraform", result.Manifest.Gaps);
        Assert.DoesNotContain("Terraform", result.HumanMarkdown);
        Assert.Contains("cJobML 0.1", result.PublishedMarkdown);
        Assert.Contains("complete.example/ledger", result.FullJobMlMarkdown);
    }

    [Fact]
    public async Task Compiler_retains_author_selected_career_anchor_without_requirement_match()
    {
        const string anchorProse = "Personally executed the first public production release of ASP.NET MVC.";
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        file = file with
        {
            Markdown = file.Markdown + $"\n\n### Microsoft {{#microsoft-role}}\n\n<p id=\"microsoft-release\">\n{anchorProse}\n</p>"
        };
        file.Data.Entities.Add(new JobMlEntity
        {
            Id = "microsoft-role",
            Type = "experience",
            Name = "Program Manager II, Microsoft Corp",
            Source = "#microsoft-role",
            Projection = new JobMlProjectionPreference
            {
                Include = "always",
                Reason = "Career anchor selected by the author."
            }
        });
        file.Data.Claims.Add(new JobMlClaim
        {
            Id = "microsoft-release",
            Subject = "microsoft-role",
            Type = "achievement",
            Statement = anchorProse,
            Origin = "declared",
            Review = "accepted",
            Evidence =
            [
                new JobMlEvidence
                {
                    Type = "prose",
                    Ref = "#microsoft-release",
                    Fingerprint = new JobMlFingerprint
                    {
                        Text = MarkdownEvidenceIndex.Fingerprint(anchorProse)
                    }
                }
            ]
        });
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            parser.Serialize(file), file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "TypeScript and AWS platform leadership.",
            new CompilationOptions { MaximumClaims = 1, MaximumSections = 1 });

        Assert.Contains(anchorProse, result.HumanMarkdown);
        Assert.Contains("Led a 15 engineer", result.HumanMarkdown);
        Assert.Equal(2, result.Manifest.Sections.Count);
        Assert.Contains(result.ProjectedJobMl.Data.Entities,
            entity => entity.Id == "microsoft-role" && entity.Projection?.Include == "always");
    }

    [Fact]
    public async Task Compiler_renders_experience_in_reverse_chronological_order()
    {
        const string recentProse = "Led a TypeScript and AWS product team through a current platform change.";
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        file = file with
        {
            Markdown = file.Markdown + $"\n\n### Recent Ltd {{#recent-role}}\n\n<p id=\"recent-leadership\">\n{recentProse}\n</p>"
        };
        file.Data.Entities.Add(new JobMlEntity
        {
            Id = "recent-role",
            Type = "experience",
            Name = "Engineering Lead, Recent Ltd",
            Source = "#recent-role"
        });
        var evidence = new JobMlEvidence
        {
            Type = "prose",
            Ref = "#recent-leadership",
            Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(recentProse) }
        };
        file.Data.Claims.Add(new JobMlClaim
        {
            Id = "recent-dates",
            Subject = "recent-role",
            Type = "experience",
            Statement = "Engineering Lead | Recent Ltd | 2025-01-01 | Present",
            Review = "accepted",
            Origin = "declared",
            Evidence = [evidence]
        });
        file.Data.Claims.Add(new JobMlClaim
        {
            Id = "recent-leadership",
            Subject = "recent-role",
            Type = "achievement",
            Statement = recentProse,
            Review = "accepted",
            Origin = "declared",
            Concepts = new JobMlClaimConcepts { Skills = ["typescript", "aws"] },
            Evidence = [evidence]
        });
        var source = parser.Serialize(file);
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            source, file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.");

        Assert.True(result.HumanMarkdown.IndexOf("Engineering Lead, Recent Ltd", StringComparison.Ordinal) <
                    result.HumanMarkdown.IndexOf("VP Engineering, Example Ltd", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Compiler_adds_only_omitted_roles_longer_than_three_months_to_compact_chronology()
    {
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        var roles = new[]
        {
            (Id: "four-hour-role", Name: "Consultant · Tiny Engagement", Start: "2024-01-01", End: "2024-01-01"),
            (Id: "three-month-role", Name: "Consultant · Exact Quarter Ltd", Start: "2023-01-01", End: "2023-04-01"),
            (Id: "long-role", Name: "Technical Lead · Durable Systems Ltd", Start: "2022-01-01", End: "2022-04-02"),
            (Id: "long-role-import", Name: "Durable Systems Limited · Remote", Start: "2022-01-01", End: "2022-04-02")
        };
        foreach (var role in roles)
        {
            var statement = $"{role.Name} | {role.Start} | {role.End}";
            file = file with
            {
                Markdown = file.Markdown + $"\n\n<p id=\"{role.Id}-dates\">\n{statement}\n</p>"
            };
            file.Data.Entities.Add(new JobMlEntity
            {
                Id = role.Id,
                Type = "experience",
                Name = role.Name,
                Source = $"#{role.Id}-dates"
            });
            file.Data.Claims.Add(new JobMlClaim
            {
                Id = $"{role.Id}-dates",
                Subject = role.Id,
                Type = "experience",
                Statement = statement,
                Review = "accepted",
                Origin = "declared",
                Evidence =
                [
                    new JobMlEvidence
                    {
                        Type = "prose", Ref = $"#{role.Id}-dates",
                        Fingerprint = new JobMlFingerprint
                        {
                            Text = MarkdownEvidenceIndex.Fingerprint(statement)
                        }
                    },
                    new JobMlEvidence
                    {
                        Id = $"{role.Id}-source",
                        Type = "source_ledger",
                        Ref = $"ledger://experience/{role.Id}",
                        Title = "Imported career record"
                    }
                ]
            });
        }
        var source = parser.Serialize(file);
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            source, file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.",
            new CompilationOptions { MinimumExperienceSections = 0, MaximumSections = 1, MaximumClaims = 1 });

        Assert.Contains("### Additional consulting, contract and earlier experience", result.HumanMarkdown);
        Assert.Contains("Technical Lead · Durable Systems Ltd | Jan 2022 - Apr 2022", result.HumanMarkdown);
        Assert.DoesNotContain("Tiny Engagement", result.HumanMarkdown);
        Assert.DoesNotContain("Exact Quarter", result.HumanMarkdown);
        Assert.DoesNotContain("Durable Systems Limited", result.HumanMarkdown);
        var additional = Assert.Single(result.Manifest.Sections,
            section => section.Kind == "additional_experience");
        Assert.Equal("long-role-dates", Assert.Single(additional.Claims).Claim.Id);
        Assert.Contains(result.ProjectedJobMl.Data.Entities,
            entity => entity.Id == "long-role" && entity.Source == "#additional-experience");
        Assert.Contains("Technical Lead · Durable Systems Ltd", result.PublishedMarkdown);
        Assert.Contains("Complete transcript: Technical Lead · Durable Systems Ltd", result.PublishedMarkdown);
        Assert.All(JobMlProcessor.Reconcile(result.ProjectedJobMl).SelectMany(item => item.Evidence),
            evidence => Assert.True(evidence.State is EvidenceState.Valid or EvidenceState.External));
    }

    [Fact]
    public async Task Compiler_retains_reviewed_education_as_a_conventional_section()
    {
        const string educationProse = "BSc (Hons) Psychology, University of Stirling.";
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        file = file with
        {
            Markdown = file.Markdown + $"\n\n### University of Stirling {{#education}}\n\n<p id=\"degree\">\n{educationProse}\n</p>"
        };
        file.Data.Entities.Add(new JobMlEntity
        {
            Id = "education",
            Type = "education",
            Name = "BSc (Hons) Psychology · University of Stirling",
            Source = "#education"
        });
        file.Data.Claims.Add(new JobMlClaim
        {
            Id = "degree",
            Subject = "education",
            Type = "education",
            Statement = educationProse,
            Review = "accepted",
            Origin = "declared",
            Evidence =
            [
                new JobMlEvidence
                {
                    Type = "prose", Ref = "#degree",
                    Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(educationProse) }
                }
            ]
        });
        var source = parser.Serialize(file);
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            source, file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.");

        Assert.Contains("## Education", result.HumanMarkdown);
        Assert.Contains("BSc (Hons) Psychology", result.HumanMarkdown);
        Assert.Contains(result.ProjectedJobMl.Data.Claims, claim => claim.Id == "degree");
        Assert.All(JobMlProcessor.Reconcile(result.ProjectedJobMl).SelectMany(item => item.Evidence),
            evidence => Assert.True(evidence.State is EvidenceState.Valid or EvidenceState.External));
    }

    [Fact]
    public async Task Compiler_prefers_productisation_project_for_an_executive_target()
    {
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        const string agentBody = "Built a TypeScript and AWS coding agents experiment.";
        const string productBody = "Led TypeScript and AWS productisation from research through commercial release.";
        const string agentProse = "Agent experiment: " + agentBody;
        const string productProse = "Commercial product: " + productBody;
        file = file with
        {
            Markdown = file.Markdown +
                       $"\n\n<p id=\"agent-project\">\n{agentProse}\n</p>" +
                       $"\n\n<p id=\"product-project\">\n{productProse}\n</p>"
        };
        foreach (var (id, name, prose) in new[]
                 {
                     ("agent-project", "Agent experiment", agentProse),
                     ("product-project", "Commercial product", productProse)
                 })
        {
            file.Data.Entities.Add(new JobMlEntity { Id = id, Type = "project", Name = name, Source = $"#{id}" });
            file.Data.Claims.Add(new JobMlClaim
            {
                Id = $"{id}-claim",
                Subject = id,
                Type = "project",
                Statement = prose,
                Review = "accepted",
                Origin = "declared",
                Concepts = new JobMlClaimConcepts { Skills = ["typescript", "aws"] },
                Evidence =
                [
                    new JobMlEvidence
                    {
                        Type = "prose", Ref = $"#{id}",
                        Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(prose) }
                    }
                ]
            });
        }
        var source = parser.Serialize(file);
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            source, file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.",
            new CompilationOptions { MinimumExperienceSections = 0, MaximumSections = 1, MaximumClaims = 1 });

        Assert.Contains(productBody, result.HumanMarkdown);
        Assert.DoesNotContain(productProse, result.HumanMarkdown);
        Assert.DoesNotContain(agentBody, result.HumanMarkdown);

        var handsOnCompiler = new JobMlResumeCompiler(new FakeJobParser(new JobDescription
        {
            Title = "Engineering Technical Lead",
            RequiredSkills = ["TypeScript", "AWS"],
            Responsibilities = ["Use AI-assisted development and coding agents throughout delivery"]
        }), new ResumeCompositionOrchestrator([], new CompositionValidator()));
        var handsOn = await handsOnCompiler.CompileAsync(snapshot,
            "Engineering Technical Lead using AI-assisted development and coding agents.",
            new CompilationOptions { MinimumExperienceSections = 0, MaximumSections = 1, MaximumClaims = 1 });

        Assert.Contains(agentBody, handsOn.HumanMarkdown);
        Assert.DoesNotContain(agentProse, handsOn.HumanMarkdown);
        Assert.DoesNotContain(productBody, handsOn.HumanMarkdown);
    }

    [Fact]
    public async Task Compiler_never_selects_a_claim_which_requires_review()
    {
        var file = new JobMlParser().Parse(Fixture.Source);
        // Legacy ledgers emitted "extracted". It must be treated as machine-derived,
        // not accepted merely because it is not the newer "derived" spelling.
        file.Data.Claims.Single().Origin = "extracted";
        file.Data.Claims.Single().Review = "required";
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            Fixture.Source, file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.");

        Assert.Empty(result.ProjectedJobMl.Data.Claims);
        Assert.DoesNotContain("Led a 15 engineer", result.HumanMarkdown);
    }

    [Fact]
    public async Task Compiler_multi_claim_section_has_valid_projection_fingerprints()
    {
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        file = file with
        {
            Markdown = file.Markdown + "\n\nImproved AWS release governance across the platform."
        };
        file.Data.Claims.Add(new JobMlClaim
        {
            Id = "release-governance",
            Subject = "example-role",
            Statement = "Improved AWS release governance.",
            Review = "accepted",
            Origin = "declared",
            Concepts = new JobMlClaimConcepts { Skills = ["aws"] },
            Evidence =
            [
                new JobMlEvidence
                {
                    Type = "prose", Ref = "#example-role:p2",
                    Fingerprint = new JobMlFingerprint
                    {
                        Text = MarkdownEvidenceIndex.Fingerprint("Improved AWS release governance across the platform.")
                    }
                }
            ]
        });
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            parser.Serialize(file), file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.");
        var resolutions = JobMlProcessor.Reconcile(result.ProjectedJobMl);

        Assert.Equal(2, result.ProjectedJobMl.Data.Claims.Count);
        Assert.All(resolutions.SelectMany(item => item.Evidence), evidence =>
            Assert.True(evidence.State is EvidenceState.Valid or EvidenceState.External));
    }

    [Fact]
    public async Task Compiler_projects_ledger_provenance_to_exact_published_transcript_section()
    {
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        file.Data.Claims.Single().Evidence.Add(new JobMlEvidence
        {
            Id = "imported-role-source",
            Type = "source_ledger",
            Ref = "ledger://experience/example-role",
            Title = "Imported career record"
        });
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            parser.Serialize(file), file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.",
            new CompilationOptions
            {
                FullJobMlUri = "https://resume.example/lucidresume/api/jobml/revision"
            });

        var transcript = Assert.Single(result.ProjectedJobMl.Data.Claims.Single().Evidence,
            evidence => evidence.Type == "career_transcript");
        Assert.Equal("Complete transcript: VP Engineering, Example Ltd", transcript.Title);
        Assert.Equal("https://resume.example/lucidresume/api/jobml/revision#example-role", transcript.Uri);
        Assert.Contains("[Career Transcript]", result.PublishedMarkdown);
        Assert.Contains("<https://resume.example/lucidresume/api/jobml/revision#example-role>",
            result.PublishedMarkdown);
        Assert.Contains("Full JobML: <https://resume.example/lucidresume/api/jobml/revision>",
            result.PublishedMarkdown);
    }

    [Fact]
    public async Task Compiler_collapses_near_duplicate_imported_prose_in_one_section()
    {
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        const string duplicate = "Led the 15-person TypeScript engineering team through an AWS platform change with release and security governance.";
        file = file with { Markdown = file.Markdown + $"\n\n{duplicate}" };
        file.Data.Claims.Add(new JobMlClaim
        {
            Id = "leadership-duplicate",
            Subject = "example-role",
            Type = "achievement",
            Statement = duplicate,
            Review = "accepted",
            Origin = "declared",
            Concepts = new JobMlClaimConcepts { Skills = ["typescript", "aws"] },
            Evidence =
            [
                new JobMlEvidence
                {
                    Type = "prose", Ref = "#example-role:p2",
                    Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(duplicate) }
                }
            ]
        });
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            parser.Serialize(file), file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.");

        Assert.Single(result.Manifest.Sections.Single().Claims);
        Assert.Single(result.ProjectedJobMl.Data.Claims);
    }

    [Fact]
    public async Task Compiler_limits_each_role_to_two_selected_claims_and_a_compact_target_brief()
    {
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        for (var index = 2; index <= 6; index++)
        {
            var prose = $"Delivered TypeScript AWS outcome number {index} with a distinct production constraint.";
            file = file with { Markdown = file.Markdown + $"\n\n<p id=\"claim-{index}\">\n{prose}\n</p>" };
            file.Data.Claims.Add(new JobMlClaim
            {
                Id = $"claim-{index}",
                Subject = "example-role",
                Type = "achievement",
                Statement = prose,
                Review = "accepted",
                Origin = "declared",
                Concepts = new JobMlClaimConcepts { Skills = ["typescript", "aws"] },
                Evidence =
                [
                    new JobMlEvidence
                    {
                        Type = "prose",
                        Ref = $"#claim-{index}",
                        Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(prose) }
                    }
                ]
            });
        }
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            parser.Serialize(file), file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.");
        var section = Assert.Single(result.Manifest.Sections);

        Assert.InRange(section.Claims.Count, 1, 2);
        Assert.InRange(section.MaximumWords, 24, 76);
        Assert.True(section.Claims.Sum(claim => CountWords(claim.Prose)) <= section.MaximumWords);
        Assert.Contains("not a catalogue of duties", section.Intent);
        Assert.Contains("target emphasis", section.Intent);
    }

    [Fact]
    public async Task Compiler_retains_a_reviewed_role_framing_claim_before_keyword_dense_detail()
    {
        const string framing = "Asked to design two auditable customer-import systems for a regulated workflow.";
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        file = file with { Markdown = file.Markdown + $"\n\n<p id=\"role-framing\">\n{framing}\n</p>" };
        file.Data.Claims.Add(new JobMlClaim
        {
            Id = "role-framing",
            Subject = "example-role",
            Type = "achievement",
            Statement = framing,
            Review = "accepted",
            Origin = "declared",
            Evidence =
            [
                new JobMlEvidence
                {
                    Type = "prose", Ref = "#role-framing",
                    Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(framing) }
                }
            ]
        });
        var source = parser.Serialize(file);
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            source, file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.");

        Assert.Contains(framing, result.HumanMarkdown);
        Assert.Contains("Led a 15 engineer TypeScript team", result.HumanMarkdown);
    }

    [Fact]
    public async Task Compiler_compacts_long_human_passage_by_selecting_complete_sentences()
    {
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        const string original = "Led a 15 engineer TypeScript team through platform change on AWS, with accountable release and security governance.";
        const string longProse = "Built a dependable internal service in C#. Led a 15 engineer TypeScript team through platform change on AWS, with accountable release and security governance. Documented unrelated office administration, travel booking, equipment ordering and meeting-room arrangements in exhaustive detail for historical completeness. Improved AWS deployment feedback while retaining human review.";
        file = file with { Markdown = file.Markdown.Replace(original, longProse, StringComparison.Ordinal) };
        file.Data.Claims.Single().Evidence.Single(evidence => evidence.Type == "prose").Fingerprint = new JobMlFingerprint
        {
            Text = MarkdownEvidenceIndex.Fingerprint(longProse)
        };
        var source = parser.Serialize(file);
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            source, file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.");
        var section = Assert.Single(result.Manifest.Sections);

        Assert.True(CountWords(section.Claims.Single().Prose) <= section.MaximumWords);
        Assert.Contains("15 engineer TypeScript team", section.Claims.Single().Prose);
        Assert.DoesNotContain("office administration", section.Claims.Single().Prose);
        Assert.EndsWith(".", section.Claims.Single().Prose);
    }

    [Fact]
    public async Task Compiler_keeps_the_reviewed_identity_sentence_when_targeting_a_summary()
    {
        const string summary = "Hands-on engineering leader with more than 20 years of experience building products and leading teams. Uses coding agents daily across design, implementation, testing and review, taking AI products from research through commercial release. Combines C#, ASP.NET Core, TypeScript, AWS and distributed systems with technical strategy and delivery governance. Has led teams of more than 10 engineers and trained more than 200 developers. Builds deterministic evaluation, provenance, deployment and human-review systems around probabilistic components.";
        var parser = new JobMlParser();
        var file = parser.Parse(Fixture.Source);
        file = file with { Markdown = file.Markdown + $"\n\n<p id=\"professional-summary\">\n{summary}\n</p>" };
        file.Data.Entities.Add(new JobMlEntity
        {
            Id = "person",
            Type = "person",
            Name = "Professional Summary",
            Source = "#professional-summary"
        });
        file.Data.Claims.Add(new JobMlClaim
        {
            Id = "summary",
            Subject = "person",
            Type = "summary",
            Statement = summary,
            Review = "accepted",
            Origin = "declared",
            Evidence =
            [
                new JobMlEvidence
                {
                    Type = "prose", Ref = "#professional-summary",
                    Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(summary) }
                }
            ]
        });
        var source = parser.Serialize(file);
        var snapshot = new JobMlSnapshot(new string('a', 64), DateTimeOffset.UtcNow,
            source, file, JobMlProcessor.Validate(file));
        var compiler = new JobMlResumeCompiler(new FakeJobParser(),
            new ResumeCompositionOrchestrator([], new CompositionValidator()));

        var result = await compiler.CompileAsync(snapshot, "VP Engineering with TypeScript and AWS.");
        var summaryPacket = result.Manifest.Sections.Single(section => section.Kind == "summary");

        Assert.StartsWith("Hands-on engineering leader", summaryPacket.Claims.Single().Prose);
        Assert.InRange(CountWords(summaryPacket.Claims.Single().Prose), 1, 80);
    }

    [Fact]
    public async Task Orchestrator_discards_a_pass_that_invents_a_number()
    {
        var claim = new JobMlClaim { Id = "leadership", Subject = "role", Statement = "Led engineering." };
        var selected = new SelectedClaim(claim, "Example", "Led a 15 engineer team.", ["e1"], .9, []);
        var packet = new EvidencePacket("role", "Example", "tighten", 80, [selected], []);
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow, [], [packet], [], [], "lexical");
        var orchestrator = new ResumeCompositionOrchestrator([new InventingProvider()], new CompositionValidator());

        var result = await orchestrator.ComposeAsync(manifest, "lead a team", new CompilationOptions
        { ComposeProse = true, CompositionProvider = "bad" });

        Assert.Equal("Led a 15 engineer team.", result.Blocks.Single().Text);
        Assert.Contains(result.Warnings, x => x.Contains("numeric fact '40'"));
    }

    [Fact]
    public void Validator_allows_numeric_thousands_separator_formatting()
    {
        var claim = new JobMlClaim { Id = "scale", Subject = "role", Statement = "Served 5000 customers." };
        var selected = new SelectedClaim(claim, "Example", "Served 5000 customers.", ["e1"], .9, []);
        var packet = new EvidencePacket("role", "Example", "tighten", 20, [selected], []);
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow, [], [packet], [], [], "lexical");
        var source = new CompositionBlock("role", "Served 5000 customers.", ["scale"], ["e1"]);
        var draft = new CompositionDraft([source with { Text = "Served 5,000 customers." }], []);

        var errors = new CompositionValidator().Validate(draft, [source], manifest);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validator_treats_led_and_lead_as_the_same_grounded_term()
    {
        var claim = new JobMlClaim { Id = "leadership", Subject = "role", Statement = "Led delivery." };
        var selected = new SelectedClaim(claim, "Example", "Led delivery.", ["e1"], .9, []);
        var packet = new EvidencePacket("role", "Example", "tighten", 20, [selected], []);
        var requirement = new CompilerRequirement("req-1", "Lead delivery", RequirementKind.Required, "Lead delivery");
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow,
            [requirement], [packet], [], [], "lexical");
        var source = new CompositionBlock("role", "Led delivery.", ["leadership"], ["e1"]);
        var draft = new CompositionDraft([source with { Text = "Lead delivery." }], []);

        var errors = new CompositionValidator().Validate(draft, [source], manifest);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("Led a team.", "Led teams.", "Lead teams")]
    [InlineData("Delivered a service.", "Delivery of services.", "Service delivery")]
    public void Validator_allows_bounded_inflection_changes(
        string sourceText, string editedText, string requirementText)
    {
        var claim = new JobMlClaim { Id = "grounded", Subject = "role", Statement = sourceText };
        var selected = new SelectedClaim(claim, "Example", sourceText, ["e1"], .9, []);
        var packet = new EvidencePacket("role", "Example", "tighten", 20, [selected], []);
        var requirement = new CompilerRequirement("req-1", requirementText,
            RequirementKind.Required, requirementText);
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow,
            [requirement], [packet], [], [], "lexical");
        var source = new CompositionBlock("role", sourceText, ["grounded"], ["e1"]);
        var draft = new CompositionDraft([source with { Text = editedText }], []);

        var errors = new CompositionValidator().Validate(draft, [source], manifest);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Orchestrator_discards_an_unsupported_term_copied_from_the_vacancy()
    {
        var claim = new JobMlClaim { Id = "leadership", Subject = "role", Statement = "Led engineering." };
        var selected = new SelectedClaim(claim, "Example", "Led an engineering team.", ["e1"], .9, []);
        var packet = new EvidencePacket("role", "Example", "tighten", 80, [selected], []);
        var requirement = new CompilerRequirement("req-1", "Terraform experience", RequirementKind.Required, "Terraform experience");
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow,
            [requirement], [packet], [], [], "lexical");
        var orchestrator = new ResumeCompositionOrchestrator([new VacancyCopyingProvider()], new CompositionValidator());

        var result = await orchestrator.ComposeAsync(manifest, "Terraform experience", new CompilationOptions
        { ComposeProse = true, CompositionProvider = "copy" });

        Assert.Equal("Led an engineering team.", result.Blocks.Single().Text);
        Assert.Contains(result.Warnings, x => x.Contains("target-role term 'terraform'"));
    }

    [Fact]
    public async Task Orchestrator_keeps_valid_section_edits_when_another_section_fails_validation()
    {
        var claimA = new JobMlClaim { Id = "a", Subject = "role-a", Statement = "Built service." };
        var claimB = new JobMlClaim { Id = "b", Subject = "role-b", Statement = "Led team." };
        var selectedA = new SelectedClaim(claimA, "A", "Built a reliable service.", ["ea"], .9, []);
        var selectedB = new SelectedClaim(claimB, "B", "Led a small team.", ["eb"], .9, []);
        var packets = new[]
        {
            new EvidencePacket("role-a", "A", "tighten", 20, [selectedA], []),
            new EvidencePacket("role-b", "B", "tighten", 20, [selectedB], [])
        };
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow, [], packets, [], [], "lexical");
        var orchestrator = new ResumeCompositionOrchestrator([new PartiallyValidProvider()], new CompositionValidator());

        var result = await orchestrator.ComposeAsync(manifest, "lead a team", new CompilationOptions
        { ComposeProse = true, CompositionProvider = "partial" });

        Assert.True(result.Used);
        Assert.Equal("Built a reliable service.", result.Blocks.Single(block => block.SectionId == "role-a").Text);
        Assert.Equal("Led team.", result.Blocks.Single(block => block.SectionId == "role-b").Text);
        Assert.Contains(result.Warnings, warning => warning.Contains("role-a") && warning.Contains("numeric fact '40'"));
    }

    [Fact]
    public async Task Orchestrator_rejects_job_ad_copying_in_summary()
    {
        var claim = new JobMlClaim { Id = "summary", Subject = "person", Type = "summary", Statement = "Human summary." };
        var selected = new SelectedClaim(claim, "Person", "Human summary.", ["e1"], .9, []);
        var packet = new EvidencePacket("summary", "Professional Summary", "retain", 20, [selected], [], "summary");
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow,
            [new CompilerRequirement("terraform", "Terraform", RequirementKind.Required, "Terraform")],
            [packet], [], [], "lexical");
        var orchestrator = new ResumeCompositionOrchestrator([new VacancyCopyingProvider()], new CompositionValidator());

        var result = await orchestrator.ComposeAsync(manifest, "Terraform experience", new CompilationOptions
        { ComposeProse = true, CompositionProvider = "copy" });

        Assert.Equal("Human summary.", Assert.Single(result.Blocks).Text);
        Assert.False(result.Used);
    }

    [Fact]
    public async Task Orchestrator_can_tighten_a_summary_without_changing_its_claim()
    {
        var claim = new JobMlClaim { Id = "summary", Subject = "person", Type = "summary",
            Statement = "I build scalable services and lead engineering teams." };
        var selected = new SelectedClaim(claim, "Person", claim.Statement, ["e1"], .9, []);
        var packet = new EvidencePacket("summary", "Professional Summary", "tighten", 20,
            [selected], [], "summary");
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow, [], [packet], [], [], "lexical");
        var orchestrator = new ResumeCompositionOrchestrator([new SummaryEditingProvider()], new CompositionValidator());

        var result = await orchestrator.ComposeAsync(manifest, "Build cloud services", new CompilationOptions
        { ComposeProse = true, CompositionProvider = "summary-edit" });

        Assert.True(result.Used);
        var edited = Assert.Single(result.Blocks);
        Assert.Equal("I build scalable services.", edited.Text);
        Assert.Equal("summary", Assert.Single(edited.ClaimIds));
        Assert.Equal("e1", Assert.Single(edited.EvidenceIds));
    }

    [Fact]
    public async Task Orchestrator_never_rewrites_compact_additional_experience()
    {
        var claim = new JobMlClaim
        {
            Id = "dates",
            Subject = "role",
            Type = "experience",
            Statement = "Consultant · Example Ltd | 2020-01-01 | 2020-06-01"
        };
        var prose = "Consultant · Example Ltd | Jan 2020 - Jun 2020";
        var selected = new SelectedClaim(claim, "Consultant · Example Ltd", prose, ["e1"], 0, []);
        var packet = new EvidencePacket("additional-role", prose, "retain", 10, [selected], [],
            "additional_experience");
        var manifest = new ProjectionManifest("source", "job", DateTimeOffset.UtcNow, [], [packet], [], [],
            "lexical");
        var orchestrator = new ResumeCompositionOrchestrator([new VacancyCopyingProvider()],
            new CompositionValidator());

        var result = await orchestrator.ComposeAsync(manifest, "Terraform experience", new CompilationOptions
        { ComposeProse = true, CompositionProvider = "copy" });

        Assert.Equal(prose, Assert.Single(result.Blocks).Text);
        Assert.False(result.Used);
    }

    private sealed class FakeJobParser(JobDescription? parsed = null) : IJobSpecParser
    {
        public Task<JobDescription> ParseFromTextAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(parsed ?? new JobDescription
            {
                Title = "VP Engineering",
                RawText = text,
                RequiredSkills = ["TypeScript", "AWS", "Terraform"],
                Responsibilities = ["Lead engineering teams through change"]
            });
        public Task<JobDescription> ParseFromUrlAsync(string url, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class InventingProvider : IResumeCompositionProvider
    {
        public string ProviderId => "bad";
        public bool IsAvailable => true;
        public Task<CompositionDraft> RunPassAsync(CompositionPassRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompositionDraft(
                request.CurrentBlocks.Select(x => x with { Text = "Led a 40 engineer team." }).ToList(), []));
    }

    private sealed class VacancyCopyingProvider : IResumeCompositionProvider
    {
        public string ProviderId => "copy";
        public bool IsAvailable => true;
        public Task<CompositionDraft> RunPassAsync(CompositionPassRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompositionDraft(
                request.CurrentBlocks.Select(x => x with { Text = "Led an engineering and Terraform team." }).ToList(), []));
    }

    private sealed class SummaryEditingProvider : IResumeCompositionProvider
    {
        public string ProviderId => "summary-edit";
        public bool IsAvailable => true;
        public Task<CompositionDraft> RunPassAsync(CompositionPassRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new CompositionDraft(
            request.CurrentBlocks.Select(block => block with { Text = "I build scalable services." }).ToList(), []));
    }

    private sealed class PartiallyValidProvider : IResumeCompositionProvider
    {
        public string ProviderId => "partial";
        public bool IsAvailable => true;
        public Task<CompositionDraft> RunPassAsync(CompositionPassRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new CompositionDraft(
            request.CurrentBlocks.Select(block => block with
            {
                Text = block.SectionId == "role-a" ? "Built 40 reliable services." : "Led team."
            }).ToList(), []));
    }

    private static int CountWords(string value) =>
        System.Text.RegularExpressions.Regex.Matches(value, @"\b[\p{L}\p{N}][\p{L}\p{N}'’-]*\b").Count;
}

internal static class Fixture
{
    private const string Prose = "Led a 15 engineer TypeScript team through platform change on AWS, with accountable release and security governance.";
    private static readonly string Fingerprint = MarkdownEvidenceIndex.Fingerprint(Prose);

    public static string Source => $$"""
        # Alex Example

        ## Complete Experience

        ### Example Ltd {#example-role}

        <p id="example-leadership">
        {{Prose}}
        </p>

        ---

        ```jobml
        jobml:
          version: "0.1"
          purpose: Complete machine-readable evidence ledger for this resume.
          semantics:
            - Claims describe experience, skills, capabilities, responsibilities, or domain knowledge.
            - Every substantive claim should be supported by one or more evidence references.
            - Do not infer unsupported claims.
        document:
          id: alex-complete-resume
          language: en-GB
          complete_ledger: https://complete.example/ledger
        entities:
          - id: example-role
            type: experience
            name: VP Engineering, Example Ltd
            source: "#example-role"
        claims:
          - id: leadership
            subject: example-role
            statement: Led an engineering team through platform change.
            review: accepted
            concepts:
              skills: [typescript, aws]
              capabilities: [engineering-leadership]
            supported_by:
              - id: leadership-prose
                type: prose
                ref: "#example-leadership"
                fingerprint:
                  text: "{{Fingerprint}}"
              - id: engineering-post
                type: article
                uri: https://example.com/engineering
                title: Engineering through change
        concepts:
          - id: typescript
            type: skill
            name: TypeScript
          - id: aws
            type: skill
            name: AWS
            aliases: [Amazon Web Services]
          - id: engineering-leadership
            type: capability
            name: Engineering Leadership
            aliases: [lead engineering teams]
        ```
        """;
}
