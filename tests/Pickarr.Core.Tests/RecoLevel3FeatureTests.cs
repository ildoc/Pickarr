using Pickarr.Core.Recommendations;
using Shouldly;

namespace Pickarr.Core.Tests;

public sealed class RecoLevel3FeatureTests
{
    [Fact]
    public void Vote_feature_uses_the_blended_score_and_appends_the_usable_count()
    {
        var features = RecoLevel3.BuildFeatures(
            titleVector: null,
            sparse: 0,
            knn: 0,
            prior: 0,
            releaseYear: null,
            vote: 8,
            popularity: null,
            usableVotes: 2_000);

        features.Length.ShouldBe(RecoLevel3.FeatureCount);
        RecoLevel3.ScalarFeatureCount.ShouldBe(12);
        RecoLevel3.VoteFeatureIndex.ShouldBe(RecoLevel3.HashBuckets + 9);
        RecoLevel3.PopularityFeatureIndex.ShouldBe(RecoLevel3.HashBuckets + 10);
        RecoLevel3.VoteCountFeatureIndex.ShouldBe(RecoLevel3.HashBuckets + 11);
        features[RecoLevel3.VoteFeatureIndex].ShouldBe(0.8f);
        features[RecoLevel3.PopularityFeatureIndex].ShouldBe(0.5f);
        features[RecoLevel3.VoteCountFeatureIndex].ShouldBe((float)(Math.Log10(2001) / 6), 1e-6f);

        var missing = RecoLevel3.BuildFeatures(null, 0, 0, 0, null, vote: null, popularity: null);
        missing[RecoLevel3.VoteFeatureIndex].ShouldBe(0.5f);
        missing[RecoLevel3.VoteCountFeatureIndex].ShouldBe(0);
    }

    [Fact]
    public void Usable_vote_count_sums_only_sources_with_a_rating_and_a_positive_count()
    {
        RecoLevel3.UsableVoteCount(8, 500, 6, 1_500).ShouldBe(2_000);
        RecoLevel3.UsableVoteCount(8, 0, 6, 100).ShouldBe(100);
        RecoLevel3.UsableVoteCount(null, 500, 6, 80).ShouldBe(80);
        RecoLevel3.UsableVoteCount(null, null, null, null).ShouldBe(0);

        var capped = RecoLevel3.BuildFeatures(null, 0, 0, 0, null, vote: 1, popularity: null, usableVotes: 10_000_000);
        capped[RecoLevel3.VoteCountFeatureIndex].ShouldBe(1f);
    }
}
