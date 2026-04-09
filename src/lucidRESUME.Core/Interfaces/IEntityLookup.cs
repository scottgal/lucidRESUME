namespace lucidRESUME.Core.Interfaces;

/// <summary>
/// Provides fast exact-match lookup for known entities (companies, locations, titles, industries).
/// Used by JD parser to validate NER candidates and boost confidence for recognised entities.
/// </summary>
public interface IEntityLookup
{
    bool IsKnownCompany(string text);
    bool IsKnownLocation(string text);
    bool IsKnownJobTitle(string text);
    bool IsKnownIndustry(string text);
    List<string> FindCompaniesInText(string text);
    List<string> FindLocationsInText(string text);
}
