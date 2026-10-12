using System.Runtime.InteropServices;
using System.Text.Json;
using lucidRESUME.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace lucidRESUME.Services;

/// <summary>Checks the shipped SQLite provider and vector extension without opening user data.</summary>
internal static class StorageAudit
{
    public static async Task<int> RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucidRESUME storage audit {Guid.NewGuid():N}");
        var path = Path.Combine(directory, "career Ω.db");
        var checks = new List<string>();
        try
        {
            Directory.CreateDirectory(directory);
            var migration = Path.Combine(directory, "data.json");
            var initial = new AppState();
            initial.Profile.DisplayName = "SQLite migration Ω";
            await File.WriteAllTextAsync(migration, JsonSerializer.Serialize(initial));
            var small = new float[384];
            var large = new float[768];
            small[0] = large[0] = 1;
            using (var store = new SqliteAppStore(path, migration))
            {
                var state = await store.LoadAsync();
                Require(state.Profile.DisplayName == initial.Profile.DisplayName && File.Exists(migration + ".bak"), "JSON migration");
                state.Profile.DisplayName = "SQLite persisted Ω";
                await store.SaveAsync(state);
                await store.Vectors.UpsertAsync(1, small, "audit", "384", "SQLite native vector 384");
                await store.Vectors.UpsertAsync(2, large, "audit", "768", "SQLite native vector 768");
            }
            checks.Add("create-schema-migrate-write");

            // Relocation also catches native handles surviving store disposal on Windows.
            var moved = Path.Combine(directory, "reopened career Ω.db");
            File.Move(path, moved);
            using (var reopened = new SqliteAppStore(moved))
            {
                Require((await reopened.LoadAsync()).Profile.DisplayName == "SQLite persisted Ω", "state persistence");
                Require(await reopened.Vectors.CountAsync() == 2, "vector persistence");
                var nearSmall = await reopened.Vectors.SearchAsync(small, 1);
                var nearLarge = await reopened.Vectors.SearchAsync(large, 1);
                Require(nearSmall.Count == 1 && nearSmall[0].SourceId == "384" && nearSmall[0].Distance == 0, "384-dimension native search");
                Require(nearLarge.Count == 1 && nearLarge[0].SourceId == "768" && nearLarge[0].Distance == 0, "768-dimension native search");
            }
            checks.Add("dispose-relocate-reopen-persist");
            checks.Add("sqlite-vec-384-and-768-search");

            string version;
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = moved, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                connection.LoadVector();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT sqlite_version()";
                version = (string)(await command.ExecuteScalarAsync())!;
                command.CommandText = "PRAGMA integrity_check";
                Require((string)(await command.ExecuteScalarAsync())! == "ok", "database integrity");
            }
            checks.Add("integrity-check");
            Directory.Delete(directory, recursive: true);
            checks.Add("release-handles-cleanup");
            Console.WriteLine(JsonSerializer.Serialize(new { complete = true, architecture = RuntimeInformation.ProcessArchitecture.ToString(), sqliteVersion = version, checks }));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { complete = false, architecture = RuntimeInformation.ProcessArchitecture.ToString(), checks, error = error.ToString(), diagnosticDirectory = directory }));
            return 2;
        }
    }

    private static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException($"Storage audit failed: {check}");
    }
}
