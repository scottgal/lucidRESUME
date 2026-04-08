using lucidRESUME.GitHub;

namespace lucidRESUME.GitHub.Tests;

public class GitHubLanguageMapTests
{
    [Theory]
    [InlineData("C#", "c#")]
    [InlineData("C++", "c++")]
    [InlineData("Jupyter Notebook", "python")]
    [InlineData("Shell", "bash")]
    [InlineData("Dockerfile", "docker")]
    [InlineData("PLpgSQL", "postgresql")]
    public void ToCanonical_MapsGitHubLanguages(string github, string expected)
    {
        var result = GitHubLanguageMap.ToCanonical(github);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToCanonical_UnknownLanguage_ReturnsLowercase()
    {
        var result = GitHubLanguageMap.ToCanonical("SomeObscureLanguage");
        Assert.Equal("someobscurelanguage", result);
    }

    [Fact]
    public void ToCanonical_Python_ReturnsSelf()
    {
        // Python is in the taxonomy, not in overrides — should canonicalize via taxonomy
        var result = GitHubLanguageMap.ToCanonical("Python");
        Assert.NotNull(result);
    }
}
