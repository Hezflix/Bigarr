using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Server.Backup;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Tests.Auth;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Server;

/// <summary>Backups (M7): a zip of a consistent database snapshot, restored by staging the file and restarting.</summary>
public sealed class BackupTests
{
    [Fact]
    public async Task A_backup_is_a_zip_of_the_database_and_restoring_it_brings_the_old_state_back_after_a_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bigarr-backup-test-{Guid.NewGuid():N}");
        var library = Directory.CreateTempSubdirectory("bigarr-backup-lib-").FullName;
        try
        {
            int before;
            string backupName;
            await using (var factory = new ConfiguredBigarrApiFactory(directory, null, deleteOnDispose: false))
            {
                var client = await ClientAsync(factory);
                var (hd, _) = await ProfilesAsync(client);
                var root = await AddRootAsync(client, library);
                var first = await AddMovieAsync(client, 603, hd, root.Id);
                before = first.Id;

                var created = await client.PostAsync("/api/v1/system/backup", null);
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                var backup = (await created.Content.ReadFromJsonAsync<BackupResponse>(Json))!;
                backupName = backup.Name;
                Assert.StartsWith("bigarr_backup_v", backupName, StringComparison.Ordinal);
                Assert.Equal("manual", backup.Type);
                Assert.True(backup.Size > 0);

                var listed = (await client.GetFromJsonAsync<List<BackupResponse>>("/api/v1/system/backup", Json))!;
                Assert.Equal(backupName, Assert.Single(listed).Name);

                var download = await client.GetAsync($"/api/v1/system/backup/{backupName}/download");
                Assert.Equal(HttpStatusCode.OK, download.StatusCode);
                Assert.Equal("application/zip", download.Content.Headers.ContentType!.MediaType);
                using (var zip = new ZipArchive(await download.Content.ReadAsStreamAsync()))
                {
                    Assert.NotNull(zip.GetEntry(BackupService.DatabaseEntry));
                    using var manifest = JsonDocument.Parse(await new StreamReader(zip.GetEntry(BackupService.ManifestEntry)!.Open()).ReadToEndAsync());
                    Assert.Equal(BackupService.DatabaseEntry, manifest.RootElement.GetProperty("database").GetString());
                }

                Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/v1/system/backup/not_a_backup.zip")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/system/backup/bigarr_backup_missing.zip/download")).StatusCode);

                var tasks = (await client.GetFromJsonAsync<List<ScheduledTaskResponse>>("/api/v1/system/tasks", Json))!;
                Assert.Equal(7 * 24 * 60, tasks.Single(t => t.Name == BackupJob.JobName).IntervalMinutes);

                // A second film after the backup: the restore must forget it.
                await AddMovieAsync(client, 27205, hd, root.Id);
                Assert.Equal(2, (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.Count);

                var restore = await client.PostAsync($"/api/v1/system/backup/{backupName}/restore", null);
                Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
                var outcome = (await restore.Content.ReadFromJsonAsync<RestoreResponse>(Json))!;
                Assert.True(outcome.RestartRequested);
                Assert.True(File.Exists(Path.Combine(directory, "bigarr.db" + BackupService.StagedRestoreSuffix)));
            }

            // "The restart": a new host on the same directory swaps the staged database in before migrating.
            await using (var factory = new ConfiguredBigarrApiFactory(directory, null, deleteOnDispose: false))
            {
                var client = await ClientAsync(factory);
                var movies = (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!;
                Assert.Equal(before, Assert.Single(movies).Id);
                Assert.False(File.Exists(Path.Combine(directory, "bigarr.db" + BackupService.StagedRestoreSuffix)));
                Assert.True(File.Exists(Path.Combine(directory, "bigarr.db.before-restore")));

                Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/system/backup/{backupName}")).StatusCode);
                Assert.Empty((await client.GetFromJsonAsync<List<BackupResponse>>("/api/v1/system/backup", Json))!);
            }
        }
        finally
        {
            TryDelete(directory);
            TryDelete(library);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
