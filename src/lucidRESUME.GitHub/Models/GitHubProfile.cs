using System.Text.Json.Serialization;

namespace lucidRESUME.GitHub.Models;

public sealed class GitHubProfile
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("bio")]
    public string? Bio { get; set; }

    [JsonPropertyName("company")]
    public string? Company { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("blog")]
    public string? Blog { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("twitter_username")]
    public string? TwitterUsername { get; set; }

    [JsonPropertyName("hireable")]
    public bool? Hireable { get; set; }

    [JsonPropertyName("public_repos")]
    public int PublicRepos { get; set; }

    [JsonPropertyName("followers")]
    public int Followers { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }
}
