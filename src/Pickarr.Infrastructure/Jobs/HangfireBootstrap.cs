using Hangfire;
using Hangfire.InMemory;
using Hangfire.Server;
using Hangfire.Storage.SQLite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pickarr.Infrastructure.Jobs;

public static class HangfireBootstrap
{
    public static IServiceCollection AddPickarrHangfire(
        this IServiceCollection services,
        IConfiguration configuration,
        string dataPath)
    {
        services.Configure<HangfireOptions>(configuration.GetSection(HangfireOptions.SectionName));

        var options = configuration.GetSection(HangfireOptions.SectionName).Get<HangfireOptions>()
                      ?? new HangfireOptions();

        // Hangfire's AddHangfire stores JobStorage.Current and JobActivator.Current in statics.
        // Parallel WebApplicationFactory hosts would otherwise share one storage and run each
        // other's jobs. Register the in-memory instances first so TryAdd keeps them per host.
        if (options.Storage == HangfireStorageKind.Memory)
        {
            services.AddSingleton<InMemoryStorage>(_ => new InMemoryStorage());
            services.AddSingleton<JobStorage>(sp => sp.GetRequiredService<InMemoryStorage>());
            services.AddSingleton<JobActivator, HostJobActivator>();
        }

        services.AddHangfire((sp, global) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Pickarr.Hangfire");
            if (options.Storage == HangfireStorageKind.Memory)
            {
                global.UseStorage(sp.GetRequiredService<InMemoryStorage>());
                logger.LogInformation("Hangfire storage: InMemory (configured)");
                return;
            }

            ConfigureStorage(global, options, dataPath, logger);
        });

        services.AddHangfireServer(server =>
        {
            server.Queues = ["default", "sync", "arr"];
            server.WorkerCount = Math.Max(1, Environment.ProcessorCount / 2);
        });

        // Isolated ML queue so long reco/embedding work does not occupy sync/arr workers (§11.1).
        services.AddHangfireServer(server =>
        {
            server.Queues = ["ml"];
            server.WorkerCount = 1;
            server.ServerName = $"{Environment.MachineName}:ml";
        });

        services.AddSingleton(new HangfireRuntimeInfo(options.Storage, ResolveSqlitePath(dataPath, options)));
        return services;
    }

    public static void ConfigureStorage(
        IGlobalConfiguration global,
        HangfireOptions options,
        string dataPath,
        ILogger logger)
    {
        if (options.Storage == HangfireStorageKind.Memory)
        {
            global.UseInMemoryStorage();
            logger.LogInformation("Hangfire storage: InMemory (configured)");
            return;
        }

        var sqlitePath = ResolveSqlitePath(dataPath, options);
        PrepareSqliteStorage(sqlitePath, logger);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath)!);
            global.UseSQLiteStorage(sqlitePath, new SQLiteStorageOptions
            {
                JournalMode = SQLiteStorageOptions.JournalModes.WAL,
                QueuePollInterval = TimeSpan.FromSeconds(2),
            });
            logger.LogInformation("Hangfire storage: SQLite at {Path}", sqlitePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hangfire SQLite storage failed; falling back to InMemory");
            global.UseInMemoryStorage();
        }
    }

    public static string ResolveSqlitePath(string dataPath, HangfireOptions options)
    {
        var name = options.SqliteFileName;
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\') || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Equals("Pickarr.db", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Pickarr.db-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Hangfire:SqliteFileName must be a file name under the data path, separate from Pickarr.db and its sidecars.");
        }

        return Path.Combine(dataPath, name);
    }

    internal static void PrepareSqliteStorage(string sqlitePath, ILogger logger)
    {
        if (!File.Exists(sqlitePath))
        {
            return;
        }

        if ((File.GetAttributes(sqlitePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Hangfire SQLite storage must not be a symbolic link.");
        }

        string? corruption = null;
        var suffixes = new[] { "", "-wal", "-shm", "-journal" };
        var checkDirectory = $"{sqlitePath}.check-{Guid.NewGuid():N}";
        Directory.CreateDirectory(checkDirectory);
        try
        {
            var checkPath = Path.Combine(checkDirectory, Path.GetFileName(sqlitePath));
            foreach (var suffix in suffixes)
            {
                if (File.Exists(sqlitePath + suffix))
                {
                    File.Copy(sqlitePath + suffix, checkPath + suffix);
                }
            }

            // Check a copy with WAL/journal recovery, before Hangfire opens or modifies the originals.
            try
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = checkPath,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false,
                }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var result = reader.GetString(0);
                    if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        corruption = result;
                        break;
                    }
                }
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26) // SQLITE_CORRUPT / SQLITE_NOTADB
            {
                corruption = ex.Message;
            }
        }
        finally
        {
            Directory.Delete(checkDirectory, recursive: true);
        }

        if (corruption is null)
        {
            return;
        }

        var archivePath = $"{sqlitePath}.corrupt-{Guid.NewGuid():N}";
        logger.LogWarning(
            "Corrupt Hangfire SQLite storage at {Path}: {Reason}. Archiving at {ArchivePath} before recreating storage; pending jobs and history will not be restored",
            sqlitePath, corruption, archivePath);
        Directory.CreateDirectory(archivePath);
        // Move the main file last so interrupted archival cannot leave old WAL beside a fresh database.
        foreach (var suffix in suffixes.Reverse())
        {
            var source = sqlitePath + suffix;
            if (File.Exists(source))
            {
                File.Move(source, Path.Combine(archivePath, Path.GetFileName(source)));
            }
        }
    }
}

/// <summary>Probeable runtime choice for tests and readiness.</summary>
public sealed record HangfireRuntimeInfo(HangfireStorageKind RequestedStorage, string? SqlitePath);

/// <summary>
/// Resolves jobs from this host's container. Hangfire's default activator is process-wide,
/// so a second host started in the same process would steal it.
/// </summary>
internal sealed class HostJobActivator(IServiceScopeFactory scopeFactory) : JobActivator
{
    public override JobActivatorScope BeginScope(JobActivatorContext context) => CreateScope();

    public override JobActivatorScope BeginScope(PerformContext context) => CreateScope();

    [Obsolete("Please use the BeginScope(JobActivatorContext) method instead. Will be removed in 2.0.0.")]
    public override JobActivatorScope BeginScope() => CreateScope();

    private JobActivatorScope CreateScope() => new DependencyScope(scopeFactory.CreateScope());

    private sealed class DependencyScope(IServiceScope scope) : JobActivatorScope
    {
        public override object Resolve(Type type) => scope.ServiceProvider.GetRequiredService(type);

        public override void DisposeScope() => scope.Dispose();
    }
}
