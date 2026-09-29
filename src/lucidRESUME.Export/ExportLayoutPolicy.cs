using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Export;

internal static class ExportLayoutPolicy
{
    /// <summary>
    /// Returns the experience-list index before which a second page should begin.
    /// The split is based on detailed roles, leaving compact chronology, education,
    /// and publications to complete the second page.
    /// </summary>
    public static int? SecondPageExperienceIndex(ResumeDocument resume)
    {
        if (resume.MinimumOutputPages < 2 || resume.Experience.Count == 0)
            return null;

        var detailedIndices = resume.Experience
            .Select((experience, index) => (experience, index))
            .Where(item => !item.experience.IsCompact)
            .Select(item => item.index)
            .ToList();

        if (detailedIndices.Count == 0)
            return 0;

        if (detailedIndices.Count == 1)
            return detailedIndices[0];

        // Page one also carries identity, summary, skills, and selected engineering,
        // so put the larger half of an odd role count on page two.
        var detailedSplit = Math.Clamp(detailedIndices.Count / 2, 1, detailedIndices.Count - 1);
        return detailedIndices[detailedSplit];
    }
}
