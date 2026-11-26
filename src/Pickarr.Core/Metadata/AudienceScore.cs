namespace Pickarr.Core.Metadata;

/// <summary>Prior of the audience score: catalog mean <c>C</c> and the TMDB-to-IMDb offset.</summary>
public readonly record struct AudiencePrior(double Mean, double TmdbOffset);

/// <summary>IMDb and TMDB rating of one catalog title, used to fit <see cref="AudiencePrior"/>.</summary>
public readonly record struct AudienceSample(
    double? ImdbRating,
    int? ImdbVotes,
    double? TmdbRating,
    int? TmdbVotes);

/// <summary>
/// Audience score from IMDb and TMDB. Votes of both sources are pooled into one Bayesian
/// average toward the catalog mean; TMDB is first moved onto the IMDb scale.
/// </summary>
public static class AudienceScore
{
    public const double MinimumVotes = 500;
    public const double TmdbVoteWeight = 1;
    public const int CalibrationMinimumVotes = 50;
    public const int CalibrationMinimumPairs = 20;

    /// <summary>Ranking score stored as <c>VoteBlended</c>. Null without a source that has votes.</summary>
    public static double? Blend(
        double? imdbRating,
        int? imdbVotes,
        double? tmdbRating,
        int? tmdbVotes,
        AudiencePrior prior)
    {
        var imdbWeightable = IsWeightable(imdbRating, imdbVotes);
        var tmdbWeightable = IsWeightable(tmdbRating, tmdbVotes);
        if (!imdbWeightable && !tmdbWeightable)
        {
            return null;
        }

        var votes = MinimumVotes;
        var sum = MinimumVotes * prior.Mean;
        if (imdbWeightable)
        {
            votes += imdbVotes!.Value;
            sum += imdbVotes.Value * imdbRating!.Value;
        }

        if (tmdbWeightable)
        {
            var weighted = TmdbVoteWeight * tmdbVotes!.Value;
            votes += weighted;
            sum += weighted * OnImdbScale(tmdbRating!.Value, prior.TmdbOffset);
        }

        return sum / votes;
    }

    /// <summary>
    /// Score shown on cards and as the dialog headline: the blend when both sources have votes,
    /// otherwise the raw rating of the only source with votes.
    /// </summary>
    public static double? Display(
        double? imdbRating,
        int? imdbVotes,
        double? tmdbRating,
        int? tmdbVotes,
        double? blended)
    {
        var imdbWeightable = IsWeightable(imdbRating, imdbVotes);
        var tmdbWeightable = IsWeightable(tmdbRating, tmdbVotes);
        if (imdbWeightable && tmdbWeightable)
        {
            return blended;
        }

        if (imdbWeightable)
        {
            return imdbRating;
        }

        return tmdbWeightable ? tmdbRating : null;
    }

    /// <summary>
    /// Fits the prior on catalog titles. <c>C</c> is the plain mean of IMDb ratings with votes,
    /// or <paramref name="fallbackMean"/> when none. The TMDB offset is the mean IMDb − TMDB gap
    /// over titles with at least <see cref="CalibrationMinimumVotes"/> on both sources, or 0 with
    /// fewer than <see cref="CalibrationMinimumPairs"/> such titles.
    /// </summary>
    public static AudiencePrior Calibrate(IEnumerable<AudienceSample> samples, double fallbackMean)
    {
        double imdbSum = 0;
        var imdbCount = 0;
        double gapSum = 0;
        var gapCount = 0;
        foreach (var sample in samples)
        {
            if (!IsWeightable(sample.ImdbRating, sample.ImdbVotes))
            {
                continue;
            }

            imdbSum += sample.ImdbRating!.Value;
            imdbCount++;
            if (sample.ImdbVotes >= CalibrationMinimumVotes
                && sample.TmdbRating is { } tmdb
                && sample.TmdbVotes >= CalibrationMinimumVotes)
            {
                gapSum += sample.ImdbRating.Value - tmdb;
                gapCount++;
            }
        }

        var mean = imdbCount == 0 ? fallbackMean : imdbSum / imdbCount;
        var offset = gapCount < CalibrationMinimumPairs ? 0 : gapSum / gapCount;
        return new AudiencePrior(mean, offset);
    }

    private static bool IsWeightable(double? rating, int? votes) =>
        rating is not null && votes is > 0;

    private static double OnImdbScale(double tmdbRating, double offset) =>
        Math.Clamp(tmdbRating + offset, 0, 10);
}
