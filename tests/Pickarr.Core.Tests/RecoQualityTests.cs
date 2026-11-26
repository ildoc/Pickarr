using Pickarr.Core.Recommendations;
using Shouldly;

namespace Pickarr.Core.Tests;

public sealed class RecoQualityTests
{
    [Fact]
    public void Quality_is_the_percentile_of_the_blended_score()
    {
        var quality = RecoQuality.Compute(
        [
            new RecoTitleStats(1, 8.5, null),
            new RecoTitleStats(2, 6.0, null),
            new RecoTitleStats(3, 7.0, null),
            new RecoTitleStats(4, 7.0, null),
            new RecoTitleStats(5, 9.0, null),
        ]);

        quality[2].ShouldBe(0);
        quality[3].ShouldBe(0.375);
        quality[4].ShouldBe(0.375);
        quality[1].ShouldBe(0.75);
        quality[5].ShouldBe(1);
    }

    [Fact]
    public void Missing_score_or_a_single_rated_title_is_neutral()
    {
        var quality = RecoQuality.Compute(
        [
            new RecoTitleStats(1, 8.5, null),
            new RecoTitleStats(2, null, null),
        ]);

        quality[1].ShouldBe(RecoQuality.Neutral);
        quality[2].ShouldBe(RecoQuality.Neutral);
    }

    [Fact]
    public void Apply_boosts_high_quality_and_damps_low_quality()
    {
        RecoQuality.Apply(0.6, 1, 0.3).ShouldBe(0.6 * 1.15, 1e-12);
        RecoQuality.Apply(0.6, 0, 0.3).ShouldBe(0.6 * 0.85, 1e-12);
        RecoQuality.Apply(0.6, RecoQuality.Neutral, 0.3).ShouldBe(0.6);
        RecoQuality.Apply(0.6, 1, 0).ShouldBe(0.6);
        RecoQuality.Apply(0.6, 1, 5).ShouldBe(0.6 * 1.5, 1e-12);
    }
}
