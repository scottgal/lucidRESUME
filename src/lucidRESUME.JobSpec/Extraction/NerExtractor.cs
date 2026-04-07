using lucidRESUME.Core.Interfaces;

namespace lucidRESUME.JobSpec.Extraction;

/// <summary>
/// Runs NER models on JD text and maps entity labels to JD field candidates.
/// </summary>
public static class NerExtractor
{
    public static async Task<List<JdFieldCandidate>> ExtractAsync(
        string text, IEnumerable<IEntityDetector> detectors, CancellationToken ct = default)
    {
        var candidates = new List<JdFieldCandidate>();
        var context = new DetectionContext(text);

        foreach (var detector in detectors)
        {
            try
            {
                var entities = await detector.DetectAsync(context, ct);
                foreach (var entity in entities)
                {
                    // Skip very short fragments and low-confidence noise
                    if (entity.Value.Length < 2 || entity.Confidence < 0.5) continue;

                    var fieldType = MapLabelToField(entity.Label) ?? MapLabelToField(entity.Classification);
                    if (fieldType is null) continue;

                    candidates.Add(new JdFieldCandidate(
                        fieldType,
                        entity.Value,
                        entity.Confidence,
                        $"ner:{detector.DetectorId}"));
                }
            }
            catch { /* individual detector failure is non-fatal */ }
        }

        return candidates;
    }

    private static string? MapLabelToField(string? label) => label switch
    {
        // Mapped classification names (from OnnxNerDetector entity type maps)
        "NerSkill" => "skill",
        "Organization" => "company",
        "JobTitle" => "title",
        "Address" => "location",
        "YearsExperience" => "yearsexp",
        "Miscellaneous" => null, // general NER MISC too noisy for JD skills - produces fragments
        "PersonName" or "Email" or "PhoneNumber" or "Url" or "Degree" or "Date" => null,

        // Raw BIO labels (fallback if Classification isn't mapped)
        "Skills" or "B-Skills" => "skill",
        "Companies worked at" or "B-Companies worked at" => "company",
        "Designation" or "B-Designation" => "title",
        "Location" or "B-Location" => "location",
        "Years of Experience" or "B-Years of Experience" => "yearsexp",
        "ORG" or "B-ORG" => "company",
        "LOC" or "B-LOC" => "location",
        "MISC" or "B-MISC" => null, // too noisy for JD extraction

        _ => null
    };
}