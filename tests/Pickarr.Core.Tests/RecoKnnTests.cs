using Pickarr.Core.Recommendations;
using Shouldly;

namespace Pickarr.Core.Tests;

public sealed class RecoKnnTests
{
    [Fact]
    public void MergeSimilarityFallback_is_deterministic_and_does_not_duplicate()
    {
        var embeddingMatches = new[]
        {
            (TitleId: 10, Similarity: 0.8),
            (TitleId: 11, Similarity: 0.2),
        };
        var sparseMatches = new[]
        {
            (TitleId: 10, Similarity: 1.0),
            (TitleId: 12, Similarity: 0.7),
        };

        var merged = RecoKnn.MergeSimilarityFallback(
            embeddingMatches,
            sparseMatches,
            new HashSet<int> { 10, 11 },
            new HashSet<int> { 11 },
            10);

        merged.Count.ShouldBe(2);
        merged[0].TitleId.ShouldBe(10);
        merged[1].TitleId.ShouldBe(12);
        merged.Select(match => match.TitleId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void ScoreAll_weights_neighbours_by_similarity()
    {
        // Unit vectors in 2D: title 1 liked, title 2 close, title 3 orthogonal.
        var embeddings = new Dictionary<int, float[]>
        {
            [1] = [1f, 0f],
            [2] = [0.9f, 0.4358899f], // ~cos 0.9 with 1 after normalize-ish
            [3] = [0f, 1f],
        };
        RecoKnn.L2Normalize(embeddings[2]);
        var signals = new Dictionary<int, double> { [1] = 1.0 };

        var scores = RecoKnn.ScoreAll(embeddings, signals, k: 10, simMin: 0.1);
        RecoKnn.ToUnitInterval(scores[2]).ShouldBeGreaterThan(RecoKnn.ToUnitInterval(scores[3]));
    }

    [Fact]
    public void TopSimilar_orders_by_dot_product()
    {
        var query = new float[] { 1f, 0f };
        var candidates = new Dictionary<int, float[]>
        {
            [2] = [0.8f, 0.2f],
            [3] = [0.1f, 0.9f],
            [4] = [0.95f, 0.05f],
        };
        foreach (var v in candidates.Values)
        {
            RecoKnn.L2Normalize(v);
        }

        var top = RecoKnn.TopSimilar(query, candidates, limit: 2, minSimilarity: 0);
        top[0].TitleId.ShouldBe(4);
        top[1].TitleId.ShouldBe(2);
    }

    [Fact]
    public void TopSimilar_matches_brute_force_oracle_beyond_old_120_limit()
    {
        var query = new float[] { 1f, 0f };
        var candidates = Enumerable.Range(1, 121)
            .ToDictionary(
                id => id,
                id =>
                {
                    if (id == 121)
                    {
                        return new float[] { 1f, 0f };
                    }
                    var x = 1.0 - (id - 1) / 120.0;
                    var y = Math.Sqrt(Math.Max(0, 1.0 - x * x));
                    return new float[] { (float)x, (float)y };
                });
        foreach (var vector in candidates.Values)
        {
            RecoKnn.L2Normalize(vector);
        }

        var oracle = candidates
            .Select(kv => (TitleId: kv.Key, Similarity: RecoKnn.Dot(query, kv.Value)))
            .OrderByDescending(match => match.Similarity)
            .ThenBy(match => match.TitleId)
            .Take(5)
            .ToList();
        var result = RecoKnn.TopSimilar(query, candidates, limit: 5, minSimilarity: 0);

        result.Count.ShouldBe(oracle.Count);
        for (var i = 0; i < oracle.Count; i++)
        {
            result[i].TitleId.ShouldBe(oracle[i].TitleId);
            result[i].Similarity.ShouldBe(oracle[i].Similarity, 1e-6);
        }
        result.ShouldContain(match => match.TitleId == 121);
    }

    [Fact]
    public void ScoreAll_maxSignals_keeps_strongest_neighbours()
    {
        var embeddings = new Dictionary<int, float[]>
        {
            [1] = [1f, 0f],
            [2] = [0.95f, 0.1f],
            [10] = [1f, 0f],
        };
        foreach (var v in embeddings.Values)
        {
            RecoKnn.L2Normalize(v);
        }

        var signals = new Dictionary<int, double>
        {
            [1] = 1.0,
            [2] = -0.4,
        };

        var capped = RecoKnn.ScoreAll(embeddings, signals, k: 1, simMin: 0.1, maxSignals: 1);
        var full = RecoKnn.ScoreAll(embeddings, signals, k: 2, simMin: 0.1, maxSignals: 0);

        // Cap keeps only the +1.0 signal → candidate 10 scores positive.
        capped[10].ShouldBeGreaterThan(0.5);
        // Full set also includes the nearby dislike → lower score.
        full[10].ShouldBeLessThan(capped[10]);
    }
}
