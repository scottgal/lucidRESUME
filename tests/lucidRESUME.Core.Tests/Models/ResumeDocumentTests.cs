using lucidRESUME.Core.Models.Extraction;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Tests.Models;

public class ResumeDocumentTests
{
    [Fact]
    public void Create_SetsIdAndTimestamp()
    {
        var doc = ResumeDocument.Create("resume.pdf", "application/pdf", 12345);
        Assert.NotEqual(Guid.Empty, doc.ResumeId);
        Assert.Equal("resume.pdf", doc.FileName);
        Assert.True(doc.CreatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void AddEntity_AppendsToEntities()
    {
        var doc = ResumeDocument.Create("resume.pdf", "application/pdf", 12345);
        var entity = ExtractedEntity.Create("John Smith", "PersonName", DetectionSource.Ner, 0.95, 1);
        doc.AddEntity(entity);
        Assert.Single(doc.Entities);
        Assert.Equal("John Smith", doc.Entities[0].Value);
    }
}
