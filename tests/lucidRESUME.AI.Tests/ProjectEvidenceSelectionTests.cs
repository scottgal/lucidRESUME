using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI.Tests;

public sealed class ProjectEvidenceSelectionTests
{
    [Fact]
    public void Generic_platform_word_is_insufficient_to_select_a_personal_blog_for_an_ai_role()
    {
        var project = new Project { Name = "Personal blog", Description = "Developed a website and blog platform from scratch." };
        Assert.Empty(SemanticCompressor.SelectRelevantProjects([project], new HashSet<string>(["AI", "platform", "evaluation"])));
    }

    [Fact]
    public void Repository_and_package_evidence_is_not_forced_into_an_unrelated_projection()
    {
        var projects = EvidenceProjects();

        var selected = SemanticCompressor.SelectRelevantProjects(projects,
            new HashSet<string>(["kubernetes", "healthcare"], StringComparer.OrdinalIgnoreCase));

        Assert.Empty(selected);
    }

    [Fact]
    public void Browser_extension_is_selected_when_the_target_asks_for_its_evidence()
    {
        var projects = EvidenceProjects();

        var selected = SemanticCompressor.SelectRelevantProjects(projects,
            new HashSet<string>(["chrome", "extension", "prompt"], StringComparer.OrdinalIgnoreCase));

        var project = Assert.Single(selected);
        Assert.Equal("lucidRESUME browser extension", project.Name);
    }

    [Fact]
    public void Package_family_is_selected_when_the_target_matches_its_technical_content()
    {
        var projects = EvidenceProjects();

        var selected = SemanticCompressor.SelectRelevantProjects(projects,
            new HashSet<string>(["rag", "embeddings", "retrieval"], StringComparer.OrdinalIgnoreCase));

        var project = Assert.Single(selected);
        Assert.Equal("LucidRAG package family", project.Name);
    }

    [Fact]
    public void Matching_fork_observation_cannot_become_a_selected_personal_project()
    {
        var projects = EvidenceProjects();
        projects.Add(new Project
        {
            Name = "Upstream AI platform fork",
            Description = "Agentic AI orchestration platform.",
            Technologies = ["AI", "Agents"],
            EvidenceMetadata = new Dictionary<string, string>
            {
                ["provider"] = "github",
                ["fork"] = "true",
                ["eligible_for_personal_evidence"] = "false"
            }
        });

        var selected = SemanticCompressor.SelectRelevantProjects(projects,
            new HashSet<string>(["agentic", "agents"], StringComparer.OrdinalIgnoreCase));

        Assert.Empty(selected);
    }

    private static List<Project> EvidenceProjects() =>
    [
        new()
        {
            Name = "lucidRESUME browser extension",
            Description = "Chrome browser extension using the on-device Prompt API for form mapping.",
            Technologies = ["Chrome", "Prompt API"]
        },
        new()
        {
            Name = "LucidRAG package family",
            Description = "Public package family for retrieval augmented generation and embeddings.",
            Technologies = ["RAG", "Embeddings", "Vector search"]
        }
    ];
}
