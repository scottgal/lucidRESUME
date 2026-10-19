using System.Text.Json;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace lucidRESUME.Core.Tests;

public sealed class SavedCareerReaderTests
{
    [Fact]
    public async Task ReadsAuthoritativeTranscriptWithoutReconcilingOrChangingStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"saved-career-{Guid.NewGuid():N}.db");
        try
        {
            var transcript = new ResumeDocument
            {
                Personal = new PersonalInfo { FullName = "Saved author" },
                CanonicalMarkdown = "Author-edited career prose",
                Experience = [new WorkExperience { Company = "Reviewed employer", Title = "Lead" }]
            };
            await CreateStoreAsync(path, JsonSerializer.Serialize(new CompleteCareerRecord { Transcript = transcript }));
            var before = await File.ReadAllBytesAsync(path);
            var loaded = await SavedCareerReader.ReadAsync(path);
            Assert.Equal(transcript.ResumeId, loaded.ResumeId);
            Assert.Equal("Author-edited career prose", loaded.CanonicalMarkdown);
            Assert.Equal("Reviewed employer", Assert.Single(loaded.Experience).Company);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
            Assert.False(Directory.Exists(path + ".prose-search"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MissingCareerDoesNotFallBackToImports()
    {
        var path = Path.Combine(Path.GetTempPath(), $"saved-career-{Guid.NewGuid():N}.db");
        try
        {
            await CreateStoreAsync(path, "null");
            await Assert.ThrowsAsync<InvalidDataException>(() => SavedCareerReader.ReadAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MissingDatabaseIsNotCreated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-career-{Guid.NewGuid():N}.db");
        await Assert.ThrowsAsync<SqliteException>(() => SavedCareerReader.ReadAsync(path));
        Assert.False(File.Exists(path));
    }

    private static async Task CreateStoreAsync(string path, string json)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE app_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL); INSERT INTO app_meta VALUES ('complete_career', $json);";
        command.Parameters.AddWithValue("$json", json);
        await command.ExecuteNonQueryAsync();
    }
}
