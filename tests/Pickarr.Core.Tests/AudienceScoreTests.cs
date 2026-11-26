using Pickarr.Core.Metadata;
using Shouldly;

namespace Pickarr.Core.Tests;

public sealed class AudienceScoreTests
{
    private static readonly AudiencePrior Prior = new(Mean: 7, TmdbOffset: 0);

    [Fact]
    public void Minimum_votes_threshold_is_500()
    {
        AudienceScore.MinimumVotes.ShouldBe(500);
    }

    [Fact]
    public void Two_equal_sources_pool_toward_the_prior()
    {
        // (8·500 + 6·500 + 7·500) / 1500
        AudienceScore.Blend(8.0, 500, 6.0, 500, Prior).ShouldBe(7.0);
    }

    [Fact]
    public void Votes_are_pooled_so_the_larger_source_dominates()
    {
        // (8·100000 + 6·500 + 7·500) / 101000
        var score = AudienceScore.Blend(8.0, 100_000, 6.0, 500, Prior);

        score.ShouldNotBeNull();
        score!.Value.ShouldBe(806_500.0 / 101_000, 1e-12);
    }

    [Fact]
    public void Single_source_is_shrunk_like_two_sources()
    {
        var score = AudienceScore.Blend(9.1, 12, null, null, Prior);

        score.ShouldNotBeNull();
        score!.Value.ShouldBe((9.1 * 12 + 7 * 500) / 512, 1e-12);
    }

    [Fact]
    public void A_few_extra_votes_barely_move_the_score()
    {
        var imdbOnly = AudienceScore.Blend(9.1, 12, null, null, Prior)!.Value;
        var withTmdb = AudienceScore.Blend(9.1, 12, 9.0, 3, Prior)!.Value;

        Math.Abs(withTmdb - imdbOnly).ShouldBeLessThan(0.05);
    }

    [Fact]
    public void Zero_vote_source_does_not_enter_the_blend()
    {
        AudienceScore.Blend(9.0, 0, 8.0, 100, Prior)
            .ShouldBe(AudienceScore.Blend(null, null, 8.0, 100, Prior));
    }

    [Fact]
    public void Tmdb_is_moved_by_the_offset_and_clamped_to_ten()
    {
        var shifted = AudienceScore.Blend(null, null, 6.0, 500, new AudiencePrior(7, 0.5));
        shifted.ShouldNotBeNull();
        shifted!.Value.ShouldBe((6.5 * 500 + 7 * 500) / 1000, 1e-12);

        var clamped = AudienceScore.Blend(null, null, 9.8, 500, new AudiencePrior(7, 0.5));
        clamped.ShouldNotBeNull();
        clamped!.Value.ShouldBe((10.0 * 500 + 7 * 500) / 1000, 1e-12);
    }

    [Fact]
    public void Ratings_without_positive_votes_are_absent()
    {
        AudienceScore.Blend(9.0, 0, 8.0, 0, Prior).ShouldBeNull();
        AudienceScore.Blend(9.1, null, null, null, Prior).ShouldBeNull();
        AudienceScore.Blend(null, null, null, null, Prior).ShouldBeNull();
        AudienceScore.Blend(null, 10, null, 10, Prior).ShouldBeNull();
    }

    [Fact]
    public void Display_uses_the_blend_only_when_both_sources_have_votes()
    {
        AudienceScore.Display(8.0, 500, 6.0, 500, blended: 7.0).ShouldBe(7.0);
        AudienceScore.Display(9.1, 12, null, null, blended: 7.05).ShouldBe(9.1);
        AudienceScore.Display(9.0, 0, 8.0, 100, blended: 7.17).ShouldBe(8.0);
        AudienceScore.Display(null, null, 0.0, 0, blended: null).ShouldBeNull();
    }

    [Fact]
    public void Calibrate_uses_the_plain_catalog_imdb_mean()
    {
        var prior = AudienceScore.Calibrate(
            [
                new AudienceSample(8.0, 1_000_000, null, null),
                new AudienceSample(6.0, 10, null, null),
                new AudienceSample(null, null, 9.0, 100),
                new AudienceSample(1.0, 0, null, null),
            ],
            fallbackMean: 5);

        prior.Mean.ShouldBe(7.0);
        prior.TmdbOffset.ShouldBe(0);
    }

    [Fact]
    public void Calibrate_falls_back_without_catalog_imdb_votes()
    {
        AudienceScore.Calibrate([], fallbackMean: 6.4).ShouldBe(new AudiencePrior(6.4, 0));
    }

    [Fact]
    public void Calibrate_offset_needs_enough_well_voted_pairs()
    {
        var pairs = Enumerable.Range(0, AudienceScore.CalibrationMinimumPairs)
            .Select(_ => new AudienceSample(7.0, 1000, 6.6, 100))
            .Append(new AudienceSample(9.0, 1000, 1.0, AudienceScore.CalibrationMinimumVotes - 1))
            .ToList();

        AudienceScore.Calibrate(pairs, fallbackMean: 0).TmdbOffset.ShouldBe(0.4, 1e-12);
        AudienceScore.Calibrate(pairs.Skip(1), fallbackMean: 0).TmdbOffset.ShouldBe(0);
    }
}
