using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pickarr.RecoBench;

public sealed record TemporalInputData(
    DateTime ObservedAt,
    IReadOnlyList<TemporalRatingEvent> Events,
    IReadOnlyList<CurrentRating> CurrentRatings);

public static class TemporalInput
{
    public static TemporalInputData Read(SqliteConnection connection, DateTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (observedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Observation time must be UTC.", nameof(observedAt));
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA query_only";
            if (Convert.ToInt64(check.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("A query-only snapshot connection is required.");
        }

        using var transaction = connection.BeginTransaction(deferred: true);
        using var eventsCommand = connection.CreateCommand();
        eventsCommand.Transaction = transaction;
        eventsCommand.CommandText = "SELECT Id, ProfileId, TitleId, At, Value FROM RatingEvents";
        var events = new List<TemporalRatingEvent>();
        using (var reader = eventsCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                events.Add(new TemporalRatingEvent(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2),
                    ParseTime(reader.GetString(3)), reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        using var ratingsCommand = connection.CreateCommand();
        ratingsCommand.Transaction = transaction;
        ratingsCommand.CommandText = "SELECT ProfileId, TitleId, Value, RatedAt FROM Ratings";
        var ratings = new List<CurrentRating>();
        using (var reader = ratingsCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                if (ParseTime(reader.GetString(3)) > observedAt)
                    throw new InvalidOperationException("A current rating is later than the snapshot observation time.");
                ratings.Add(new CurrentRating(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
            }
        }

        // Validate both sources without using current state to fill the event history.
        TemporalHistory.ReplayBefore(events, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
        TemporalHistory.CurrentSnapshot(ratings, observedAt, observedAt);
        if (events.Any(e => e.At > observedAt))
            throw new InvalidOperationException("An event is later than the snapshot observation time.");
        transaction.Commit();
        return new TemporalInputData(observedAt, events, ratings);
    }

    private static DateTime ParseTime(string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
            throw new InvalidOperationException("Invalid rating timestamp.");
        return time.UtcDateTime;
    }
}
