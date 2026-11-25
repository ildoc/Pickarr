using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Pickarr.Core.Domain;
using Pickarr.Core.Recommendations;
using Pickarr.Infrastructure.Metadata.Reco;

namespace Pickarr.RecoBench;

public static class ModelComparisonRunner
{
    private const int WarmupDocuments = 16;
    private const int MeasurementSampleSize = 512;
    private const int EvaluationWarmups = 1;
    private const int SamplingIntervalMilliseconds = 50;

    public static int Run(
        string input,
        string output,
        int seed,
        DateTime observedAt,
        int repetitions,
        string currentModelDirectory,
        string multilingualModelDirectory,
        params string[] protectedDirectories)
    {
        if (observedAt.Kind != DateTimeKind.Utc || observedAt == DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc))
            throw new ArgumentException("A finite UTC observation time is required.");
        if (repetitions < 2)
            throw new ArgumentOutOfRangeException(nameof(repetitions), "At least two measured repetitions are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(currentModelDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(multilingualModelDirectory);

        var protectedRoots = protectedDirectories
            .Concat([currentModelDirectory, multilingualModelDirectory])
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
        using var connection = Snapshot.OpenBenchmark(input, output, protectedRoots);
        using var inputFile = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
        var inputFingerprint = Hash(inputFile);

        var dataRead = Stopwatch.StartNew();
        var history = TemporalInput.Read(connection, observedAt);
        var catalog = BenchmarkCatalogInput.Read(connection, RecoModelCatalog.DefaultModelId,
            RecoModelCatalog.EmbeddingDimensions);
        var poolTextInputs = BenchmarkTextInput.Read(connection, catalog);
        var titleById = catalog.Titles.ToDictionary(title => title.Features.TitleId);
        var storedEmbeddingIds = poolTextInputs
            .Where(text => titleById[text.TitleId].Embedding is not null)
            .Select(text => text.TitleId)
            .ToHashSet();
        var textHashes = BenchmarkTextInput.ReadCurrentEmbeddingTextHashes(
            connection, RecoModelCatalog.DefaultModelId);
        var currentVectorOverlap = poolTextInputs.Where(text =>
                storedEmbeddingIds.Contains(text.TitleId)
                && textHashes.TryGetValue(text.TitleId, out var storedHash)
                && string.Equals(storedHash, RecoEmbeddingText.Sha256Hex(text.Text),
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var qualityInputs = poolTextInputs;
        var measurementSample = SelectEvenlySpaced(qualityInputs, MeasurementSampleSize);
        var profiles = ReadProfileCounts(connection, history);
        dataRead.Stop();

        var textFingerprint = FingerprintTexts(qualityInputs);
        var total = Stopwatch.StartNew();
        var current = RunModel(catalog, history.Events, qualityInputs, measurementSample, repetitions,
            RecoBenchmarkModelCatalog.Current,
            () => LoadCurrent(currentModelDirectory));
        var multilingual = RunModel(catalog, history.Events, qualityInputs, measurementSample, repetitions,
            RecoBenchmarkModelCatalog.Multilingual,
            () => LoadMultilingual(multilingualModelDirectory));
        total.Stop();

        inputFile.Position = 0;
        if (Hash(inputFile) != inputFingerprint)
            throw new InvalidOperationException("Input changed during benchmark.");

        var qualityMetricsAvailable = HasComparableEmbeddingQuality(current.Evaluation, multilingual.Evaluation);
        var insufficient = !qualityMetricsAvailable
            || current.Evaluation.Test.Coverage.PositiveTitles < RecoEvalMetrics.LowSampleWarningThreshold
            || current.Evaluation.Test.Metrics["combined"].RecallAt10.Profiles == 0
            || multilingual.Evaluation.Test.Coverage.PositiveTitles < RecoEvalMetrics.LowSampleWarningThreshold
            || multilingual.Evaluation.Test.Metrics["combined"].RecallAt10.Profiles == 0;
        var assembly = typeof(ModelComparisonRunner).Assembly;
        using var executable = File.OpenRead(assembly.Location);
        var report = new
        {
            schemaVersion = 2,
            kind = "temporal_model_comparison",
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
                variants = "fresh_current_and_multilingual_embeddings_on_identical_full_current_catalog_pool_text; no_selection_or_promotion",
                measurementSampleTitles = measurementSample.Count,
                qualityComparisonTitles = qualityInputs.Count,
                qualityComparisonSelection = "all_current_catalog_pool_titles_sorted_by_title_id",
                warmupDocuments = Math.Min(WarmupDocuments, measurementSample.Count),
                evaluationWarmups = EvaluationWarmups,
                repetitions,
                rankingEvaluation = "full_temporal_proxy_validation_and_test",
                embeddingOperation = "one_title_text_per_onnx_inference; deterministic_evenly_spaced_sample_for_repeated_throughput",
                latencyPercentiles = "nearest_rank_p50_p95"
            },
            qualityComparison = new
            {
                status = qualityMetricsAvailable ? "available" : "embedding_metric_unavailable",
                requirement = "embedding recall metric available for every profile in both folds and models"
            },
            dataset = new
            {
                sha256 = inputFingerprint,
                textCorpusSha256 = textFingerprint,
                profiles = profiles.Count,
                titles = catalog.Titles.Count,
                poolTitles = poolTextInputs.Count,
                storedCurrentModelPoolEmbeddings = storedEmbeddingIds.Count,
                hashMatchingCurrentModelPoolEmbeddings = currentVectorOverlap.Length,
                qualityComparisonTitles = qualityInputs.Count,
                poolTitlesWithoutMatchingCurrentModelVector = poolTextInputs.Count - currentVectorOverlap.Length,
                events = history.Events.Count,
                currentRatings = history.CurrentRatings.Count,
                comparisonTitleSample = measurementSample.Count
            },
            profileCounts = profiles,
            dataReadMilliseconds = dataRead.Elapsed.TotalMilliseconds,
            totalModelComparisonMilliseconds = total.Elapsed.TotalMilliseconds,
            totalEndToEndMilliseconds = dataRead.Elapsed.TotalMilliseconds + total.Elapsed.TotalMilliseconds,
            models = new[] { current, multilingual },
            environment = new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processors = Environment.ProcessorCount,
                processorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                gcMemoryLimitBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
            },
            limitations = new[]
            {
                "current_catalog_membership_metadata_and_embeddings_are_approximate",
                "both_models_are_freshly_embedded_from_the_same_current_pool_text_using_pinned_artifacts",
                "current_catalog_text_and_metadata_are_not_historical",
                "rating_event_history_may_be_incomplete_absence_is_not_a_negative",
                "implicit_watched_library_tmdb_graph_production_settings_and_exploration_not_reconstructed",
                "default_weights_and_neutral_missing_embedding_combined_proxy_not_production",
                "sparse_similarity_mmr_proxy_not_production",
                "single_host_measurements_are_local_only_and_not_comparable_to_cluster",
                "repeated_embedding_latency_uses_a_deterministic_512_title_sample_or_the_full_smaller_pool",
                "working_set_is_sampled_locally; no confidence interval or statistical sufficiency claim",
                "no model variant selected or promoted"
            }
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        inputFile.Position = 0;
        if (Hash(inputFile) != inputFingerprint)
            throw new InvalidOperationException("Input changed during benchmark.");
        using var destination = Snapshot.CreateBenchmarkOutput(input, output, protectedRoots);
        destination.Write(bytes);
        destination.Flush(flushToDisk: true);
        return insufficient ? 3 : 0;
    }

    public static bool HasComparableEmbeddingQuality(
        TemporalBenchmarkReport current,
        TemporalBenchmarkReport multilingual)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(multilingual);
        return HasCompleteEmbeddingMetric(current.Validation)
            && HasCompleteEmbeddingMetric(current.Test)
            && HasCompleteEmbeddingMetric(multilingual.Validation)
            && HasCompleteEmbeddingMetric(multilingual.Test);
    }

