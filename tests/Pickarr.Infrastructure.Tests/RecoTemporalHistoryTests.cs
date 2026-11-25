using Pickarr.RecoBench;
using Shouldly;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace Pickarr.Infrastructure.Tests;

public sealed class RecoTemporalHistoryTests
{
    private static readonly DateTime Epoch = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime Day(int day) => Epoch.AddDays(day);
    private static TemporalRatingEvent E(long id, int profile, int title, int day, string? value) =>
        new(id, profile, title, Day(day), value);

    [Fact]
    public void Split_is_per_profile_keeps_ties_together_and_reports_insufficient_history()
    {
        var events = new[]
        {
            E(1, 1, 1, 0, "Love"), E(2, 1, 2, 1, "Like"), E(3, 1, 3, 1, "Dislike"),
            E(4, 1, 4, 2, "Like"), E(5, 1, 5, 3, "Love"), E(6, 1, 6, 4, "Like"),
            E(7, 2, 1, 0, "Like"), E(8, 2, 2, 4, "Love")
        };
        var split = TemporalHistory.Split(1, events.Reverse()).Split.ShouldNotBeNull();
        split.ValidationCutoff.ShouldBe(Day(3));
        split.TestCutoff.ShouldBe(Day(4));
        split.Train.Select(e => e.Id).ShouldBe(new long[] { 1, 2, 3, 4 });
        split.Validation.Select(e => e.Id).ShouldBe(new long[] { 5 });
        split.Test.Select(e => e.Id).ShouldBe(new long[] { 6 });
        var insufficient = TemporalHistory.Split(2, events);
        insufficient.Split.ShouldBeNull();
        insufficient.UnavailableReason.ShouldBe("insufficient_distinct_timestamps");
    }

    [Fact]
    public void Replay_handles_updates_deletions_conflicts_and_recovery_without_id_order_assumptions()
    {
        var events = new[]
        {
            E(1, 1, 1, 0, "Love"), E(2, 1, 1, 1, "Dislike"), E(3, 1, 1, 2, null),
            E(4, 1, 1, 3, null), E(5, 1, 1, 3, "Like"), E(6, 1, 1, 4, "Love"),
            E(7, 2, 1, 0, "not_interested"), E(8, 2, 1, 0, "NotInterested")
        };
        TemporalHistory.ReplayBefore(events, Day(1))[(1, 1)].Value.ShouldBe("Love");
        TemporalHistory.ReplayBefore(events, Day(2))[(1, 1)].Value.ShouldBe("Dislike");
        TemporalHistory.ReplayBefore(events, Day(3))[(1, 1)].Status.ShouldBe(TemporalRatingStatus.Deleted);
        var ambiguous = TemporalHistory.ReplayBefore(events, Day(4));
        ambiguous[(1, 1)].Status.ShouldBe(TemporalRatingStatus.Ambiguous);
        ambiguous[(2, 1)].Value.ShouldBe("NotInterested");
        ambiguous.ContainsKey((1, 99)).ShouldBeFalse();
        TemporalHistory.ReplayBefore(events, Day(5))[(1, 1)].Value.ShouldBe("Love");
    }

    [Fact]
    public void Frozen_cutoff_excludes_future_updates_deletes_and_other_profile_events()
    {
        var prefix = new[] { E(1, 1, 1, 0, "Love"), E(2, 2, 2, 1, "Dislike") };
        var futureA = prefix.Concat(new[] { E(3, 1, 1, 3, null), E(4, 2, 2, 4, "Like") });
        var futureB = prefix.Concat(new[] { E(3, 1, 1, 3, "Dislike"), E(4, 2, 2, 4, null) });
        var expected = TemporalHistory.ReplayBefore(prefix, Day(3));
        var first = TemporalHistory.ReplayBefore(futureA, Day(3));
        var second = TemporalHistory.ReplayBefore(futureB, Day(3));
        first.Count.ShouldBe(2);
        first[(1, 1)].ShouldBe(expected[(1, 1)]);
        second[(1, 1)].ShouldBe(expected[(1, 1)]);
        first[(2, 2)].ShouldBe(expected[(2, 2)]);
        second[(2, 2)].ShouldBe(expected[(2, 2)]);
    }

