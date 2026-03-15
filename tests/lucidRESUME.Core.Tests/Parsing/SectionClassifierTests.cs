using lucidRESUME.Ingestion.Parsing;

namespace lucidRESUME.Core.Tests.Parsing;

public class SectionClassifierTests
{
    [Theory]
    [InlineData("## Work Experience", "Experience")]
    [InlineData("## Employment History", "Experience")]
    [InlineData("## Education", "Education")]
    [InlineData("## Skills", "Skills")]
    [InlineData("## Technical Skills", "Skills")]
    [InlineData("## Certifications", "Certifications")]
    [InlineData("## Projects", "Projects")]
    public void ClassifyHeading_ReturnsSectionName(string heading, string expected)
    {
        var result = SectionClassifier.ClassifyHeading(heading);
        Assert.Equal(expected, result);
    }
}
