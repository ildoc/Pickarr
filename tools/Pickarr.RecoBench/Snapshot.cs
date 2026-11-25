using Microsoft.Data.Sqlite;

namespace Pickarr.RecoBench;

public sealed record SnapshotCounts(long Titles, long Profiles, long Ratings);

public static class Snapshot
{
    public static SnapshotCounts ReadSourceCounts(string source)
    {
        using var connection = OpenReadOnly(ExistingFile(source));
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT (SELECT count(*) FROM Titles), (SELECT count(*) FROM Profiles), (SELECT count(*) FROM Ratings)";
        using var reader = command.ExecuteReader();
        reader.Read();
        return new SnapshotCounts(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    // Callers must supply the repository and every other protected directory.
    public static SnapshotCounts Create(string source, string destination, params string[] protectedDirectories)
    {
        var sourcePath = ExistingFile(source);
        var destinationPath = NewOutput(destination, sourcePath, protectedDirectories);
        using (new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            using (var input = OpenReadOnly(sourcePath))
            using (var output = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString()))
            {
                output.Open();
                input.BackupDatabase(output);
                using var command = output.CreateCommand();
                command.CommandText = "PRAGMA journal_mode=DELETE";
                if (!string.Equals(command.ExecuteScalar()?.ToString(), "delete", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Could not make snapshot standalone.");
            }

            using var copy = OpenReadOnly(destinationPath);
            return Verify(copy);
        }
        catch
        {
            File.Delete(destinationPath);
            throw;
        }
    }

    // A caller owns the returned connection and must not run application jobs or migrations.
    public static SqliteConnection OpenBenchmark(string snapshot, string output, params string[] protectedDirectories)
    {
        var inputPath = ValidateBenchmarkPaths(snapshot, output, protectedDirectories);
        var connection = OpenReadOnly(inputPath);
        try
        {
            Verify(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public static FileStream CreateBenchmarkOutput(string snapshot, string output, params string[] protectedDirectories)
    {
        var inputPath = ValidateBenchmarkPaths(snapshot, output, protectedDirectories);
        var outputPath = NewOutput(output, inputPath, protectedDirectories);
        return new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    internal static string ValidateBenchmarkPaths(string snapshot, string output, string[] protectedDirectories)
    {
        var inputPath = ExistingFile(snapshot);
        _ = NewOutput(output, inputPath, protectedDirectories);
        foreach (var directory in protectedDirectories)
            if (IsInside(inputPath, Normalize(directory), PathComparison))
                throw new ArgumentException("Snapshot is in a protected directory.");
        if (File.Exists(inputPath + "-wal") || File.Exists(inputPath + "-shm") || File.Exists(inputPath + "-journal"))
            throw new InvalidOperationException("Snapshot has SQLite sidecars; supply a closed, standalone snapshot.");
        return inputPath;
    }

    public static SnapshotCounts Verify(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA ignore_check_constraints=OFF";
        command.ExecuteNonQuery();
        command.CommandText = "PRAGMA integrity_check";
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
                throw new InvalidOperationException("Snapshot integrity check failed.");
        }
        command.CommandText = "PRAGMA foreign_key_check";
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
                throw new InvalidOperationException("Snapshot foreign key check failed.");
        }
        command.CommandText = "SELECT (SELECT count(*) FROM Titles), (SELECT count(*) FROM Profiles), (SELECT count(*) FROM Ratings)";
        using var counts = command.ExecuteReader();
        counts.Read();
        return new SnapshotCounts(counts.GetInt64(0), counts.GetInt64(1), counts.GetInt64(2));
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA query_only=ON";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static string ExistingFile(string path)
    {
        var full = Normalize(path);
        CheckLinks(full);
        if (!File.Exists(full))
            throw new FileNotFoundException("Input database does not exist.");
        return full;
    }

    private static string NewOutput(string output, string input, string[] protectedDirectories)
    {
        if (protectedDirectories.Length == 0)
            throw new ArgumentException("At least the repository directory must be protected.");
        var full = Normalize(output);
        var parent = Path.GetDirectoryName(full) ?? throw new ArgumentException("Output parent is required.");
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("Output parent must already exist.");
        CheckLinks(parent);
        CheckLinks(full);
        if (IsInside(full, Path.GetDirectoryName(input)!, PathComparison))
            throw new ArgumentException("Output must be outside the input directory.");
        foreach (var directory in protectedDirectories)
        {
            var root = Normalize(directory);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("Protected directory must exist.");
            CheckLinks(root);
            if (IsInside(full, root, PathComparison))
                throw new ArgumentException("Output is in a protected directory.");
        }
        if (File.Exists(full) || Directory.Exists(full) || File.Exists(full + "-wal") ||
            File.Exists(full + "-shm") || File.Exists(full + "-journal"))
            throw new IOException("Output or SQLite sidecar already exists.");
        return full;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsInside(string path, string directory, StringComparison comparison)
    {
        var root = Path.TrimEndingDirectorySeparator(directory);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.Equals(root, comparison) || path.StartsWith(prefix, comparison);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("An absolute path is required.");
        return Path.GetFullPath(path);
    }

    private static void CheckLinks(string path)
    {
        var current = Path.GetPathRoot(path)!;
        var rest = path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in rest)
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Symbolic links and reparse points are not supported.");
        }
    }
}
