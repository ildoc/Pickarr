using Pickarr.Core.Recommendations;
using Shouldly;

namespace Pickarr.Core.Tests;

public sealed class RecoEvalMetricsTests
{
    [Fact]
    public void PairwiseAuc_perfect_ranking_is_one()
    {
        RecoEvalMetrics.PairwiseAuc(0.9, [0.1, 0.2, 0.3]).ShouldBe(1.0);
    }

    [Fact]
    public void PairwiseAuc_ties_count_half()
    {
        RecoEvalMetrics.PairwiseAuc(0.5, [0.5, 0.5]).ShouldBe(0.5);
        RecoEvalMetrics.PairwiseAuc(0.5, [0.5 - 1e-13, 0.5, 0.5 + 1e-13]).ShouldBe(0.5);
        RecoEvalMetrics.PairwiseAuc(0.5, []).ShouldBe(0.5);
    }

    [Fact]
    public void Labelled_auc_requires_both_classes_and_counts_exact_ties_half()
    {
        RecoEvalMetrics.LikeVsDislikeAuc([]).ShouldBeNull();
        RecoEvalMetrics.LikeVsDislikeAuc([(true, 0.8)]).ShouldBeNull();
        RecoEvalMetrics.LikeVsDislikeAuc([(false, 0.2)]).ShouldBeNull();
        RecoEvalMetrics.LikeVsDislikeAuc([(true, 0.8), (false, 0.2), (false, 0.8), (false, 0.9)])
            .ShouldBe(0.5);
    }

    [Fact]
    public void HitAt_true_when_few_better()
    {
        RecoEvalMetrics.HitAt(0.8, [0.9, 0.1, 0.2], k: 2).ShouldBeTrue();
        RecoEvalMetrics.HitAt(0.8, [0.95, 0.9, 0.85], k: 2).ShouldBeFalse();
        RecoEvalMetrics.HitAt(0.8, [0.8, 0.8], k: 1).ShouldBeTrue();
        RecoEvalMetrics.HitAt(0.8, [], k: 1).ShouldBeTrue();
    }

    [Fact]
    public void ShouldActivateLevel3_requires_margin_and_counts()
    {
        var combined = new RecoEvalLevelMetrics(0.70, 0.4, 20);
        var l3 = new RecoEvalLevelMetrics(0.72, 0.5, 20);

        RecoEvalMetrics.ShouldActivateLevel3(combined, l3, likeCount: 50, dislikeCount: 50, minRatings: 100)
            .ShouldBeTrue();

        RecoEvalMetrics.ShouldActivateLevel3(combined, l3, likeCount: 10, dislikeCount: 10, minRatings: 100)
            .ShouldBeFalse();

        RecoEvalMetrics.ShouldActivateLevel3(combined, new RecoEvalLevelMetrics(0.705, 0.4, 20), 50, 50, 100)
            .ShouldBeFalse();
    }

    [Fact]
    public void ShouldActivateLevel3_rejects_superior_combined_baseline_and_partial_folds()
    {
        var combined = new RecoEvalLevelMetrics(0.80, 0.8, 20);
        var weakerL3 = new RecoEvalLevelMetrics(0.70, 0.6, 20);

        RecoEvalMetrics.ShouldActivateLevel3(combined, weakerL3, 50, 50, 100).ShouldBeFalse();
        RecoEvalMetrics.ShouldActivateLevel3(
            new RecoEvalLevelMetrics(0.70, 0.8, 20),
            new RecoEvalLevelMetrics(0.90, 0.9, 19),
            50,
            50,
            100).ShouldBeFalse();
        RecoEvalMetrics.ShouldActivateLevel3(
            new RecoEvalLevelMetrics(0.70, 0.8, 9),
            new RecoEvalLevelMetrics(0.90, 0.9, 9),
            50,
            50,
            100).ShouldBeFalse();
    }

    [Fact]
    public void Level3_features_have_fixed_dimension()
    {
        var features = RecoLevel3.BuildFeatures(
            SparseVector.Empty,
            sparse: 0.4,
            knn: 0.6,
            prior: 0.5,
            releaseYear: 2010,
            vote: 7.5,
            popularity: 20,
            weightSparse: 0.4,
            weightKnn: 0.6,
            personalWeight: 0.9,
            hasSparse: true,
            hasKnn: false);
        features.Length.ShouldBe(RecoLevel3.FeatureCount);
        features[RecoLevel3.SparseWeightFeatureIndex].ShouldBe(0.4f);
        features[RecoLevel3.KnnWeightFeatureIndex].ShouldBe(0.6f);
        features[RecoLevel3.PersonalWeightFeatureIndex].ShouldBe(0.9f);
        features[RecoLevel3.KnnAvailableFeatureIndex].ShouldBe(0);
    }
}
