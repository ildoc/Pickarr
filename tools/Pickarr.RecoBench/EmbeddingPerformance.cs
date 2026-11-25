using System.Diagnostics;

namespace Pickarr.RecoBench;

public sealed record EmbeddingPerformanceSummary(
    int WarmupDocuments,
    int Repetitions,
    int DocumentsPerPass,
    double TotalEmbeddingMilliseconds,
    double DocumentsPerSecond,
    double DocumentLatencyP50Milliseconds,
    double DocumentLatencyP95Milliseconds,
    IReadOnlyList<double> PassMilliseconds);

public sealed class ModelEmbeddingResult
{
    internal ModelEmbeddingResult(
        EmbeddingPerformanceSummary summary,
        IReadOnlyDictionary<int, float[]> vectors)
    {
        Summary = summary;
        Vectors = vectors;
    }

    public EmbeddingPerformanceSummary Summary { get; }
    internal IReadOnlyDictionary<int, float[]> Vectors { get; }
}

public static class EmbeddingPerformance
{
    public static ModelEmbeddingResult Measure(
        IReadOnlyList<BenchmarkTitleText> inputs,
        Func<string, float[]> embed,
        int dimensions,
        int warmupDocuments,
        int repetitions,
        Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(embed);
        if (inputs.Count == 0 || dimensions <= 0 || warmupDocuments < 0
            || warmupDocuments > inputs.Count || repetitions <= 0)
            throw new ArgumentOutOfRangeException(nameof(inputs), "Embedding measurement parameters are invalid.");
        if (inputs.Select(x => x.TitleId).Distinct().Count() != inputs.Count
            || inputs.Any(x => x.TitleId <= 0))
            throw new ArgumentException("Embedding inputs must have unique positive title IDs.", nameof(inputs));

        for (var index = 0; index < warmupDocuments; index++)
            ValidateVector(embed(inputs[index].Text), dimensions);

        var latencyMilliseconds = new List<double>(checked(inputs.Count * repetitions));
        var passMilliseconds = new List<double>(repetitions);
        IReadOnlyDictionary<int, float[]>? finalVectors = null;
        var total = Stopwatch.StartNew();
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            var vectors = new Dictionary<int, float[]>(inputs.Count);
            var pass = Stopwatch.StartNew();
            foreach (var input in inputs)
            {
                var elapsed = Stopwatch.StartNew();
                var vector = embed(input.Text);
                elapsed.Stop();
                ValidateVector(vector, dimensions);
                latencyMilliseconds.Add(elapsed.Elapsed.TotalMilliseconds);
                vectors.Add(input.TitleId, vector);
                var completed = vectors.Count;
                if (completed % 1000 == 0 || completed == inputs.Count)
                    progress?.Invoke(completed, inputs.Count);
            }
            pass.Stop();
            passMilliseconds.Add(pass.Elapsed.TotalMilliseconds);
            if (repetition == repetitions - 1)
                finalVectors = vectors;
        }
        total.Stop();

        var seconds = total.Elapsed.TotalSeconds;
        return new ModelEmbeddingResult(
            new EmbeddingPerformanceSummary(
                warmupDocuments,
                repetitions,
                inputs.Count,
                total.Elapsed.TotalMilliseconds,
                inputs.Count * (double)repetitions / seconds,
                Percentile(latencyMilliseconds, 0.50),
                Percentile(latencyMilliseconds, 0.95),
                passMilliseconds),
            finalVectors!);
    }

    public static double Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0 || percentile is < 0 or > 1 || values.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Percentile input is invalid.", nameof(values));
        var sorted = values.Order().ToArray();
        var index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private static void ValidateVector(float[] vector, int dimensions)
    {
        if (vector.Length != dimensions || vector.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Embedding output has an invalid dimension or non-finite value.");
        var norm = Math.Sqrt(vector.Sum(value => (double)value * value));
        if (!double.IsFinite(norm) || Math.Abs(norm - 1) > 0.001)
            throw new InvalidDataException("Embedding output is not L2-normalized.");
    }
}
