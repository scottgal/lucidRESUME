namespace lucidRESUME.Core.Models.Resume;

/// <summary>
/// Normalised education levels for cross-cultural equivalence.
/// A German "Diplom", UK "BSc (Hons)", US "Bachelor's", Indian "B.Tech"
/// all map to the same level. This prevents Leiden communities from
/// clustering by culture instead of by actual qualification.
/// </summary>
public enum EducationLevel
{
    Unknown = 0,
    SecondarySchool = 1,     // GCSE, O-levels, High School Diploma, SSC
    PostSecondary = 2,       // A-levels, IB, HSC, Abitur, Baccalauréat
    Vocational = 3,          // HND, HNC, BTEC, diplôme professionnel
    Associate = 4,           // Associate's degree, Foundation degree
    Bachelors = 5,           // BSc, BA, BEng, B.Tech, Licence, Diplom (FH)
    PostGradDiploma = 6,     // PgDip, PGCE, Graduate Diploma
    Masters = 7,             // MSc, MA, MEng, M.Tech, Diplom, DEA, Magistère
    Doctoral = 8,            // PhD, DPhil, Dr., Doktorgrad, D.Phil
    PostDoctoral = 9,        // PostDoc, Habilitation
}

/// <summary>
/// Maps degree names from various countries/systems to normalised EducationLevel.
/// Uses substring matching + known patterns. Extensible via taxonomy files.
/// </summary>
public static class EducationLevelClassifier
{
    private static readonly (string[] Patterns, EducationLevel Level)[] Rules =
    [
        // Doctoral
        (["phd", "ph.d", "dphil", "d.phil", "doctorate", "doktorgrad", "dr.", "doctoral", "dba", "edd", "md ", "juris doctor"], EducationLevel.Doctoral),

        // Masters
        (["master", "msc", "m.sc", "ma ", "m.a.", "mba", "meng", "m.eng", "m.tech", "mtech", "mphil", "m.phil",
          "diplom", "magistère", "dea", "dess", "llm", "mres", "med ", "msw"], EducationLevel.Masters),

        // PostGrad Diploma
        (["pgdip", "pgce", "graduate diploma", "postgraduate diploma", "grad dip"], EducationLevel.PostGradDiploma),

        // Bachelors
        (["bachelor", "bsc", "b.sc", "ba ", "b.a.", "beng", "b.eng", "btech", "b.tech", "licence",
          "llb", "bcom", "b.com", "bba", "bfa", "honours", "hons"], EducationLevel.Bachelors),

        // Associate
        (["associate", "foundation degree", "as ", "aa "], EducationLevel.Associate),

        // Vocational
        (["hnd", "hnc", "btec", "nvq", "city & guilds", "diplôme", "vocational", "technical diploma",
          "advanced diploma"], EducationLevel.Vocational),

        // Post-secondary (includes Scottish Highers)
        (["a-level", "a level", "ib diploma", "international baccalaureate", "abitur", "baccalauréat",
          "hsc", "higher secondary", "12th",
          "scottish higher", "advanced higher", "sqa higher", "csys", "leaving cert"], EducationLevel.PostSecondary),

        // Secondary (includes Scottish Standard Grades and National 5s)
        (["gcse", "o-level", "o level", "o-grade", "high school", "ssc", "secondary", "10th", "matric",
          "standard grade", "national 5", "intermediate 2", "intermediate 1", "national 4",
          "junior cert"], EducationLevel.SecondarySchool),
    ];

    /// <summary>
    /// Classify a degree string to a normalised education level.
    /// </summary>
    public static EducationLevel Classify(string? degree)
    {
        if (string.IsNullOrWhiteSpace(degree)) return EducationLevel.Unknown;
        var lower = $" {degree.ToLowerInvariant()} ";

        foreach (var (patterns, level) in Rules)
        {
            if (patterns.Any(p => lower.Contains(p)))
                return level;
        }

        return EducationLevel.Unknown;
    }

    /// <summary>
    /// Check if a candidate's education meets a JD's minimum requirement.
    /// </summary>
    public static bool MeetsRequirement(EducationLevel candidate, EducationLevel required)
    {
        if (required == EducationLevel.Unknown) return true; // no requirement
        return candidate >= required;
    }
}
