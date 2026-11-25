namespace Pickarr.Core.Recommendations;

/// <summary>Level-2 embedding kNN (§12.4).</summary>
public static class RecoKnn
{
    public const int DefaultK = 10;
    public const double DefaultGamma = 3.0;
    public const double DefaultSimMin = 0.25;

    /// <summary>
    /// Cap on signalled neighbours used in brute-force kNN (NFR-REC-02 / SP-R3).
    /// Strongest |s_i| kept; quality stays close to full set for typical rating profiles.
    /// </summary>
    public const int DefaultMaxSignals = 24;

    /// <summary>
    /// For each candidate title, score as weighted average of signalled neighbours' s_i
    /// by sim^γ. Result in [-1,1]; with fewer than K neighbours above simMin, damp toward 0.5
    /// after mapping to [0,1] is done by the caller via pool normalize — here we return the
    /// raw knn in [-1,1] and apply damp on the [0,1] mapped value.
    /// </summary>
    public static IReadOnlyDictionary<int, double> ScoreAll(
        IReadOnlyDictionary<int, float[]> embeddings,
        IReadOnlyDictionary<int, double> signals,
        int k = DefaultK,
        double gamma = DefaultGamma,
        double simMin = DefaultSimMin,
        int maxSignals = DefaultMaxSignals) =>
        ScoreCandidates(embeddings, embeddings, signals, k, gamma, simMin, maxSignals);

    /// <summary>Score only the candidates, using a separate vector lookup for signalled neighbours.</summary>
    public static IReadOnlyDictionary<int, double> ScoreCandidates(
        IReadOnlyDictionary<int, float[]> candidates,
        IReadOnlyDictionary<int, float[]> neighbourEmbeddings,
        IReadOnlyDictionary<int, double> signals,
        int k = DefaultK,
        double gamma = DefaultGamma,
        double simMin = DefaultSimMin,
        int maxSignals = DefaultMaxSignals)
    {
        var result = new Dictionary<int, double>(candidates.Count);
        if (candidates.Count == 0 || signals.Count == 0)
        {
            foreach (var id in candidates.Keys)
            {
                result[id] = 0; // neutral in [-1,1] → 0.5 after ToUnitInterval
            }

            return result;
        }

        var signalledQuery = signals
            .Where(kv => kv.Value != 0 && neighbourEmbeddings.ContainsKey(kv.Key))
            .Select(kv => (TitleId: kv.Key, Signal: kv.Value, Vector: neighbourEmbeddings[kv.Key]));
        var signalled = maxSignals > 0 && signals.Count > maxSignals
            ? signalledQuery
                .OrderByDescending(s => Math.Abs(s.Signal))
                .ThenBy(s => s.TitleId)
                .Take(maxSignals)
                .ToList()
            : signalledQuery.ToList();

        foreach (var (titleId, vector) in candidates)
        {
            var neighbours = new List<(double Sim, double Signal)>(signalled.Count);
            foreach (var s in signalled)
            {
                if (s.TitleId == titleId)
                {
                    continue;
                }

                var sim = Dot(vector, s.Vector);
                if (sim < simMin)
                {
                    continue;
                }

                neighbours.Add((sim, s.Signal));
            }

            neighbours.Sort((a, b) => b.Sim.CompareTo(a.Sim));
            if (neighbours.Count > k)
            {
                neighbours.RemoveRange(k, neighbours.Count - k);
            }

            if (neighbours.Count == 0)
            {
                result[titleId] = 0; // → 0.5 after [-1,1]→[0,1]
                continue;
            }

            var num = 0.0;
            var den = 0.0;
            foreach (var (sim, signal) in neighbours)
            {
                var w = Math.Pow(sim, gamma);
                num += w * signal;
                den += w;
            }

            var knn = den <= double.Epsilon ? 0.0 : num / den;

            // Dampen toward 0 when fewer than K strong neighbours.
            if (neighbours.Count < k)
            {
                var fill = (double)neighbours.Count / k;
                knn *= fill;
            }

            result[titleId] = Math.Clamp(knn, -1, 1);
        }

        return result;
    }

    /// <summary>Map knn from [-1,1] to [0,1].</summary>
    public static double ToUnitInterval(double knn) => (knn + 1.0) / 2.0;

    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0;
        }

        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return (float)sum;
    }

    public static void L2Normalize(Span<float> vector)
    {
        double sumSq = 0;
        for (var i = 0; i < vector.Length; i++)
        {
            sumSq += vector[i] * vector[i];
        }

        if (sumSq < 1e-12)
        {
            return;
        }

        var inv = (float)(1.0 / Math.Sqrt(sumSq));
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] *= inv;
        }
    }

    public static IReadOnlyList<(int TitleId, double Similarity)> TopSimilar(
        float[] query,
        IReadOnlyDictionary<int, float[]> candidates,
        int limit,
        double minSimilarity = 0,
        IReadOnlySet<int>? excludeTitleIds = null)
    {
        if (query.Length == 0 || limit <= 0)
        {
            return [];
        }

        var scored = new List<(int TitleId, double Similarity)>();
        foreach (var (titleId, vector) in candidates)
        {
            if (excludeTitleIds is not null && excludeTitleIds.Contains(titleId))
            {
                continue;
            }

            var sim = Dot(query, vector);
            if (sim < minSimilarity)
            {
                continue;
            }

            scored.Add((titleId, sim));
        }

        return scored
            .OrderByDescending(s => s.Similarity)
            .ThenBy(s => s.TitleId)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Merge exact embedding matches with sparse fallback matches for candidates missing embeddings.
    /// Embedding cosine is normalized from [-1,1] to [0,1]; sparse cosine is already [0,1].
    /// Sparse fallback does not replace or duplicate an embedding match.
    /// </summary>
    public static IReadOnlyList<(int TitleId, double Similarity)> MergeSimilarityFallback(
        IReadOnlyList<(int TitleId, double Similarity)> embeddingMatches,
        IReadOnlyList<(int TitleId, double Similarity)> sparseMatches,
        IReadOnlySet<int> embeddingCandidateIds,
        IReadOnlySet<int> excludeTitleIds,
        int limit)
    {
        var scores = new Dictionary<int, double>();
        foreach (var (titleId, similarity) in embeddingMatches)
        {
            if (!excludeTitleIds.Contains(titleId))
            {
                scores[titleId] = Math.Clamp((similarity + 1.0) / 2.0, 0, 1);
            }
        }

        foreach (var (titleId, similarity) in sparseMatches)
        {
            if (!excludeTitleIds.Contains(titleId) && !embeddingCandidateIds.Contains(titleId))
            {
                scores[titleId] = Math.Clamp(similarity, 0, 1);
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(Math.Max(0, limit))
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }
}
