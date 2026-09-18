namespace lucidRESUME.JobML;

/// <summary>Builds the portable human + machine artefact distributed to employers.</summary>
public static class JobMlArtifactComposer
{
    public const string ArticleUrl = "https://mostlylucid.net/blog/the-problem-with-resumes";
    public const string MachineAreaHeading = "## MACHINE AREA";

    public static string Compose(JobMlFile file)
    {
        var serialized = new JobMlParser().Serialize(file).TrimEnd();
        var separator = serialized.LastIndexOf("\n---\n", StringComparison.Ordinal);
        if (separator < 0) return serialized + "\n";

        var prose = serialized[..separator].TrimEnd();
        var jobMlFence = serialized[(separator + 5)..].TrimStart();
        return $"""
            {prose}

            ---

            {MachineAreaHeading}

            Machine-readable evidence and claim links. [What is this?]({ArticleUrl})

            {jobMlFence}
            """ + "\n";
    }
}
