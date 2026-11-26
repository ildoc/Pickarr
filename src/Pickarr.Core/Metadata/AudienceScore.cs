namespace Pickarr.Core.Metadata;

/// <summary>
/// Audience score from IMDb and TMDB: the mean of both ratings weighted by their vote counts.
/// It describes the title alone; there is no prior and no shrinkage.
/// </summary>
public static class AudienceScore
{
    /// <summary>
    /// <c>(v_imdb·R_imdb + v_tmdb·R_tmdb) / (v_imdb + v_tmdb)</c> over sources with a rating and
    /// a positive vote count. Null without such a source.
    /// </summary>
    public static double? Blend(
        double? imdbRating,
        int? imdbVotes,
        double? tmdbRating,
        int? tmdbVotes)
    {
        double votes = 0;
        double sum = 0;
        if (IsWeightable(imdbRating, imdbVotes))
        {
            votes += imdbVotes!.Value;
            sum += imdbVotes.Value * imdbRating!.Value;
        }

        if (IsWeightable(tmdbRating, tmdbVotes))
        {
            votes += tmdbVotes!.Value;
            sum += tmdbVotes.Value * tmdbRating!.Value;
        }

        return votes > 0 ? sum / votes : null;
    }

    private static bool IsWeightable(double? rating, int? votes) =>
        rating is not null && votes is > 0;
}
