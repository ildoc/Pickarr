using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pickarr.RecoBench;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class RecoPreparationTests : IDisposable
{
    private readonly string root = Path.GetFullPath(Path.Combine(
        FindRepo(), "..", "Pickarr.RecoBench.Tests", Guid.NewGuid().ToString("N")));
    private readonly string input;
    private readonly string outputDirectory;

    public RecoPreparationTests()
    {
        input = Path.Combine(root, "input", "synthetic.db");
        outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        Directory.CreateDirectory(outputDirectory);
        using var database = OpenWriter();
        Execute(database, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "preparation.sql")));
    }

    [Fact]
    public void Cli_reports_aggregate_coverage_reproducibly_without_changing_input()
    {
        using (var database = OpenWriter())
            Execute(database, """
                ALTER TABLE Profiles ADD COLUMN Name TEXT;
                UPDATE Profiles SET Name='SYNTHETIC_PRIVATE_NAME';
                ALTER TABLE Titles ADD COLUMN OriginalTitle TEXT;
                UPDATE Titles SET OriginalTitle='SYNTHETIC_PRIVATE_TITLE';
                CREATE TABLE Settings (Key TEXT, Value TEXT);
                INSERT INTO Settings VALUES ('SYNTHETIC_KEY','SYNTHETIC_SECRET_VALUE');
                """);
        var hash = SHA256.HashData(File.ReadAllBytes(input));
        var first = Path.Combine(outputDirectory, "first.json");
        var second = Path.Combine(outputDirectory, "second.json");
        var result = RunCli("prepare", input, first, "42", FindRepo());
        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("diagnostics only");
        result.Output.ShouldNotContain(input);
        RunCli("prepare", input, second, "42", FindRepo()).ExitCode.ShouldBe(0);
        File.ReadAllBytes(first).ShouldBe(File.ReadAllBytes(second));
        File.ReadAllText(first).ShouldNotContain("SYNTHETIC_");
        File.ReadAllText(first).ShouldNotContain(root);
        using var report = JsonDocument.Parse(File.ReadAllText(first));
        var dataset = report.RootElement.GetProperty("dataset");
        dataset.GetProperty("sha256").GetString().ShouldBe(Convert.ToHexString(hash));
        dataset.GetProperty("titles").GetInt64().ShouldBe(8);
        dataset.GetProperty("profiles").GetInt64().ShouldBe(2);
        dataset.GetProperty("ratings").GetInt64().ShouldBe(5);
        var coverage = report.RootElement.GetProperty("coverage");
        coverage.GetProperty("ratedTitles").GetInt64().ShouldBe(4);
        coverage.GetProperty("titleFraction").GetDouble().ShouldBe(0.5);
        coverage.GetProperty("profileFraction").GetDouble().ShouldBe(1);
        coverage.GetProperty("positiveRatings").GetInt64().ShouldBe(3);
        coverage.GetProperty("negativeRatings").GetInt64().ShouldBe(2);
        coverage.GetProperty("timestampedRatings").GetInt64().ShouldBe(5);
        report.RootElement.GetProperty("qualityMetrics").ValueKind.ShouldBe(JsonValueKind.Null);
        report.RootElement.GetProperty("code").GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();
        report.RootElement.GetProperty("environment").GetProperty("runtime").GetString()!.ShouldContain(".NET");
        SHA256.HashData(File.ReadAllBytes(input)).ShouldBe(hash);
        File.Exists(input + "-wal").ShouldBeFalse();
        File.Exists(input + "-shm").ShouldBeFalse();
        File.Exists(input + "-journal").ShouldBeFalse();
    }

    [Theory]
    [InlineData("DELETE FROM Ratings", 0, 0)]
    [InlineData("DELETE FROM Ratings; DELETE FROM Profiles; DELETE FROM Titles", 0, 0)]
    [InlineData("UPDATE Ratings SET Value='unsupported', RatedAt='invalid'", 5, 0)]
    [InlineData("UPDATE Ratings SET Value='not_interested', RatedAt='invalid'", 0, 5)]
    public void Missing_labels_are_not_a_success_and_unknown_values_are_not_positive(
        string sql, int unknown, int negative)
    {
        using (var database = OpenWriter())
            Execute(database, sql);
        var output = Path.Combine(outputDirectory, "sample.json");
        var result = RunCli("prepare", input, output, "7", FindRepo());
        result.ExitCode.ShouldBe(negative == 0 ? 3 : 0);
        using var report = JsonDocument.Parse(File.ReadAllText(output));
        report.RootElement.GetProperty("status").GetString()
            .ShouldBe(negative == 0 ? "insufficient_sample" : "diagnostics_only");
        var coverage = report.RootElement.GetProperty("coverage");
        coverage.GetProperty("unknownRatings").GetInt32().ShouldBe(unknown);
        coverage.GetProperty("negativeRatings").GetInt32().ShouldBe(negative);
        if (report.RootElement.GetProperty("dataset").GetProperty("titles").GetInt64() == 0)
        {
            coverage.GetProperty("titleFraction").ValueKind.ShouldBe(JsonValueKind.Null);
            coverage.GetProperty("profileFraction").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        report.RootElement.GetProperty("qualityMetrics").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Cli_fails_without_reports_for_missing_input_bad_schema_or_invalid_arguments()
    {
        var output = Path.Combine(outputDirectory, "result.json");
        var missing = Path.Combine(root, "input", "missing.db");
        RunCli("prepare", missing, output, "42", FindRepo()).ExitCode.ShouldBe(1);
        File.Exists(missing).ShouldBeFalse();
        File.Exists(output).ShouldBeFalse();
        RunCli("prepare", input, output).ExitCode.ShouldBe(2);
        RunCli("prepare", input, output, "invalid", FindRepo()).ExitCode.ShouldBe(2);
        using (var database = OpenWriter())
            Execute(database, "ALTER TABLE Ratings DROP COLUMN RatedAt");
        var result = RunCli("prepare", input, output, "42", FindRepo());
        result.ExitCode.ShouldBe(1);
        result.Output.ShouldNotContain(input);
        result.Output.ShouldNotContain("RatedAt");
        File.Exists(output).ShouldBeFalse();
    }

    [Fact]
    public void Cli_rejects_protected_coincident_existing_and_sidecar_paths()
    {
        var hash = SHA256.HashData(File.ReadAllBytes(input));
        var output = Path.Combine(outputDirectory, "result.json");
        var protectedOutput = Path.Combine(FindRepo(), "obj", Guid.NewGuid() + ".json");
        var extraProtected = Directory.CreateDirectory(Path.Combine(root, "protected")).FullName;
        RunCli("prepare", input, protectedOutput, "42", FindRepo()).ExitCode.ShouldBe(1);
        File.Exists(protectedOutput).ShouldBeFalse();
        RunCli("prepare", input, input, "42", FindRepo()).ExitCode.ShouldBe(1);
        RunCli("prepare", input, Path.Combine(Path.GetDirectoryName(input)!, "result.json"), "42", FindRepo())
            .ExitCode.ShouldBe(1);
        RunCli("prepare", input, Path.Combine(extraProtected, "result.json"), "42", FindRepo(), extraProtected)
            .ExitCode.ShouldBe(1);
        RunCli("prepare", input, output, "42", FindRepo(), Path.GetDirectoryName(input)!)
            .ExitCode.ShouldBe(1);
        File.WriteAllText(output + "-wal", "");
        RunCli("prepare", input, output, "42", FindRepo()).ExitCode.ShouldBe(1);
        File.Delete(output + "-wal");
        File.WriteAllText(input + "-journal", "");
        RunCli("prepare", input, output, "42", FindRepo()).ExitCode.ShouldBe(1);
        File.Delete(input + "-journal");
        File.WriteAllText(output, "keep");
        RunCli("prepare", input, output, "42", FindRepo()).ExitCode.ShouldBe(1);
        File.ReadAllText(output).ShouldBe("keep");
        SHA256.HashData(File.ReadAllBytes(input)).ShouldBe(hash);
    }

    [Fact]
    public void Reservation_rechecks_paths_after_open_and_does_not_overwrite_a_late_output()
    {
        var output = Path.Combine(outputDirectory, "result.json");
        using var connection = Snapshot.OpenBenchmark(input, output, FindRepo());
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM Ratings";
            Should.Throw<SqliteException>(() => command.ExecuteNonQuery());
        }
        File.WriteAllText(output, "late arrival");
        Should.Throw<IOException>(() => Snapshot.CreateBenchmarkOutput(input, output, FindRepo()));
        File.ReadAllText(output).ShouldBe("late arrival");
        File.Delete(output);
        File.WriteAllText(output + "-shm", "");
        Should.Throw<IOException>(() => Snapshot.CreateBenchmarkOutput(input, output, FindRepo()));
        File.Delete(output + "-shm");
        using (var reservation = Snapshot.CreateBenchmarkOutput(input, output, FindRepo()))
        {
            Should.Throw<IOException>(() => Snapshot.CreateBenchmarkOutput(input, output, FindRepo()));
            reservation.WriteByte(123);
        }
        File.ReadAllBytes(output).ShouldBe(new byte[] { 123 });
    }

    private SqliteConnection OpenWriter()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = input,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
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
