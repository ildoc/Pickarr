using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain;
using Pickarr.Infrastructure.Catalog;
using Pickarr.Infrastructure.Jobs;
using Pickarr.Infrastructure.Metadata;
using Pickarr.Infrastructure.Persistence;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class ImdbRatingsJobTests
{
    [Fact]
    public async Task Successful_import_enqueues_one_reco_build()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Importer.ImportAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new ImdbRatingImportResult(7.2, 2, 3));

        await harness.Job.ExecuteAsync();

        var run = await harness.Db.SyncRuns.SingleAsync();
        run.Kind.ShouldBe(SyncRunKinds.ImdbRatings);
        run.Status.ShouldBe(SyncRunStatuses.Succeeded);
        run.TriggeredBy.ShouldBe(SyncRunTriggers.Schedule);
        run.PagesFetched.ShouldBe(2);
        run.TitlesUpdated.ShouldBe(3);
        harness.Jobs.Received(1).Create(
            Arg.Is<Job>(job => job.Type == typeof(RecoBuildJob)),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task Manual_import_is_recorded_as_manual()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Importer.ImportAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new ImdbRatingImportResult(7.01, 2, 1));

        await harness.Job.ExecuteAsync(isManual: true);

        var run = await harness.Db.SyncRuns.SingleAsync();
        run.Status.ShouldBe(SyncRunStatuses.Succeeded);
        run.TriggeredBy.ShouldBe(SyncRunTriggers.Manual);
    }

    [Fact]
    public async Task Failed_download_marks_the_run_failed_and_does_not_enqueue()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Source.OpenAsync(Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new HttpRequestException("offline"));

        await Should.ThrowAsync<HttpRequestException>(() => harness.Job.ExecuteAsync());

        var run = await harness.Db.SyncRuns.SingleAsync();
        run.Status.ShouldBe(SyncRunStatuses.Failed);
        run.ErrorMessage.ShouldBe("offline");
        await harness.Importer.DidNotReceive().ImportAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        harness.Jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public required PickarrDbContext Db { get; init; }
        public required IImdbRatingsSource Source { get; init; }
        public required IImdbRatingImporter Importer { get; init; }
        public required IBackgroundJobClient Jobs { get; init; }
        public required ImdbRatingsJob Job { get; init; }

        private Harness(SqliteConnection connection) => _connection = connection;

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new PickarrDbContext(
                new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var settings = Substitute.For<ISettingsStore>();
            var runs = new EfSyncRunStore(db, new FixedClock(new DateTime(2026, 11, 29, 4, 0, 0, DateTimeKind.Utc)), settings);
            var source = Substitute.For<IImdbRatingsSource>();
            source.OpenAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<Stream>(new MemoryStream([1])));
            var importer = Substitute.For<IImdbRatingImporter>();
            var jobs = Substitute.For<IBackgroundJobClient>();
            jobs.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns("hf-1");
            var job = new ImdbRatingsJob(source, importer, runs, jobs, NullLogger<ImdbRatingsJob>.Instance);
            return new Harness(connection)
            {
                Db = db,
                Source = source,
                Importer = importer,
                Jobs = jobs,
                Job = job,
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }
}
