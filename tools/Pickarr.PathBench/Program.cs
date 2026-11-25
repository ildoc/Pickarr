using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Recommendations;
using Pickarr.Infrastructure.Catalog;
using Pickarr.Infrastructure.Persistence;
using Pickarr.PathBench;
using Pickarr.RecoBench;

try
{
    if (args is ["synthetic"])
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
        connection.Open();
        Console.WriteLine(await Report(connection, true, null));
        return 0;
    }
    if (args.Length < 5 || args[0] != "snapshot")
    {
        Console.Error.WriteLine("synthetic | snapshot INPUT EXPECTED_SHA256 NEW_PRIVATE_REPORT PROTECTED_ROOT [PROTECTED_ROOT...]");
        return 2;
    }
    var (input, expected, output, roots) = (args[1], args[2], args[3], args[4..]);
    _ = Snapshot.ValidateBenchmarkPaths(input, output, roots);
    if (!File.GetAttributes(input).HasFlag(FileAttributes.ReadOnly)) throw new InvalidDataException("Input not read-only.");
    using var locked = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
    string InputHash()
    {
        locked.Position = 0;
        return Convert.ToHexString(SHA256.HashData(locked));
    }
    if (!string.Equals(InputHash(), expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Input digest mismatch.");
    var scratch = Path.Combine(Path.GetDirectoryName(output)!, $"pathbench-{Guid.NewGuid():N}.sqlite");
    bool owned = false;
    string report;
    try
    {
        using (var copy = Snapshot.CreateBenchmarkOutput(input, scratch, roots))
        {
            owned = true;
            locked.Position = 0;
            locked.CopyTo(copy);
        }
        using var working = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = scratch, Mode = SqliteOpenMode.ReadWrite, Pooling = false
        }.ToString());
        working.Open();
        using var pragmas = working.CreateCommand();
        pragmas.CommandText = "PRAGMA temp_store=MEMORY; PRAGMA journal_mode=DELETE; PRAGMA foreign_keys=ON";
        pragmas.ExecuteNonQuery();
        Snapshot.Verify(working);
        report = await Report(working, false, expected.ToUpperInvariant());
    }
    finally
    {
        if (owned)
        {
            foreach (var suffix in new[] { "-journal", "-wal", "-shm", "" })
                File.Delete(scratch + suffix);
        }
        if (!File.GetAttributes(input).HasFlag(FileAttributes.ReadOnly)
            || !string.Equals(InputHash(), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Input changed.");
    }
    using var stream = Snapshot.CreateBenchmarkOutput(input, output, roots);
    using var writer = new StreamWriter(stream);
    writer.Write(report);
    return 0;
}
catch (Exception e)
{
    // Exceptions may contain SQL values or private paths. Do not send them to CI.
    Console.Error.WriteLine($"Path benchmark failed ({e.GetType().Name}); no certified result.");
    if (e.GetBaseException() is SqliteException sql)
    {
        var schema = System.Text.RegularExpressions.Regex.Match(sql.Message,
            @"no such (?:column|table)|too many SQL variables|too many terms in compound SELECT|Expression tree is too large|no such function|cannot change into wal mode from within a transaction", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Console.Error.WriteLine($"SQLite code {sql.SqliteExtendedErrorCode}; {schema.Value}");
    }
    return 1;
}

static async Task<string> Report(SqliteConnection connection, bool synthetic, string? digest)
{
    using var db = new PickarrDbContext(new DbContextOptionsBuilder<PickarrDbContext>()
        .UseSqlite(connection, sqlite => sqlite.UseParameterizedCollectionMode(ParameterTranslationMode.Parameter)).Options);
    var measurements = await Measure.Run(db, synthetic);
    using var process = Process.GetCurrentProcess();
    static string AssemblyHash(Type t) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(t.Assembly.Location)));
    return JsonSerializer.Serialize(new
    {
        protocol = "pathbench-v1", synthetic, snapshotSha256 = digest,
        observedAt = new Clock().UtcNow, model = RecoModelCatalog.DefaultModelId,
        dimensions = RecoModelCatalog.EmbeddingDimensions, level3 = false,
        cpuCount = Environment.ProcessorCount, runtime = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription, arch = RuntimeInformation.ProcessArchitecture.ToString(),
        infrastructure = AssemblyHash(typeof(RecoBuildService)), core = AssemblyHash(typeof(RecoModelCatalog)),
        processPeakBytes = process.PeakWorkingSet64, measurements
    }, new JsonSerializerOptions { WriteIndented = true });
}
