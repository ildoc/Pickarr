using Microsoft.Data.Sqlite;
using Pickarr.RecoBench;
using Shouldly;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Pickarr.Infrastructure.Tests;

public sealed class RecoSnapshotTests : IDisposable
{
    private readonly string root = Path.Combine(FindRepo(), "obj", "reco-snapshot-tests", Guid.NewGuid().ToString("N"));
    private readonly string inputDirectory;
    private readonly string outputDirectory;
    private readonly string protectedDirectory;

    public RecoSnapshotTests()
    {
        inputDirectory = Path.Combine(root, "input");
        outputDirectory = Path.Combine(root, "output");
        protectedDirectory = Path.Combine(root, "protected");
        Directory.CreateDirectory(inputDirectory);
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(protectedDirectory);
    }

    [Fact]
    public void Backup_reads_committed_wal_not_uncommitted_and_input_remains_usable()
    {
        var source = Path.Combine(inputDirectory, "live.db");
        using var writer = CreateDatabase(source);
        Execute(writer, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; INSERT INTO Titles VALUES (1); INSERT INTO Profiles VALUES (1); INSERT INTO Ratings VALUES (1, 1)");
        File.Exists(source + "-wal").ShouldBeTrue();
        using var transaction = writer.BeginTransaction();
        using (var pending = writer.CreateCommand())
        {
            pending.Transaction = transaction;
            pending.CommandText = "INSERT INTO Titles VALUES (2)";
            pending.ExecuteNonQuery();
        }

        var snapshot = Path.Combine(outputDirectory, "snapshot.db");
        var counts = Snapshot.Create(source, snapshot, protectedDirectory);
        counts.ShouldBe(new SnapshotCounts(1, 1, 1));
        transaction.Commit();
        Scalar(writer, "SELECT count(*) FROM Titles").ShouldBe(2);
        Scalar(writer, "SELECT count(*) FROM Ratings").ShouldBe(1);
        var resultDirectory = Directory.CreateDirectory(Path.Combine(root, "output2")).FullName;
        using var reader = Snapshot.OpenBenchmark(snapshot, Path.Combine(resultDirectory, "result.json"), protectedDirectory);
        Scalar(reader, "SELECT count(*) FROM Titles").ShouldBe(1);
    }

    [Fact]
    public void Source_counts_read_only_committed_wal_and_never_create_missing_input()
    {
        var source = Path.Combine(inputDirectory, "live.db");
        using var writer = CreateDatabase(source);
        Execute(writer, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; INSERT INTO Titles VALUES (1); INSERT INTO Profiles VALUES (1); INSERT INTO Ratings VALUES (1, 1)");
        using (var transaction = writer.BeginTransaction())
        {
            using var pending = writer.CreateCommand();
            pending.Transaction = transaction;
            pending.CommandText = "INSERT INTO Titles VALUES (2); INSERT INTO Profiles VALUES (2); INSERT INTO Ratings VALUES (2, 2)";
            pending.ExecuteNonQuery();
            Snapshot.ReadSourceCounts(source).ShouldBe(new SnapshotCounts(1, 1, 1));
            var cli = RunCli("counts", source);
            cli.ExitCode.ShouldBe(0);
            cli.Output.ShouldContain("Source counts: titles=1, profiles=1, ratings=1.");
            cli.Output.ShouldNotContain(source);
            transaction.Commit();
        }
        Snapshot.ReadSourceCounts(source).ShouldBe(new SnapshotCounts(2, 2, 2));
        var missing = Path.Combine(inputDirectory, "missing.db");
        var failure = RunCli("counts", missing);
        failure.ExitCode.ShouldBe(1);
        failure.Output.ShouldNotContain(missing);
        File.Exists(missing).ShouldBeFalse();
    }

    [Fact]
    public void Benchmark_is_read_only_and_rejects_bad_destinations()
    {
        var source = Path.Combine(inputDirectory, "snapshot.db");
        using (var database = CreateDatabase(source))
            Execute(database, "INSERT INTO Titles VALUES (1)");
        var originalHash = SHA256.HashData(File.ReadAllBytes(source));
        var output = Path.Combine(outputDirectory, "result.json");
        using var connection = Snapshot.OpenBenchmark(source, output, protectedDirectory);
        Should.Throw<SqliteException>(() => Execute(connection, "DELETE FROM Titles"));
        Scalar(connection, "SELECT count(*) FROM Titles").ShouldBe(1);
        Should.Throw<ArgumentException>(() => Snapshot.OpenBenchmark(source, source, protectedDirectory));
        Should.Throw<ArgumentException>(() => Snapshot.OpenBenchmark(source, Path.Combine(protectedDirectory, "result"), protectedDirectory));
        Should.Throw<ArgumentException>(() => Snapshot.OpenBenchmark(source, output, inputDirectory));
        Should.Throw<ArgumentException>(() => Snapshot.OpenBenchmark(source, "relative", protectedDirectory));
        Should.Throw<FileNotFoundException>(() => Snapshot.OpenBenchmark(Path.Combine(inputDirectory, "missing.db"), output, protectedDirectory));
        File.WriteAllText(output, "keep");
        Should.Throw<IOException>(() => Snapshot.OpenBenchmark(source, output, protectedDirectory));
        File.ReadAllText(output).ShouldBe("keep");
        if (OperatingSystem.IsWindows())
            Should.Throw<ArgumentException>(() => Snapshot.OpenBenchmark(source, Path.Combine(protectedDirectory.ToUpperInvariant(), "result"), protectedDirectory));
        SHA256.HashData(File.ReadAllBytes(source)).ShouldBe(originalHash);
    }

    [Fact]
    public void Failed_checks_never_overwrite_existing_or_leave_new_output()
    {
        var source = Path.Combine(inputDirectory, "bad.db");
        using (var database = CreateDatabase(source))
            Execute(database, "PRAGMA foreign_keys=OFF; INSERT INTO Ratings VALUES (99, 99)");
        var output = Path.Combine(outputDirectory, "snapshot.db");
        Should.Throw<InvalidOperationException>(() => Snapshot.Create(source, output, protectedDirectory)).Message.ShouldContain("foreign key");
        File.Exists(output).ShouldBeFalse();
        File.WriteAllText(output, "keep");
        Should.Throw<IOException>(() => Snapshot.Create(source, output, protectedDirectory));
        File.ReadAllText(output).ShouldBe("keep");
        var corrupt = Path.Combine(inputDirectory, "corrupt.db");
        File.WriteAllText(corrupt, "not a database");
        var other = Path.Combine(outputDirectory, "other.db");
        Should.Throw<SqliteException>(() => Snapshot.Create(corrupt, other, protectedDirectory));
        File.Exists(other).ShouldBeFalse();
    }

    [Fact]
    public void Integrity_check_rejects_corruption()
    {
        var source = Path.Combine(inputDirectory, "invalid.db");
        using (var database = CreateDatabase(source))
            Execute(database, "CREATE TABLE Checked (Value INTEGER); CREATE INDEX BrokenIndex ON Checked(Value); INSERT INTO Checked VALUES (1); PRAGMA writable_schema=ON; DELETE FROM sqlite_master WHERE name='BrokenIndex'");
        var output = Path.Combine(outputDirectory, "snapshot.db");
        Should.Throw<InvalidOperationException>(() => Snapshot.Create(source, output, protectedDirectory)).Message.ShouldContain("integrity");
        File.Exists(output).ShouldBeFalse();
    }

    [Fact]
    public void Cli_creates_and_verifies_a_snapshot_without_exposing_paths()
    {
        var source = Path.Combine(inputDirectory, "live.db");
        using (var database = CreateDatabase(source))
            Execute(database, "INSERT INTO Titles VALUES (1)");
        var snapshot = Path.Combine(outputDirectory, "snapshot.db");
        var resultDirectory = Directory.CreateDirectory(Path.Combine(root, "results")).FullName;
        var result = Path.Combine(resultDirectory, "result.json");
        var created = RunCli("snapshot", source, snapshot, protectedDirectory);
        created.ExitCode.ShouldBe(0);
        created.Output.ShouldContain("titles=1");
        created.Output.ShouldNotContain(source);
        var verified = RunCli("verify", snapshot, result, protectedDirectory);
        verified.ExitCode.ShouldBe(0);
        verified.Output.ShouldContain("titles=1");
        File.Exists(result).ShouldBeFalse();
        var duplicate = RunCli("snapshot", source, snapshot, protectedDirectory);
        duplicate.ExitCode.ShouldBe(1);
        duplicate.Output.ShouldNotContain(source);
    }

    [Fact]
    public void Rejects_symlinked_paths_and_snapshot_sidecars()
    {
        var source = Path.Combine(inputDirectory, "snapshot.db");
        using (CreateDatabase(source)) { }
        File.WriteAllText(source + "-wal", "");
        Should.Throw<InvalidOperationException>(() => Snapshot.OpenBenchmark(source, Path.Combine(outputDirectory, "result"), protectedDirectory));
        File.Delete(source + "-wal");
        var link = Path.Combine(root, "link");
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec")!,
                ArgumentList = { "/c", "mklink", "/J", link, outputDirectory },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            process.WaitForExit();
            process.ExitCode.ShouldBe(0);
        }
        else
            Directory.CreateSymbolicLink(link, outputDirectory);
        Should.Throw<ArgumentException>(() => Snapshot.Create(source, Path.Combine(link, "snapshot"), protectedDirectory));
        Directory.Delete(link);
        File.WriteAllText(Path.Combine(outputDirectory, "snapshot-wal"), "");
        Should.Throw<IOException>(() => Snapshot.Create(source, Path.Combine(outputDirectory, "snapshot"), protectedDirectory));
    }

    private static SqliteConnection CreateDatabase(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        Execute(connection, "CREATE TABLE Titles (Id INTEGER PRIMARY KEY); CREATE TABLE Profiles (Id INTEGER PRIMARY KEY); CREATE TABLE Ratings (TitleId INTEGER REFERENCES Titles(Id), ProfileId INTEGER REFERENCES Profiles(Id))");
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static string FindRepo()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pickarr.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }

    private static (int ExitCode, string Output) RunCli(params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(Snapshot).Assembly.Location);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    public void Dispose() => Directory.Delete(root, true);
}
