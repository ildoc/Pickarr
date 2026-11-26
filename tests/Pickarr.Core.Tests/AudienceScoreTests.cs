using Pickarr.Core.Metadata;
using Shouldly;

namespace Pickarr.Core.Tests;

public sealed class AudienceScoreTests
{
    [Fact]
    public void Two_sources_average_by_vote_count()
    {
        // IMDb 7.4 / 799, TMDB 7.1 / 20: between the two, close to IMDb.
        var score = AudienceScore.Blend(7.4, 799, 7.1, 20);

        score.ShouldNotBeNull();
        score!.Value.ShouldBe((7.4 * 799 + 7.1 * 20) / 819, 1e-12);
        score.Value.ShouldBeInRange(7.1, 7.4);
    }

    [Fact]
    public void Equal_counts_give_the_plain_mean()
    {
        AudienceScore.Blend(8.0, 500, 6.0, 500)!.Value.ShouldBe(7.0, 1e-12);
    }

    [Fact]
    public void Single_source_keeps_its_rating_whatever_the_count()
    {
        AudienceScore.Blend(9.1, 12, null, null)!.Value.ShouldBe(9.1, 1e-12);
        AudienceScore.Blend(null, null, 7.4, 80)!.Value.ShouldBe(7.4, 1e-12);
    }

    [Fact]
    public void Zero_vote_source_does_not_enter_the_mean()
    {
        AudienceScore.Blend(9.0, 0, 8.0, 100)!.Value.ShouldBe(8.0, 1e-12);
        AudienceScore.Blend(9.0, null, 8.0, 100)!.Value.ShouldBe(8.0, 1e-12);
    }

    [Fact]
    public void Ratings_without_positive_votes_are_absent()
    {
        AudienceScore.Blend(9.0, 0, 8.0, 0).ShouldBeNull();
        AudienceScore.Blend(9.1, null, null, null).ShouldBeNull();
        AudienceScore.Blend(null, null, 0.0, 0).ShouldBeNull();
        AudienceScore.Blend(null, null, null, null).ShouldBeNull();
        AudienceScore.Blend(null, 10, null, 10).ShouldBeNull();
    }
}