    private static bool HasCompleteEmbeddingMetric(TemporalFoldReport fold) =>
        fold.Coverage.Profiles > 0
        && fold.Metrics.TryGetValue("embedding", out var metric)
        && metric.AvailableProfiles == fold.Coverage.Profiles
        && metric.RecallAt10.Profiles == metric.AvailableProfiles;

    public static BenchmarkCatalog BuildQualityCatalog(
        BenchmarkCatalog catalog,
        IReadOnlyDictionary<int, float[]> qualityVectors,
        string modelId,
        int dimensions)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(qualityVectors);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (dimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions));

        var poolIds = RecoMembership.PoolTitleIds(catalog.Availabilities, catalog.Sources).ToHashSet();
        var titlesById = catalog.Titles.ToDictionary(title => title.Features.TitleId);
        if (poolIds.Any(id => !titlesById.ContainsKey(id))
            || qualityVectors.Count != poolIds.Count
            || poolIds.Any(id => !qualityVectors.ContainsKey(id)))
            throw new InvalidOperationException("Fresh quality embeddings must cover every catalog-pool title exactly once.");

        foreach (var vector in qualityVectors.Values)
        {
            if (vector.Length != dimensions || vector.Any(value => !float.IsFinite(value)))
                throw new InvalidDataException("Quality embedding has an invalid dimension or non-finite value.");
            var norm = Math.Sqrt(vector.Sum(value => (double)value * value));
            if (!double.IsFinite(norm) || Math.Abs(norm - 1) > 0.001)
                throw new InvalidDataException("Quality embedding is not L2-normalized.");
        }

        return catalog with
        {
            EmbeddingModelId = modelId,
            EmbeddingDimensions = dimensions,
            Titles = catalog.Titles.Select(title =>
            {
                var id = title.Features.TitleId;
                var vector = poolIds.Contains(id) ? qualityVectors[id] : null;
                return title with
                {
                    EmbeddingModelId = vector is null ? null : modelId,
                    Embedding = vector
                };
            }).ToArray()
        };
    }

    private static ModelRunReport RunModel(
        BenchmarkCatalog catalog,
        IReadOnlyList<TemporalRatingEvent> events,
        IReadOnlyList<BenchmarkTitleText> qualityInputs,
        IReadOnlyList<BenchmarkTitleText> measurementSample,
        int repetitions,
        RecoBenchmarkModelDescriptor descriptor,
        Func<LoadedEmbedder> load)
    {
        using var resources = new ProcessResourceSampler(SamplingIntervalMilliseconds);
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var workingSetBefore = process.WorkingSet64;
        var cpuBefore = process.TotalProcessorTime;
        var total = Stopwatch.StartNew();

        var loadTimer = Stopwatch.StartNew();
        using var embedder = load();
        loadTimer.Stop();

        var sampleEmbedding = EmbeddingPerformance.Measure(
            measurementSample,
            embedder.Embed,
            descriptor.EmbeddingDimensions,
            Math.Min(WarmupDocuments, measurementSample.Count),
            repetitions);
        var fullEmbedding = EmbeddingPerformance.Measure(
            qualityInputs,
            embedder.Embed,
            descriptor.EmbeddingDimensions,
            warmupDocuments: 0,
            repetitions: 1,
            progress: (completed, totalDocuments) =>
                Console.WriteLine($"{descriptor.ModelId}: quality vectors {completed}/{totalDocuments}."));
        var modelCatalog = BuildQualityCatalog(
            catalog, fullEmbedding.Vectors, descriptor.ModelId, descriptor.EmbeddingDimensions);
        var qualityVectorBuild = new
        {
            source = "fresh_current_catalog_text",
            documents = qualityInputs.Count,
            repetitions = 1,
            warmupBeforePass = "model_session_warmed_by_repeated_measurement_sample",
            elapsedMilliseconds = fullEmbedding.Summary.TotalEmbeddingMilliseconds,
            documentsPerSecond = fullEmbedding.Summary.DocumentsPerSecond
        };

        Console.WriteLine($"{descriptor.ModelId}: ranking warmup.");
        _ = TemporalBenchmark.Evaluate(modelCatalog, events);
        var evaluationTimings = new List<double>(repetitions);
        TemporalBenchmarkReport? evaluation = null;
        var evaluationTimer = Stopwatch.StartNew();
        for (var index = 0; index < repetitions; index++)
        {
            var elapsed = Stopwatch.StartNew();
            evaluation = TemporalBenchmark.Evaluate(modelCatalog, events);
            elapsed.Stop();
            evaluationTimings.Add(elapsed.Elapsed.TotalMilliseconds);
            Console.WriteLine($"{descriptor.ModelId}: ranking repetition {index + 1}/{repetitions}.");
        }
        evaluationTimer.Stop();
        total.Stop();

        process.Refresh();
        var cpuElapsed = process.TotalProcessorTime - cpuBefore;
        return new ModelRunReport(
            descriptor.ModelId,
            descriptor.Revision,
            embedder.OnnxSha256,
            embedder.TokenizerSha256,
            descriptor.EmbeddingDimensions,
            descriptor.MaxTokens,
            descriptor.Pooling,
            loadTimer.Elapsed.TotalMilliseconds,
            sampleEmbedding.Summary,
            qualityVectorBuild,
            new
            {
                warmups = EvaluationWarmups,
                repetitions,
                totalMilliseconds = evaluationTimer.Elapsed.TotalMilliseconds,
                p50Milliseconds = EmbeddingPerformance.Percentile(evaluationTimings, 0.50),
                p95Milliseconds = EmbeddingPerformance.Percentile(evaluationTimings, 0.95),
                repetitionMilliseconds = evaluationTimings
            },
            new
            {
                processCpuMilliseconds = cpuElapsed.TotalMilliseconds,
                processCpuPercentOfLogicalCapacity = total.Elapsed.TotalMilliseconds == 0
                    ? 0
                    : 100 * cpuElapsed.TotalMilliseconds
                        / (total.Elapsed.TotalMilliseconds * Environment.ProcessorCount),
                workingSetBeforeBytes = workingSetBefore,
                peakWorkingSetBytes = resources.PeakWorkingSetBytes,
                peakWorkingSetIncreaseBytes = Math.Max(0, resources.PeakWorkingSetBytes - workingSetBefore),
                workingSetSampleIntervalMilliseconds = SamplingIntervalMilliseconds
            },
            total.Elapsed.TotalMilliseconds,
            evaluation!);
    }

    private static LoadedEmbedder LoadCurrent(string modelDirectory)
    {
        var descriptor = RecoBenchmarkModelCatalog.Current;
        var onnx = VerifyArtifact(Path.Combine(modelDirectory, descriptor.OnnxPath), descriptor.OnnxSha256, "model.onnx");
        var tokenizer = VerifyArtifact(Path.Combine(modelDirectory, descriptor.TokenizerPath),
            descriptor.TokenizerSha256, "vocab.txt");
        var embedder = new RecoOnnxEmbedder();
        if (!embedder.TryLoad(modelDirectory, RecoModelCatalog.DefaultModelId, threads: 1, NullLogger.Instance))
        {
            embedder.Dispose();
            throw new InvalidDataException("The pinned current embedding model could not be loaded.");
        }
        return new LoadedEmbedder(embedder.Embed, embedder, onnx, tokenizer);
    }

    private static LoadedEmbedder LoadMultilingual(string modelDirectory)
    {
        var embedder = new MultilingualMiniLmEmbedder(modelDirectory, threads: 1);
        return new LoadedEmbedder(embedder.Embed, embedder, embedder.OnnxSha256, embedder.TokenizerSha256);
    }

    private static IReadOnlyList<object> ReadProfileCounts(SqliteConnection connection, TemporalInputData history)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Profiles ORDER BY Id";
        var profileIds = new List<int>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) profileIds.Add(reader.GetInt32(0));

        var currentByProfile = history.CurrentRatings.GroupBy(x => x.ProfileId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var eventsByProfile = history.Events.GroupBy(x => x.ProfileId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        return profileIds.Select((profileId, index) =>
        {
            var current = currentByProfile.GetValueOrDefault(profileId) ?? [];
            var events = eventsByProfile.GetValueOrDefault(profileId) ?? [];
            var splitResult = TemporalHistory.Split(profileId, events);
            var split = splitResult.Split;
            return (object)new
            {
                profileOrdinal = index + 1,
                currentRatings = new
                {
                    total = current.Length,
                    love = current.Count(x => RatingValues.Normalize(x.Value) == "Love"),
                    like = current.Count(x => RatingValues.Normalize(x.Value) == "Like"),
                    dislike = current.Count(x => RatingValues.Normalize(x.Value) == "Dislike"),
                    notInterested = current.Count(x => RatingValues.Normalize(x.Value) == "NotInterested")
                },
                ratingEvents = new
                {
                    total = events.Length,
                    love = events.Count(x => x.Value is not null && RatingValues.Normalize(x.Value) == "Love"),
                    like = events.Count(x => x.Value is not null && RatingValues.Normalize(x.Value) == "Like"),
                    dislike = events.Count(x => x.Value is not null && RatingValues.Normalize(x.Value) == "Dislike"),
                    notInterested = events.Count(x => x.Value is not null && RatingValues.Normalize(x.Value) == "NotInterested"),
                    deletes = events.Count(x => x.Value is null)
                },
                splitUnavailableReason = splitResult.UnavailableReason,
                trainEvents = split?.Train.Count,
                validationEvents = split?.Validation.Count,
                testEvents = split?.Test.Count
            };
        }).ToArray();
    }

    private static IReadOnlyList<BenchmarkTitleText> SelectEvenlySpaced(
        IReadOnlyList<BenchmarkTitleText> inputs,
        int maximumCount)
    {
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (inputs.Count == 0)
            throw new ArgumentException("At least one comparison title is required.", nameof(inputs));
        if (inputs.Count <= maximumCount)
            return inputs.ToArray();
        if (maximumCount == 1)
            return [inputs[0]];

        var sample = new BenchmarkTitleText[maximumCount];
        for (var index = 0; index < maximumCount; index++)
        {
            var sourceIndex = (int)((long)index * (inputs.Count - 1) / (maximumCount - 1));
            sample[index] = inputs[sourceIndex];
        }
        return sample;
    }

    private static string FingerprintTexts(IReadOnlyList<BenchmarkTitleText> texts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var item in texts)
        {
            var bytes = Encoding.UTF8.GetBytes(
                $"{item.TitleId.ToString(CultureInfo.InvariantCulture)}\0{item.Text}\0");
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string VerifyArtifact(string path, string expected, string name)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required model artifact is missing: {name}.");
        using var stream = File.OpenRead(path);
        var actual = Hash(stream);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Model artifact SHA-256 mismatch: {name}.");
        return actual;
    }

    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));

    private sealed class LoadedEmbedder(
        Func<string, float[]> embed,
        IDisposable lifetime,
        string onnxSha256,
        string tokenizerSha256) : IDisposable
    {
        public Func<string, float[]> Embed { get; } = embed;
        public string OnnxSha256 { get; } = onnxSha256;
        public string TokenizerSha256 { get; } = tokenizerSha256;
        public void Dispose() => lifetime.Dispose();
    }

    private sealed record ModelRunReport(
        string ModelId,
        string Revision,
        string OnnxSha256,
        string TokenizerSha256,
        int Dimensions,
        int MaxTokens,
        string Pooling,
        double ModelLoadMilliseconds,
        EmbeddingPerformanceSummary Embedding,
        object QualityVectorBuild,
        object RankingEvaluationPerformance,
        object Resources,
        double TotalModelMilliseconds,
        TemporalBenchmarkReport Evaluation);

    private sealed class ProcessResourceSampler : IDisposable
    {
        private readonly Process _process = Process.GetCurrentProcess();
        private readonly Timer _timer;
        private long _peakWorkingSetBytes;

        public ProcessResourceSampler(int intervalMilliseconds)
        {
            Sample();
            _timer = new Timer(_ => Sample(), null, 0, intervalMilliseconds);
        }

        public long PeakWorkingSetBytes => Interlocked.Read(ref _peakWorkingSetBytes);

        private void Sample()
        {
            _process.Refresh();
            var workingSet = _process.WorkingSet64;
            var observed = Interlocked.Read(ref _peakWorkingSetBytes);
            while (workingSet > observed)
            {
                var prior = Interlocked.CompareExchange(ref _peakWorkingSetBytes, workingSet, observed);
                if (prior == observed)
                    break;
                observed = prior;
            }
        }

        public void Dispose()
        {
            using var callbacksFinished = new ManualResetEvent(false);
            if (_timer.Dispose(callbacksFinished))
                callbacksFinished.WaitOne();
            Sample();
            _process.Dispose();
        }
    }
}
