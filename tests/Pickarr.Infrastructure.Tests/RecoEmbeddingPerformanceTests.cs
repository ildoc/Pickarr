using Pickarr.RecoBench;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class RecoEmbeddingPerformanceTests
{
    [Fact]
    public void Measurement_includes_warmup_and_repeated_full_passes_without_exposing_vectors()
    {
        var calls = 0;
        var inputs = new[]
        {
            new BenchmarkTitleText(1, "synthetic one"),
            new BenchmarkTitleText(2, "synthetic two"),
            new BenchmarkTitleText(3, "synthetic three")
        };

        var result = EmbeddingPerformance.Measure(
            inputs,
            _ =>
            {
                calls++;
                return [1f, 0f];
            },
            dimensions: 2,
            warmupDocuments: 2,
            repetitions: 2);

        calls.ShouldBe(8);
        result.Summary.WarmupDocuments.ShouldBe(2);
        result.Summary.Repetitions.ShouldBe(2);
        result.Summary.DocumentsPerPass.ShouldBe(3);
        result.Summary.PassMilliseconds.Count.ShouldBe(2);
        result.Summary.DocumentsPerSecond.ShouldBeGreaterThan(0);
        result.Summary.DocumentLatencyP50Milliseconds.ShouldBeGreaterThanOrEqualTo(0);
        result.Summary.DocumentLatencyP95Milliseconds.ShouldBeGreaterThanOrEqualTo(
            result.Summary.DocumentLatencyP50Milliseconds);
    }

    [Fact]
    public void Measurement_rejects_non_normalized_embeddings()
    {
        Should.Throw<InvalidDataException>(() => EmbeddingPerformance.Measure(
            [new BenchmarkTitleText(1, "synthetic")],
            _ => [2f, 0f],
            dimensions: 2,
            warmupDocuments: 1,
            repetitions: 2));
    }
}
