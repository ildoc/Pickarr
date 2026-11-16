using Hangfire;
using Hangfire.Common;
using Hangfire.Storage.SQLite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Pickarr.Infrastructure.Jobs;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class HangfireSqliteStorageTests
{
    [Fact]
    public void Sqlite_storage_survives_restart_and_keeps_recurring_job()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "Pickarr-hf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataPath);
        var sqlitePath = Path.Combine(dataPath, "jobs.db");

        try
        {
            using (var storage1 = CreateStorage(sqlitePath))
            {
                var manager = new RecurringJobManager(storage1);
                manager.AddOrUpdate(
                    ScheduleReconcilerStub.ProbeJobId,
                    Job.FromExpression(() => SpikeProbeStatic.Execute()),
                    "0 5 * * *");

                RecurringJobIds(storage1).ShouldContain(ScheduleReconcilerStub.ProbeJobId);
            }

            File.Exists(sqlitePath).ShouldBeTrue();

            using var storage2 = CreateStorage(sqlitePath);
            RecurringJobIds(storage2).ShouldContain(
                ScheduleReconcilerStub.ProbeJobId,
                customMessage: "SQLite Hangfire storage must retain recurring jobs across restarts");
        }
        finally
        {
            TryDeleteDirectory(dataPath);
        }
    }

    [Fact]
    public void ScheduleReconciler_reapplies_probe_after_storage_reset()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "Pickarr-hf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataPath);
        var sqlitePath = Path.Combine(dataPath, "jobs.db");

        try
        {
            using (var storage1 = CreateStorage(sqlitePath))
            {
                var manager = new RecurringJobManager(storage1);
                var reconciler = new ScheduleReconcilerStub(manager, NullLogger<ScheduleReconcilerStub>.Instance);
                reconciler.ReconcileProbe("0 6 * * *");
                RecurringJobIds(storage1).ShouldContain(ScheduleReconcilerStub.ProbeJobId);
            }

            File.Delete(sqlitePath);
            File.Exists(sqlitePath).ShouldBeFalse();

            using var storage2 = CreateStorage(sqlitePath);
            var manager2 = new RecurringJobManager(storage2);
            var reconciler2 = new ScheduleReconcilerStub(manager2, NullLogger<ScheduleReconcilerStub>.Instance);
            reconciler2.ReconcileProbe("0 6 * * *");

            RecurringJobIds(storage2).ShouldContain(ScheduleReconcilerStub.ProbeJobId);

            using var connection = storage2.GetConnection();
            var entries = connection.GetAllEntriesFromHash($"recurring-job:{ScheduleReconcilerStub.ProbeJobId}");
            entries.ShouldNotBeNull();
            entries["Cron"].ShouldBe("0 6 * * *");
        }
        finally
        {
            TryDeleteDirectory(dataPath);
        }
    }

    [Fact]
    public void Corrupt_jobs_are_archived_with_sidecars_and_recreated_without_touching_personal_data()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "Pickarr-hf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataPath, "keys"));
        var sqlitePath = Path.Combine(dataPath, "jobs.db");
        var mainPath = Path.Combine(dataPath, "Pickarr.db");
        var keyPath = Path.Combine(dataPath, "keys", "test-key.xml");
        byte[] mainBytes = [1, 2, 3, 4];
        byte[] keyBytes = [5, 6, 7, 8];
        File.WriteAllBytes(mainPath, mainBytes);
        File.WriteAllBytes(keyPath, keyBytes);
        var originals = new Dictionary<string, byte[]>();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            byte[] bytes = [9, 10, 11, 12];
            originals[suffix] = bytes;
            File.WriteAllBytes(sqlitePath + suffix, bytes);
        }

        try
        {
            using (var storage = CreateStorage(sqlitePath))
            {
                var manager = new RecurringJobManager(storage);
                manager.RemoveIfExists("legacy-job");
                new ScheduleReconcilerStub(manager, NullLogger<ScheduleReconcilerStub>.Instance)
                    .ReconcileProbe("0 6 * * *");
                RecurringJobIds(storage).ShouldContain(ScheduleReconcilerStub.ProbeJobId);
            }

            var archive = Directory.GetDirectories(dataPath, "jobs.db.corrupt-*").ShouldHaveSingleItem();
            foreach (var (suffix, bytes) in originals)
            {
                File.ReadAllBytes(Path.Combine(archive, "jobs.db" + suffix)).ShouldBe(bytes);
            }
            File.ReadAllBytes(mainPath).ShouldBe(mainBytes);
            File.ReadAllBytes(keyPath).ShouldBe(keyBytes);

            using var restarted = CreateStorage(sqlitePath);
            RecurringJobIds(restarted).ShouldContain(ScheduleReconcilerStub.ProbeJobId);
            Directory.GetDirectories(dataPath, "jobs.db.corrupt-*").Length.ShouldBe(1);
        }
        finally
        {
            TryDeleteDirectory(dataPath);
        }
    }

    [Fact]
    public void Index_corruption_is_detected_even_when_storage_construction_succeeds()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "Pickarr-hf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataPath);
        var sqlitePath = Path.Combine(dataPath, "jobs.db");

        try
        {
            long indexPage;
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                Pooling = false,
            }.ToString()))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = """
                    PRAGMA page_size=4096;
                    CREATE TABLE Probe (Value TEXT);
                    INSERT INTO Probe VALUES ('one'), ('two');
                    CREATE INDEX Probe_Value ON Probe(Value);
                    """;
                command.ExecuteNonQuery();
                command.CommandText = "SELECT rootpage FROM sqlite_master WHERE name='Probe_Value'";
                indexPage = command.ExecuteScalar().ShouldBeOfType<long>();
            }
            using (var file = new FileStream(sqlitePath, FileMode.Open, FileAccess.Write))
            {
                file.Position = (indexPage - 1) * 4096 + 3;
                file.Write([0, 0]);
            }

            using (var uncheckedStorage = new SQLiteStorage(sqlitePath, new SQLiteStorageOptions
            {
                JournalMode = SQLiteStorageOptions.JournalModes.WAL,
            }))
            {
                File.Exists(sqlitePath).ShouldBeTrue();
            }

            using var recovered = CreateStorage(sqlitePath);
            new RecurringJobManager(recovered).RemoveIfExists("legacy-job");
            Directory.GetDirectories(dataPath, "jobs.db.corrupt-*").ShouldHaveSingleItem();
        }
        finally
        {
            TryDeleteDirectory(dataPath);
        }
    }

    [Fact]
    public void Healthy_storage_with_committed_wal_is_not_archived()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "Pickarr-hf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataPath);
        var sqlitePath = Path.Combine(dataPath, "jobs.db");

        try
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                Pooling = false,
            }.ToString());
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA wal_autocheckpoint=0;
                CREATE TABLE Probe (Value TEXT);
                INSERT INTO Probe VALUES ('committed');
                """;
            command.ExecuteNonQuery();
            new FileInfo(sqlitePath + "-wal").Length.ShouldBeGreaterThan(0);

            HangfireBootstrap.PrepareSqliteStorage(sqlitePath, NullLogger.Instance);

            Directory.GetDirectories(dataPath, "jobs.db.corrupt-*").ShouldBeEmpty();
            command.CommandText = "SELECT Value FROM Probe";
            command.ExecuteScalar().ShouldBe("committed");
        }
        finally
        {
            TryDeleteDirectory(dataPath);
        }
    }

    [Theory]
    [InlineData("Pickarr.db")]
    [InlineData("Pickarr.db-wal")]
    [InlineData("Pickarr.db.")]
    [InlineData("Pickarr.db ")]
    [InlineData("../Pickarr.db")]
    public void Unsafe_storage_configuration_cannot_reset_the_main_database(string fileName)
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "Pickarr-hf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataPath);
        var mainPath = Path.Combine(dataPath, "Pickarr.db");
        byte[] original = [1, 2, 3, 4];
        File.WriteAllBytes(mainPath, original);

        try
        {
            Should.Throw<InvalidOperationException>(() => HangfireBootstrap.ConfigureStorage(
                GlobalConfiguration.Configuration,
                new HangfireOptions { SqliteFileName = fileName },
                dataPath,
                NullLogger.Instance));
            File.ReadAllBytes(mainPath).ShouldBe(original);
            Directory.GetDirectories(dataPath).ShouldBeEmpty();
        }
        finally
        {
            TryDeleteDirectory(dataPath);
        }
    }

    private static IReadOnlyCollection<string> RecurringJobIds(SQLiteStorage storage)
    {
        using var connection = storage.GetConnection();
        return connection.GetAllItemsFromSet("recurring-jobs");
    }

    private static SQLiteStorage CreateStorage(string sqlitePath)
    {
        HangfireBootstrap.PrepareSqliteStorage(sqlitePath, NullLogger.Instance);
        return new(sqlitePath, new SQLiteStorageOptions
        {
            JournalMode = SQLiteStorageOptions.JournalModes.WAL,
        });
    }

    private static void TryDeleteDirectory(string dataPath)
    {
        try
        {
            if (Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, recursive: true);
            }
        }
        catch
        {
            // best-effort on Windows file locks
        }
    }
}

/// <summary>Static target so Hangfire can serialize without DI in storage-only tests.</summary>
public static class SpikeProbeStatic
{
    public static void Execute()
    {
    }
}
