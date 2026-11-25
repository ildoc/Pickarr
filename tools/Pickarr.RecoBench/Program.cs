using Pickarr.RecoBench;
using System.Globalization;

if (!((args.Length == 2 && args[0] == "counts") ||
      (args.Length >= 4 && (args[0] == "snapshot" || args[0] == "verify")) ||
      (args.Length >= 5 && args[0] == "prepare" &&
       int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) ||
      (args.Length == 3 && args[0] == "model-self-test") ||
      (args.Length >= 9 && args[0] == "model-compare" &&
       int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
       DateTimeOffset.TryParseExact(args[4], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var comparisonTime) &&
       comparisonTime.Offset == TimeSpan.Zero &&
       int.TryParse(args[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var repetitions) &&
       repetitions >= 2) ||
      (args.Length >= 6 && args[0] == "benchmark" &&
       int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
       DateTimeOffset.TryParseExact(args[4], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) &&
       time.Offset == TimeSpan.Zero)))
{
    Console.Error.WriteLine("Usage: counts <absolute-source> | snapshot|verify <absolute-input> <absolute-new-output> <absolute-repository-directory> [absolute-protected-directory ...] | prepare <absolute-input> <absolute-new-report> <integer-seed> <absolute-repository-directory> [absolute-protected-directory ...] | benchmark <absolute-input> <absolute-new-report> <integer-seed> <UTC-observed-at-O> <absolute-repository-directory> [absolute-protected-directory ...] | model-self-test <current-model-directory> <multilingual-model-directory> | model-compare <absolute-input> <absolute-new-report> <integer-seed> <UTC-observed-at-O> <repetitions>=2 <current-model-directory> <multilingual-model-directory> <absolute-repository-directory> [absolute-protected-directory ...]");
    return 2;
}

try
{
    if (args[0] == "model-compare")
    {
        var observedAt = DateTimeOffset.ParseExact(args[4], "O", CultureInfo.InvariantCulture).UtcDateTime;
        var protectedDirectories = new[] { args[8] }.Concat(args[9..]).ToArray();
        var exit = ModelComparisonRunner.Run(
            args[1], args[2], int.Parse(args[3], CultureInfo.InvariantCulture), observedAt,
            int.Parse(args[5], CultureInfo.InvariantCulture), args[6], args[7], protectedDirectories);
        Console.WriteLine(exit == 3
            ? "Model comparison report written: insufficient sample."
            : "Model comparison report written: descriptive proxy, not certified sufficient.");
        return exit;
    }
    if (args[0] == "benchmark")
    {
        var observedAt = DateTimeOffset.ParseExact(args[4], "O", CultureInfo.InvariantCulture).UtcDateTime;
        var exit = BenchmarkRunner.Run(args[1], args[2], int.Parse(args[3], CultureInfo.InvariantCulture), observedAt, args[5..]);
        Console.WriteLine(exit == 3 ? "Benchmark report written: insufficient sample." : "Benchmark report written: proxy, not certified sufficient.");
        return exit;
    }
    if (args[0] == "model-self-test")
    {
        Console.WriteLine(RecoModelSelfTestRunner.Run(args[1], args[2]));
        return 0;
    }
    if (args[0] == "prepare")
    {
        var exit = PreparationRunner.Run(args[1], args[2], int.Parse(args[3], CultureInfo.InvariantCulture), args[4..]);
        Console.WriteLine(exit == 3
            ? "Preparation report written: insufficient sample; quality metrics unavailable."
            : "Preparation report written: diagnostics only; quality metrics unavailable.");
        return exit;
    }
    SnapshotCounts counts;
    if (args[0] == "counts")
        counts = Snapshot.ReadSourceCounts(args[1]);
    else if (args[0] == "snapshot")
        counts = Snapshot.Create(args[1], args[2], args[3..]);
    else
    {
        using var snapshot = Snapshot.OpenBenchmark(args[1], args[2], args[3..]);
        counts = Snapshot.Verify(snapshot);
    }
    var label = args[0] == "counts" ? "Source counts" : "Snapshot verified";
    Console.WriteLine($"{label}: titles={counts.Titles}, profiles={counts.Profiles}, ratings={counts.Ratings}.");
    return 0;
}
catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or
                                  Microsoft.Data.Sqlite.SqliteException or UnauthorizedAccessException or
                                  System.Text.Json.JsonException or FormatException or OverflowException or
                                  Microsoft.ML.OnnxRuntime.OnnxRuntimeException or DllNotFoundException or
                                  Tokenizers.HuggingFace.Errors.TokenizerLoadFileException or
                                  Tokenizers.HuggingFace.Errors.TokenizerEncodingException)
{
    var reason = exception switch
    {
        FileNotFoundException when args[0] is "model-self-test" or "model-compare" => "required model artifact is missing",
        InvalidDataException when args[0] is "model-self-test" or "model-compare" => "model artifact fingerprint, signature or output mismatch",
        Microsoft.ML.OnnxRuntime.OnnxRuntimeException when args[0] is "model-self-test" or "model-compare" => "ONNX inference failed",
        DllNotFoundException when args[0] is "model-self-test" or "model-compare" => "native model runtime is unavailable",
        Tokenizers.HuggingFace.Errors.TokenizerLoadFileException or
            Tokenizers.HuggingFace.Errors.TokenizerEncodingException
            when args[0] is "model-self-test" or "model-compare" => "multilingual tokenizer validation or encoding failed",
        Microsoft.Data.Sqlite.SqliteException => "SQLite read or backup failed",
        System.Text.Json.JsonException or FormatException or OverflowException => "invalid snapshot data",
        UnauthorizedAccessException => "access denied",
        ArgumentException => "invalid or protected path",
        FileNotFoundException => "source does not exist",
        DirectoryNotFoundException => "required directory does not exist",
        IOException when args[0] == "model-self-test" => "model artifact could not be read",
        IOException => "output exists or a file operation failed",
        InvalidOperationException => "integrity, foreign key or standalone snapshot check failed",
        _ => "unknown error"
    };
    var operation = args[0] switch
    {
        "prepare" => "Preparation",
        "benchmark" => "Benchmark",
        "model-compare" => "Model comparison",
        "model-self-test" => "Model self-test",
        _ => "Snapshot"
    };
    Console.Error.WriteLine($"{operation} failed: {reason}.");
    return 1;
}
