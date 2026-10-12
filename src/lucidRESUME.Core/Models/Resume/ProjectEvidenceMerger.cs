namespace lucidRESUME.Core.Models.Resume;

/// <summary>
/// Refreshes machine-observed project evidence without replacing reviewed human prose.
/// </summary>
public static class ProjectEvidenceMerger
{
    public static void MergeAuditObservations(ResumeDocument resume, IEnumerable<Project> observations)
    {
        ArgumentNullException.ThrowIfNull(resume);
        ArgumentNullException.ThrowIfNull(observations);
        foreach (var observation in observations)
        {
            var index = resume.Projects.FindIndex(project => SameIdentity(project, observation));
            if (index < 0)
            {
                resume.Projects.Add(observation);
                continue;
            }

            var existing = resume.Projects[index];
            if (IsAuditOwned(existing))
            {
                resume.Projects[index] = observation;
                continue;
            }

            existing.Url ??= observation.Url;
            var humanTechnologies = existing.EvidenceMetadata.GetValueOrDefault("human_declared_technologies")
                ?? System.Text.Json.JsonSerializer.Serialize(existing.Technologies);
            existing.EvidencePassages = observation.EvidencePassages;
            existing.Technologies = existing.Technologies.Concat(observation.Technologies)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var (key, value) in observation.EvidenceMetadata)
                existing.EvidenceMetadata[key] = value;

            // Reviewed prose is independent evidence. A matching fork observation must
            // not suppress the human-authored project or its ledger claim.
            existing.EvidenceMetadata["eligible_for_personal_evidence"] = "true";
            existing.EvidenceMetadata["human_prose_preserved"] = "true";
            existing.EvidenceMetadata["human_declared_technologies"] = humanTechnologies;
        }
    }

    private static bool SameIdentity(Project left, Project right) =>
        string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Url, right.Url, StringComparison.OrdinalIgnoreCase);

    private static bool IsAuditOwned(Project project) => project.ImportSources.Any(source =>
        source.Contains("audit", StringComparison.OrdinalIgnoreCase));
}
