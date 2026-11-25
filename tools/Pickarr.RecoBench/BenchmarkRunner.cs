using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Pickarr.Core.Recommendations;

namespace Pickarr.RecoBench;

public static class BenchmarkRunner
{
    public static int Run(string input, string output, int seed, DateTime observedAt, params string[] protectedDirectories)
    {
        if (observedAt.Kind != DateTimeKind.Utc || observedAt == DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc))
            throw new ArgumentException("A finite UTC observation time is required.");
        using var connection = Snapshot.OpenBenchmark(input, output, protectedDirectories);
        using var inputFile = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fingerprint = Hash(inputFile);
        var history = TemporalInput.Read(connection, observedAt);
        var catalog = BenchmarkCatalogInput.Read(connection, RecoModelCatalog.DefaultModelId, RecoModelCatalog.EmbeddingDimensions);
        var evaluation = TemporalBenchmark.Evaluate(catalog, history.Events);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT count(*) FROM Profiles), (SELECT count(*) FROM Titles)";
        long profiles, titles;
        using (var reader = command.ExecuteReader())
        {
            reader.Read();
            profiles = reader.GetInt64(0);
            titles = reader.GetInt64(1);
        }
        var insufficient = evaluation.Test.Coverage.PositiveTitles < RecoEvalMetrics.LowSampleWarningThreshold
            || evaluation.Test.Metrics["combined"].RecallAt10.Profiles == 0;
        var assembly = typeof(BenchmarkRunner).Assembly;
        using var executable = File.OpenRead(assembly.Location);
        var report = new
        {
            schemaVersion = 1,
            kind = "temporal_benchmark_proxy",
            status = insufficient ? "insufficient_sample" : "completed_not_certified_sufficient",
            code = new
            {
                version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                assemblySha256 = Hash(executable),
                buildConfiguration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration
            },
            configuration = new
            {
                protocol = TemporalHistory.Protocol,
                seed,
                seedUsage = "reserved_no_random_sampling",
                observedAt,
                modelId = RecoModelCatalog.DefaultModelId,
                embeddingDimensions = RecoModelCatalog.EmbeddingDimensions,
                variants = "all_reported_no_selection_or_promotion",
                minimumTestPositiveTitles = RecoEvalMetrics.LowSampleWarningThreshold,
                tiePolicy = "score_descending_title_id_ascending_auc_score_ties_half_mmr_uses_rank",
                labelPolicy = "last_unambiguous_event_per_eligible_title_in_fold_deletes_and_conflicts_excluded",
                aggregation = "macro_mean_per_profile_with_metric_denominators"
            },
            dataset = new
            {
                sha256 = fingerprint,
                profiles,
                titles,
                events = history.Events.Count,
                currentRatings = history.CurrentRatings.Count,
                currentModelEmbeddings = catalog.Titles.Count(x => x.Embedding is not null)
            },
            evaluation,
            limitations = new[]
            {
                "current_catalog_membership_metadata_and_embeddings_are_approximate",
                "rating_event_history_may_be_incomplete_absence_is_not_a_negative",
                "implicit_watched_library_and_tmdb_graph_not_reconstructed",
                "default_weights_and_neutral_missing_embedding_combined_proxy_not_production",
                "sparse_similarity_mmr_proxy_not_production",
                "model_id_does_not_verify_artifact_version_or_embedding_freshness",
                "level3_trainer_not_injected_and_no_variant_selected_or_promoted",
                "small_sample_is_not_statistical_sufficiency_or_online_improvement"
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
            throw new InvalidOperationException("Input changed during benchmark.");
        using var destination = Snapshot.CreateBenchmarkOutput(input, output, protectedDirectories);
        destination.Write(bytes);
        destination.Flush(flushToDisk: true);
        return insufficient ? 3 : 0;
    }

    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));
}
