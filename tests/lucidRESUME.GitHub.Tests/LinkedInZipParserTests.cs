using lucidRESUME.Ingestion.LinkedIn;
using System.IO.Compression;

namespace lucidRESUME.GitHub.Tests;

public class LinkedInZipParserTests
{
    [Fact]
    public void IsLinkedInExport_WithLinkedInZip_ReturnsTrue()
    {
        var path = CreateTestLinkedInZip();
        try
        {
            Assert.True(LinkedInZipParser.IsLinkedInExport(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsLinkedInExport_WithNonZip_ReturnsFalse()
    {
        Assert.False(LinkedInZipParser.IsLinkedInExport("/tmp/test.pdf"));
    }

    [Fact]
    public void IsLinkedInExport_WithRegularZip_ReturnsFalse()
    {
        var path = Path.GetTempFileName() + ".zip";
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("not linkedin");
        }
        try
        {
            Assert.False(LinkedInZipParser.IsLinkedInExport(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ParseAsync_ExtractsProfileData()
    {
        var path = CreateTestLinkedInZip();
        var parser = new LinkedInZipParser(new Microsoft.Extensions.Logging.Abstractions.NullLogger<LinkedInZipParser>());
        try
        {
            var resume = await parser.ParseAsync(path);
            Assert.Equal("John Doe", resume.Personal.FullName);
            Assert.Equal("London, UK", resume.Personal.Location);
            Assert.Equal(2, resume.Experience.Count);
            Assert.Equal(2, resume.Skills.Count);
            Assert.Equal("Acme Corp", resume.Experience[0].Company);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ParseAsync_ParsesSkillEndorsements()
    {
        var path = CreateTestLinkedInZip(includeEndorsements: true);
        var parser = new LinkedInZipParser(new Microsoft.Extensions.Logging.Abstractions.NullLogger<LinkedInZipParser>());
        try
        {
            var resume = await parser.ParseAsync(path);
            var csharp = resume.Skills.FirstOrDefault(s => s.Name == "C#");
            Assert.NotNull(csharp);
            Assert.Equal(3, csharp.EndorsementCount);
        }
        finally { File.Delete(path); }
    }

    private static string CreateTestLinkedInZip(bool includeEndorsements = false)
    {
        var path = Path.GetTempFileName() + ".zip";
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        AddCsv(zip, "Profile.csv",
            "First Name,Last Name,Maiden Name,Address,Birth Date,Headline,Summary,Industry,Zip Code,Geo Location\n" +
            "John,Doe,,,,,Tech lead,Software,,\"London, UK\"");

        AddCsv(zip, "Positions.csv",
            "Company Name,Title,Description,Location,Started On,Finished On\n" +
            "Acme Corp,Senior Developer,Built stuff,London,Jan 2020,Jun 2023\n" +
            "Globex Inc,Lead Engineer,Led team,Remote,Jul 2023,");

        AddCsv(zip, "Skills.csv",
            "Name\nC#\nDocker");

        AddCsv(zip, "Education.csv",
            "School Name,Start Date,End Date,Notes,Degree Name,Activities\n" +
            "MIT,2012,2016,,BSc Computer Science,");

        if (includeEndorsements)
        {
            AddCsv(zip, "Endorsement_Received_Info.csv",
                "Skill Name\nC#\nC#\nC#");
        }

        return path;
    }

    private static void AddCsv(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
