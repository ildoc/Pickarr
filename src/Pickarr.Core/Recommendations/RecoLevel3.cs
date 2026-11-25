namespace Pickarr.Core.Recommendations;

/// <summary>Feature layout for optional supervised level 3 (§12.6). Training lives in Infrastructure (ML.NET).</summary>
public static class RecoLevel3
{
    public const int DefaultMinRatings = 100;
    public const int HashBuckets = 32;
    public const int ScalarFeatureCount = 11;
    public const int FeatureCount = HashBuckets + ScalarFeatureCount;
    public const int SparseFeatureIndex = HashBuckets;
    public const int KnnFeatureIndex = HashBuckets + 1;
    public const int PriorFeatureIndex = HashBuckets + 2;
    public const int SparseWeightFeatureIndex = HashBuckets + 3;
    public const int KnnWeightFeatureIndex = HashBuckets + 4;
    public const int PersonalWeightFeatureIndex = HashBuckets + 5;
    public const int SparseAvailableFeatureIndex = HashBuckets + 6;
    public const int KnnAvailableFeatureIndex = HashBuckets + 7;
    public const int YearFeatureIndex = HashBuckets + 8;
    public const int VoteFeatureIndex = HashBuckets + 9;
    public const int PopularityFeatureIndex = HashBuckets + 10;

    /// <summary>
    /// Features: 32 hashed sparse-term buckets + [Sparse, Knn, Prior, component weights,
    /// personal weight, component availability, yearNorm, voteNorm, popNorm].
    /// </summary>
    public static float[] BuildFeatures(
        SparseVector? titleVector,
        double sparse,
        double knn,
        double prior,
        int? releaseYear,
        double? voteTmdb,
        double? popularity,
        double weightSparse = 1,
        double weightKnn = 0,
        double personalWeight = 0.9,
        bool hasSparse = true,
        bool hasKnn = false)
    {
        var features = new float[FeatureCount];
        if (titleVector is not null)
        {
            foreach (var (term, weight) in titleVector.Weights)
            {
                var bucket = (HashTerm(term) & 0x7FFFFFFF) % HashBuckets;
                features[bucket] += (float)weight;
            }
        }

        features[SparseFeatureIndex] = (float)sparse;
        features[KnnFeatureIndex] = (float)knn;
        features[PriorFeatureIndex] = (float)prior;
        features[SparseWeightFeatureIndex] = (float)weightSparse;
        features[KnnWeightFeatureIndex] = (float)weightKnn;
        features[PersonalWeightFeatureIndex] = (float)personalWeight;
        features[SparseAvailableFeatureIndex] = hasSparse ? 1 : 0;
        features[KnnAvailableFeatureIndex] = hasKnn ? 1 : 0;
        features[YearFeatureIndex] = releaseYear is { } y
            ? (float)Math.Clamp((y - 1950) / 80.0, 0, 1)
            : 0.5f;
        features[VoteFeatureIndex] = voteTmdb is { } v
            ? (float)Math.Clamp(v / 10.0, 0, 1)
            : 0.5f;
        features[PopularityFeatureIndex] = popularity is { } p
            ? (float)Math.Clamp(Math.Log10(1 + Math.Max(0, p)) / 3.0, 0, 1)
            : 0.5f;

        return features;
    }

    private static int HashTerm(string term)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var ch in term)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return (int)hash;
        }
    }
}

public sealed record RecoLevel3Example(
    int TitleId,
    bool IsLike,
    float[] Features);
