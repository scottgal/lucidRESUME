using System.Text.Json;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Data.Sqlite;

namespace lucidRESUME.Core.Persistence;

/// <summary>Reads the saved transcript without migrating, reconciling or writing the application store.</summary>
public static class SavedCareerReader
{
    public static async Task<IReadOnlyList<Models.Jobs.JobDescription>> ReadJobsAsync(string databasePath, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM jobs";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var jobs = new List<Models.Jobs.JobDescription>();
        while (await reader.ReadAsync(ct))
            if (JsonSerializer.Deserialize<Models.Jobs.JobDescription>(reader.GetString(0), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) is { } job) jobs.Add(job);
        return jobs;
    }
    public static async Task<ResumeDocument> ReadAsync(string databasePath, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_meta WHERE key = 'complete_career'";
        var json = await command.ExecuteScalarAsync(ct) as string;
        var career = string.IsNullOrWhiteSpace(json) ? null :
            JsonSerializer.Deserialize<CompleteCareerRecord>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        if (career?.Transcript is not { } transcript ||
            (transcript.Experience.Count == 0 && transcript.Projects.Count == 0 && transcript.Skills.Count == 0))
            throw new InvalidDataException("No saved complete career transcript. Save your career in lucidRESUME first.");
        return transcript;
    }
}
