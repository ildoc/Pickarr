using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pickarr.RecoBench;

public static class PreparationRunner
{
    public static int Run(string input, string output, int seed, params string[] protectedDirectories)
    {
        using var connection = Snapshot.OpenBenchmark(input, output, protectedDirectories);
        using var inputFile = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fingerprint = Hash(inputFile);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT count(*) FROM Titles),
                (SELECT count(*) FROM Profiles),
                count(*),
                count(DISTINCT TitleId),
                count(DISTINCT ProfileId),
                count(CASE WHEN lower(Value) IN ('love','like') THEN 1 END),
                count(CASE WHEN lower(Value) IN ('dislike','notinterested','not_interested') THEN 1 END),
                count(CASE WHEN julianday(RatedAt) IS NOT NULL THEN 1 END)
            FROM Ratings
            """;
        SnapshotCounts counts;
        long ratedTitles, ratedProfiles, positive, negative, timestamped;
        using (var reader = command.ExecuteReader())
        {
            reader.Read();
            counts = new SnapshotCounts(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
            ratedTitles = reader.GetInt64(3);
            ratedProfiles = reader.GetInt64(4);
            positive = reader.GetInt64(5);
            negative = reader.GetInt64(6);
            timestamped = reader.GetInt64(7);
        }
        var insufficient = counts.Titles == 0 || counts.Profiles == 0 || positive + negative == 0;
        var assembly = typeof(PreparationRunner).Assembly;
        using var executable = File.OpenRead(assembly.Location);
        var report = new
        {
            schemaVersion = 1,
            kind = "preparation_diagnostics",
            status = insufficient ? "insufficient_sample" : "diagnostics_only",
            code = new
            {
                version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                assemblySha256 = Hash(executable),
                buildConfiguration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration
            },
            configuration = new
            {
                protocol = "preparation-v1",
                seed,
                seedUsage = "reserved_no_sampling_or_training",
                scope = "current_explicit_ratings_only",
                minimum = "at_least_one_title_profile_and_recognized_rating_for_diagnostics_only"
            },
            dataset = new { sha256 = fingerprint, counts.Titles, counts.Profiles, counts.Ratings },
            coverage = new
            {
                ratedTitles,
                ratedProfiles,
                titleFraction = Fraction(ratedTitles, counts.Titles),
                profileFraction = Fraction(ratedProfiles, counts.Profiles),
                positiveRatings = positive,
                negativeRatings = negative,
                unknownRatings = counts.Ratings - positive - negative,
                timestampedRatings = timestamped
            },
            qualityMetrics = (object?)null,
            limitations = new[]
            {
                "quality_evaluation_not_implemented_TR06",
                "no_temporal_split_ranking_training_or_model_comparison",
                "diagnostic_minimum_is_not_statistical_sufficiency",
                "no_historical_catalog_reconstruction_or_embedding_coverage"
            },
            environment = new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processors = Environment.ProcessorCount
            }
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        inputFile.Position = 0;
        if (Hash(inputFile) != fingerprint)
            throw new InvalidOperationException("Input changed during preparation.");
        // Recheck paths immediately before exclusive creation; OpenBenchmark does not reserve output.
        using var destination = Snapshot.CreateBenchmarkOutput(input, output, protectedDirectories);
        destination.Write(bytes);
        destination.Flush(flushToDisk: true);
        return insufficient ? 3 : 0;
    }

    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));

    private static double? Fraction(long numerator, long denominator) =>
        denominator == 0 ? null : (double)numerator / denominator;
}
