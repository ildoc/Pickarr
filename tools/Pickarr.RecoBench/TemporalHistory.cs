using Pickarr.Core.Domain;

namespace Pickarr.RecoBench;

public sealed record TemporalRatingEvent(long Id, int ProfileId, int TitleId, DateTime At, string? Value);

public enum TemporalRatingStatus { Rated, Deleted, Ambiguous }

public sealed record TemporalRatingState(TemporalRatingStatus Status, string? Value);

public sealed record TemporalSplit(
    DateTime ValidationCutoff,
    DateTime TestCutoff,
    IReadOnlyList<TemporalRatingEvent> Train,
    IReadOnlyList<TemporalRatingEvent> Validation,
    IReadOnlyList<TemporalRatingEvent> Test);

public sealed record TemporalSplitResult(TemporalSplit? Split, string? UnavailableReason);

public sealed record CurrentRating(int ProfileId, int TitleId, string Value);

public static class TemporalHistory
{
    public const string Protocol = "temporal-v1";

    public static TemporalSplitResult Split(int profileId, IEnumerable<TemporalRatingEvent> events)
    {
        var ordered = Validate(events).Where(e => e.ProfileId == profileId)
            .OrderBy(e => e.At).ThenBy(e => e.Id).ToArray();
        var times = ordered.Select(e => e.At).Distinct().ToArray();
        if (times.Length < 3)
            return new TemporalSplitResult(null, "insufficient_distinct_timestamps");

        var trainCount = Math.Clamp((int)(times.Length * 3L / 5), 1, times.Length - 2);
        var validationCount = Math.Clamp(times.Length / 5, 1, times.Length - trainCount - 1);
        var validationCutoff = times[trainCount];
        var testCutoff = times[trainCount + validationCount];
        return new TemporalSplitResult(new TemporalSplit(
            validationCutoff, testCutoff,
            ordered.Where(e => e.At < validationCutoff).ToArray(),
            ordered.Where(e => e.At >= validationCutoff && e.At < testCutoff).ToArray(),
            ordered.Where(e => e.At >= testCutoff).ToArray()), null);
    }

    public static IReadOnlyDictionary<(int ProfileId, int TitleId), TemporalRatingState> ReplayBefore(
        IEnumerable<TemporalRatingEvent> events, DateTime cutoff)
    {
        RequireUtc(cutoff);
        var states = new Dictionary<(int, int), TemporalRatingState>();
        foreach (var group in Validate(events).Where(e => e.At < cutoff)
                     .GroupBy(e => (e.At, e.ProfileId, e.TitleId))
                     .OrderBy(g => g.Key.At))
        {
            var values = group.Select(e => Normalize(e.Value)).Distinct(StringComparer.Ordinal).ToArray();
            states[(group.Key.ProfileId, group.Key.TitleId)] = values.Length != 1
                ? new TemporalRatingState(TemporalRatingStatus.Ambiguous, null)
                : values[0] is null
                    ? new TemporalRatingState(TemporalRatingStatus.Deleted, null)
                    : new TemporalRatingState(TemporalRatingStatus.Rated, values[0]);
        }
        return states;
    }

    public static IReadOnlyDictionary<(int ProfileId, int TitleId), TemporalRatingState> CurrentSnapshot(
        IEnumerable<CurrentRating> ratings, DateTime observedAt, DateTime requestedAt)
    {
        RequireUtc(observedAt);
        RequireUtc(requestedAt);
        if (observedAt != requestedAt)
            throw new ArgumentException("Current ratings are only available at the snapshot observation time.");
        var states = new Dictionary<(int, int), TemporalRatingState>();
        foreach (var rating in ratings)
        {
            if (rating.ProfileId <= 0 || rating.TitleId <= 0 || !RatingValues.IsValid(rating.Value))
                throw new ArgumentException("Invalid current rating.", nameof(ratings));
            if (!states.TryAdd((rating.ProfileId, rating.TitleId),
                    new TemporalRatingState(TemporalRatingStatus.Rated, RatingValues.Normalize(rating.Value))))
                throw new ArgumentException("Duplicate current rating.", nameof(ratings));
        }
        return states;
    }

    private static TemporalRatingEvent[] Validate(IEnumerable<TemporalRatingEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var result = events.ToArray();
        var ids = new HashSet<long>();
        foreach (var e in result)
        {
            if (e.Id <= 0 || e.ProfileId <= 0 || e.TitleId <= 0 || !ids.Add(e.Id))
                throw new ArgumentException("Invalid or duplicate event identity.", nameof(events));
            RequireUtc(e.At);
            if (e.Value is not null && !RatingValues.IsValid(e.Value))
                throw new ArgumentException("Invalid event value.", nameof(events));
        }
        return result;
    }

    private static string? Normalize(string? value) => value is null ? null : RatingValues.Normalize(value);

    private static void RequireUtc(DateTime time)
    {
        if (time.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamps must be UTC.");
    }
}
