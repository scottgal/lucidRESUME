using System.Text.Json.Serialization;

namespace lucidRESUME.Export.JsonResume;

// Follows https://jsonresume.org/schema/
public sealed class JsonResumeRoot
{
    [JsonPropertyName("basics")] public JsonResumeBasics? Basics { get; set; }
    [JsonPropertyName("work")] public List<JsonResumeWork> Work { get; set; } = [];
    [JsonPropertyName("education")] public List<JsonResumeEducation> Education { get; set; } = [];
    [JsonPropertyName("skills")] public List<JsonResumeSkill> Skills { get; set; } = [];
    [JsonPropertyName("certificates")] public List<JsonResumeCertificate> Certificates { get; set; } = [];
    [JsonPropertyName("projects")] public List<JsonResumeProject> Projects { get; set; } = [];
}

public sealed class JsonResumeBasics
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("location")] public JsonResumeLocation? Location { get; set; }
    [JsonPropertyName("profiles")] public List<JsonResumeProfile> Profiles { get; set; } = [];
}

public sealed record JsonResumeLocation(
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("countryCode")] string? CountryCode);

public sealed record JsonResumeProfile(
    [property: JsonPropertyName("network")] string Network,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("username")] string? Username);

public sealed record JsonResumeWork(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("position")] string? Position,
    [property: JsonPropertyName("startDate")] string? StartDate,
    [property: JsonPropertyName("endDate")] string? EndDate,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("highlights")] List<string> Highlights);

public sealed record JsonResumeEducation(
    [property: JsonPropertyName("institution")] string? Institution,
    [property: JsonPropertyName("area")] string? Area,
    [property: JsonPropertyName("studyType")] string? StudyType,
    [property: JsonPropertyName("startDate")] string? StartDate,
    [property: JsonPropertyName("endDate")] string? EndDate,
    [property: JsonPropertyName("score")] string? Score);

public sealed record JsonResumeSkill(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("level")] string? Level,
    [property: JsonPropertyName("keywords")] List<string> Keywords);

public sealed record JsonResumeCertificate(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("issuer")] string? Issuer,
    [property: JsonPropertyName("url")] string? Url);

public sealed record JsonResumeProject(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("keywords")] List<string> Keywords,
    [property: JsonPropertyName("url")] string? Url);
