namespace Pickarr.Core.Recommendations;

/// <summary>
/// Audience-quality multiplier applied to the fused score at every stage (§12.5bis).
/// Quality is the percentile of <c>VoteBlended</c> in the pool; titles above the median
/// gain, titles below lose, a missing score is neutral.
/// </summary>
public static class RecoQuality
{
    public const double DefaultWeight = 0.3;
    public const double Neutral = 0.5;

    /// <summary>Percentile rank of each title's blended score in [0, 1]; ties share the mean rank.</summary>
    public static IReadOnlyDictionary<int, double> Compute(IReadOnlyList<RecoTitleStats> pool)
    {
        var result = new Dictionary<int, double>(pool.Count);
        var rated = pool
            .Where(t => t.VoteBlended is not null)
            .OrderBy(t => t.VoteBlended!.Value)
            .ToList();
        foreach (var title in pool)
        {
            result[title.TitleId] = Neutral;
        }

        if (rated.Count < 2)
        {
            return result;
        }

        var last = rated.Count - 1;
        var start = 0;
        while (start < rated.Count)
        {
            var end = start;
            while (end + 1 < rated.Count && rated[end + 1].VoteBlended == rated[start].VoteBlended)
            {
                end++;
            }

            var percentile = (start + end) / 2.0 / last;
            for (var i = start; i <= end; i++)
            {
                result[rated[i].TitleId] = percentile;
            }

            start = end + 1;
        }

        return result;
    }

    /// <summary><c>score · (1 + weight · (quality − 0.5))</c>; weight is clamped to [0, 1].</summary>
    public static double Apply(double score, double quality, double weight) =>
        score * (1 + Math.Clamp(weight, 0, 1) * (quality - Neutral));
}