    [Fact]
    public void Snapshot_cannot_backdate_current_value_or_fill_missing_history()
    {
        var current = new[] { new CurrentRating(1, 1, "Dislike") };
        var observed = TemporalHistory.CurrentSnapshot(current, Day(5), Day(5));
        observed[(1, 1)].Value.ShouldBe("Dislike");
        Should.Throw<ArgumentException>(() => TemporalHistory.CurrentSnapshot(current, Day(5), Day(1)));
        TemporalHistory.ReplayBefore(Array.Empty<TemporalRatingEvent>(), Day(5)).ShouldBeEmpty();
        var previous = TemporalHistory.ReplayBefore(new[] { E(1, 1, 1, 0, "Love") }, Day(1));
        previous[(1, 1)].Value.ShouldBe("Love");
    }

    [Fact]
    public void Invalid_events_and_snapshot_are_rejected_instead_of_guessing()
    {
        Should.Throw<ArgumentException>(() => TemporalHistory.ReplayBefore(
            new[] { E(1, 1, 1, 0, "unknown") }, Day(1)));
        Should.Throw<ArgumentException>(() => TemporalHistory.Split(1,
            new[] { E(1, 1, 1, 0, "Like"), E(1, 1, 2, 1, "Love") }));
        Should.Throw<ArgumentException>(() => TemporalHistory.ReplayBefore(
            new[] { new TemporalRatingEvent(1, 1, 1, DateTime.SpecifyKind(Day(0), DateTimeKind.Unspecified), "Like") }, Day(1)));
        Should.Throw<ArgumentException>(() => TemporalHistory.CurrentSnapshot(
            new[] { new CurrentRating(1, 1, "Like"), new CurrentRating(1, 1, "Love") }, Day(1), Day(1)));
    }

    [Fact]
    public void Reads_real_schema_query_only_without_changing_synthetic_snapshot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pickarr.slnx")))
            directory = directory.Parent;
        var root = Path.Combine(directory!.FullName, "obj", "reco-temporal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "fixture.db");
        try
        {
            using (var writer = new SqliteConnection($"Data Source={file};Pooling=False"))
            {
                writer.Open();
                using var command = writer.CreateCommand();
                command.CommandText = """
                    CREATE TABLE RatingEvents (Id INTEGER PRIMARY KEY, ProfileId INTEGER, TitleId INTEGER, At TEXT, Value TEXT);
                    CREATE TABLE Ratings (ProfileId INTEGER, TitleId INTEGER, Value TEXT, RatedAt TEXT);
                    INSERT INTO RatingEvents VALUES (1,1,1,'2025-01-01 00:00:00','Love'),
                        (2,1,1,'2025-01-03 00:00:00','Dislike'), (3,1,2,'2025-01-04 00:00:00',NULL);
                    INSERT INTO Ratings VALUES (1,1,'Dislike','2025-01-03 00:00:00'), (1,3,'Like','2025-01-01 00:00:00');
                    """;
                command.ExecuteNonQuery();
                Should.Throw<InvalidOperationException>(() => TemporalInput.Read(writer, Day(5)));
            }
            var before = SHA256.HashData(File.ReadAllBytes(file));
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA query_only=ON";
                command.ExecuteNonQuery();
                var input = TemporalInput.Read(connection, Day(5));
                var replay = TemporalHistory.ReplayBefore(input.Events, Day(2));
                replay[(1, 1)].Value.ShouldBe("Love");
                replay.ContainsKey((1, 3)).ShouldBeFalse();
                TemporalHistory.CurrentSnapshot(input.CurrentRatings, Day(5), Day(5))[(1, 1)].Value.ShouldBe("Dislike");
                Should.Throw<InvalidOperationException>(() => TemporalInput.Read(connection, Day(1)));
            }
            SHA256.HashData(File.ReadAllBytes(file)).ShouldBe(before);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
