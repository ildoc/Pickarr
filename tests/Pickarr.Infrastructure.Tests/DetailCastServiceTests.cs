using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Metadata;
using Pickarr.Infrastructure.Metadata;
using Pickarr.Infrastructure.Persistence;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class DetailCastServiceTests
{
    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class ScriptedProvider : IMetadataProvider
    {
        public TmdbTitleDetails? Details { get; init; }
        public bool ThrowTemporary { get; init; }
        public bool ThrowNotFound { get; init; }
        public TmdbPersonExternalIds? ExternalIds { get; init; }

        public string Type => "tmdb";
        public Task<TmdbTitleDetails?> GetMovieAsync(int tmdbId, string language, CancellationToken cancellationToken = default) =>
            ThrowNotFound
                ? Task.FromException<TmdbTitleDetails?>(new TmdbResourceNotFoundException($"movie/{tmdbId}"))
                : Task.FromResult(Details);
        public Task<TmdbTitleDetails?> GetTvAsync(int tmdbId, string language, CancellationToken cancellationToken = default) =>
            ThrowNotFound
                ? Task.FromException<TmdbTitleDetails?>(new TmdbResourceNotFoundException($"tv/{tmdbId}"))
                : Task.FromResult(Details);
        public Task<IReadOnlyList<TmdbEpisode>> GetTvSeasonAsync(int tmdbId, int seasonNumber, string language, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbEpisode>>([]);
        public Task<IReadOnlyList<TmdbRecommendationRef>> GetRecommendationsAsync(int tmdbId, bool isMovie, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbRecommendationRef>>([]);
        public Task<TmdbFindResult?> FindByImdbAsync(string imdbId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbFindResult?>(null);
        public Task<TmdbPersonExternalIds?> GetPersonExternalIdsAsync(int tmdbPersonId, CancellationToken cancellationToken = default) =>
            ThrowTemporary
                ? Task.FromException<TmdbPersonExternalIds?>(new HttpRequestException("TMDB request failed (503)."))
                : Task.FromResult(ExternalIds);
        public Task<IReadOnlyList<TmdbGenre>> GetMovieGenresAsync(string language, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbGenre>>([]);
        public Task<IReadOnlyList<TmdbGenre>> GetTvGenresAsync(string language, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbGenre>>([]);
        public Task<TmdbImageConfig> GetImageConfigAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TmdbImageConfig("https://image.tmdb.org/t/p/", DateTime.UtcNow));
    }

    [Fact]
    public async Task Processes_backfill_up_to_12_and_shares_people_without_touching_reco_credits()
    {
        await using var db = await CreateDbAsync();
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var titles = new[]
        {
            CreateTitle(1, 550, "movie"),
            CreateTitle(2, 551, "movie"),
        };
        db.Titles.AddRange(titles);
        db.People.Add(new Person { Id = 100, Name = "Reco Legacy" });
        db.TitleCredits.Add(new TitleCredit
        {
            TitleId = 1,
            PersonId = 100,
            Role = "Cast",
            Order = 0,
        });
        await db.SaveChangesAsync();

        var detailCast = Enumerable.Range(0, 13)
            .Select(i => new TmdbDetailCastMember(200 + i, $"Actor {i}", i == 0 ? "/a.jpg" : null, i))
            .ToArray();
        var details = new TmdbTitleDetails(
            550, "Movie", null, null, null, 2026, null, null, null, null, null, null, null,
            null, null, null, null, [], [], true, null, null, null, null,
            detailCast);
        var service = new DetailCastService(
            db,
            new ScriptedProvider { Details = details, ExternalIds = new TmdbPersonExternalIds("nm1234567") },
            new FixedClock(now),
            NullLogger<DetailCastService>.Instance);

        var result = await service.ProcessBatchAsync(limit: 2);

        result.Processed.ShouldBe(2);
        result.Updated.ShouldBe(2);
        result.Failed.ShouldBe(0);
        var members = await db.TitleCastMembers
            .Include(member => member.Person)
            .OrderBy(member => member.TitleId)
            .ThenBy(member => member.Position)
            .ToListAsync();
        members.Count(member => member.TitleId == 1).ShouldBe(12);
        members.First(member => member.TitleId == 1).Person!.ImdbId.ShouldBe("nm1234567");
        members.Count(member => member.PersonId >= 200 && member.PersonId <= 212).ShouldBeGreaterThan(12);
        (await db.TitleCredits.SingleAsync()).PersonId.ShouldBe(100);
        (await db.Titles.Select(t => t.CastFetchedAt).ToListAsync()).ShouldAllBe(value => value == now);
    }

    [Fact]
    public async Task Empty_cast_is_stamped_and_does_not_resurrect_old_members()
    {
        await using var db = await CreateDbAsync();
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var title = CreateTitle(1, 550, "movie");
        title.CastMembers.Add(new TitleCastMember
        {
            TitleId = 1,
            PersonId = 999,
            Position = 0,
            Person = new CastPerson { Id = 999, Name = "Stale" },
        });
        db.Titles.Add(title);
        await db.SaveChangesAsync();

        var details = new TmdbTitleDetails(
            550, "Movie", null, null, null, 2026, null, null, null, null, null, null, null,
            null, null, null, null, [], [], true, null, null, null, null, []);
        var service = new DetailCastService(
            db,
            new ScriptedProvider { Details = details },
            new FixedClock(now),
            NullLogger<DetailCastService>.Instance);

        var result = await service.ProcessBatchAsync();

        result.Empty.ShouldBe(1);
        var current = await db.Titles.Include(t => t.CastMembers).SingleAsync();
        current.CastFetchedAt.ShouldBe(now);
        current.CastMembers.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Temporary_error_preserves_stamp_and_members_for_retry()
    {
        await using var db = await CreateDbAsync();
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var title = CreateTitle(1, 550, "movie");
        var person = new CastPerson { Id = 200, Name = "Existing" };
        title.CastMembers.Add(new TitleCastMember
        {
            TitleId = 1,
            PersonId = 200,
            Position = 0,
            Person = person,
        });
        db.Titles.Add(title);
        await db.SaveChangesAsync();

        var details = new TmdbTitleDetails(
            550, "Movie", null, null, null, 2026, null, null, null, null, null, null, null,
            null, null, null, null, [], [], true, null, null, null, null,
            [new TmdbDetailCastMember(200, "Existing", "/existing.jpg", 0)]);
        var service = new DetailCastService(
            db,
            new ScriptedProvider { Details = details, ThrowTemporary = true },
            new FixedClock(now),
            NullLogger<DetailCastService>.Instance);

        var result = await service.ProcessBatchAsync();

        result.Failed.ShouldBe(1);
        result.LastError.ShouldNotBeNull();
        result.LastError!.ShouldContain("503");
        var current = await db.Titles.Include(t => t.CastMembers).ThenInclude(m => m.Person).SingleAsync();
        current.CastFetchedAt.ShouldBeNull();
        current.CastMembers.Count.ShouldBe(1);
        current.CastMembers.Single().Person!.Name.ShouldBe("Existing");
    }

    [Fact]
    public async Task Not_found_preserves_existing_cast_and_sets_retry_attempt()
    {
        await using var db = await CreateDbAsync();
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var title = CreateTitle(1, 550, "movie");
        title.CastMembers.Add(new TitleCastMember
        {
            TitleId = 1,
            PersonId = 200,
            Position = 0,
            Person = new CastPerson { Id = 200, Name = "Existing" },
        });
        db.Titles.Add(title);
        await db.SaveChangesAsync();

        var service = new DetailCastService(
            db,
            new ScriptedProvider { ThrowNotFound = true },
            new FixedClock(now),
            NullLogger<DetailCastService>.Instance);

        var result = await service.ProcessBatchAsync();

        result.Failed.ShouldBe(1);
        var current = await db.Titles.Include(t => t.CastMembers).ThenInclude(m => m.Person).SingleAsync();
        current.CastFetchedAt.ShouldBeNull();
        current.CastFetchAttemptAt.ShouldBe(now);
        current.CastMembers.Count.ShouldBe(1);
        current.CastMembers.Single().Person!.Name.ShouldBe("Existing");
    }

    [Fact]
    public async Task Retry_attempt_timestamp_allows_other_titles_to_progress()
    {
        await using var db = await CreateDbAsync();
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var recentlyRetried = CreateTitle(1, 550, "movie");
        recentlyRetried.CastFetchAttemptAt = now;
        db.Titles.AddRange(
            recentlyRetried,
            CreateTitle(2, 551, "movie"));
        await db.SaveChangesAsync();

        var details = new TmdbTitleDetails(
            551, "Movie", null, null, null, 2026, null, null, null, null, null, null, null,
            null, null, null, null, [], [], true, null, null, null, null, []);
        var service = new DetailCastService(
            db,
            new ScriptedProvider { Details = details },
            new FixedClock(now),
            NullLogger<DetailCastService>.Instance);

        var result = await service.ProcessBatchAsync(limit: 50);

        result.Processed.ShouldBe(1);
        var retried = await db.Titles.FindAsync(1);
        var processed = await db.Titles.FindAsync(2);
        retried!.CastFetchedAt.ShouldBeNull();
        processed!.CastFetchedAt.ShouldBe(now);
    }

    [Fact]
    public async Task Retry_attempts_are_ordered_oldest_first_to_prevent_starvation()
    {
        await using var db = await CreateDbAsync();
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var neverAttempted = CreateTitle(1, 550, "movie", now.AddDays(-31));
        var retried = CreateTitle(2, 551, "movie", now.AddDays(-31));
        retried.CastFetchAttemptAt = now.AddHours(-4);
        db.Titles.AddRange(neverAttempted, retried);
        await db.SaveChangesAsync();

        var details = new TmdbTitleDetails(
            551, "Movie", null, null, null, 2026, null, null, null, null, null, null, null,
            null, null, null, null, [], [], true, null, null, null, null, []);
        var service = new DetailCastService(
            db,
            new ScriptedProvider { Details = details },
            new FixedClock(now),
            NullLogger<DetailCastService>.Instance);

        var result = await service.ProcessBatchAsync(limit: 1);

        result.Processed.ShouldBe(1);
        var selected = await db.Titles.FindAsync(1);
        selected!.CastFetchedAt.ShouldBe(now);
        var retry = await db.Titles.FindAsync(2);
        retry!.CastFetchAttemptAt.ShouldBe(now.AddHours(-4));
    }

    [Fact]
    public async Task Retry_attempts_use_oldest_attempt_before_newer_attempt()
    {
        await using var db = await CreateDbAsync();
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var newer = CreateTitle(1, 550, "movie", now.AddDays(-31));
        newer.CastFetchAttemptAt = now.AddHours(-2);
        var older = CreateTitle(2, 551, "movie", now.AddDays(-31));
        older.CastFetchAttemptAt = now.AddHours(-4);
        db.Titles.AddRange(newer, older);
        await db.SaveChangesAsync();

        var details = new TmdbTitleDetails(
            551, "Movie", null, null, null, 2026, null, null, null, null, null, null, null,
            null, null, null, null, [], [], true, null, null, null, null, []);
        var service = new DetailCastService(
            db,
            new ScriptedProvider { Details = details },
            new FixedClock(now),
            NullLogger<DetailCastService>.Instance);

        var result = await service.ProcessBatchAsync(limit: 1);

        result.Processed.ShouldBe(1);
        var selected = await db.Titles.FindAsync(2);
        selected!.CastFetchedAt.ShouldBe(now);
    }

    [Fact]
    public async Task Missing_imdb_id_is_retried_after_30_days()
    {
        await using var db = await CreateDbAsync();
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var title = CreateTitle(1, 550, "movie");
        title.CastMembers.Add(new TitleCastMember
        {
            TitleId = 1,
            PersonId = 200,
            Position = 0,
            Person = new CastPerson
            {
                Id = 200,
                Name = "Existing",
                ImdbId = null,
                ImdbFetchedAt = now.AddDays(-31),
            },
        });
        db.Titles.Add(title);
        await db.SaveChangesAsync();

        var details = new TmdbTitleDetails(
            550, "Movie", null, null, null, 2026, null, null, null, null, null, null, null,
            null, null, null, null, [], [], true, null, null, null, null,
            [new TmdbDetailCastMember(200, "Existing", null, 0)]);
        var provider = new ScriptedProvider { Details = details, ExternalIds = new TmdbPersonExternalIds("nm7654321") };
        var service = new DetailCastService(
            db,
            provider,
            new FixedClock(now),
            NullLogger<DetailCastService>.Instance);

        await service.ProcessBatchAsync();

        var person = await db.CastPeople.SingleAsync();
        person.ImdbId.ShouldBe("nm7654321");
        person.ImdbFetchedAt.ShouldBe(now);
    }

    [Fact]
    public async Task Refreshes_only_cast_older_than_30_days()
    {
        await using var db = await CreateDbAsync();
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        db.Titles.AddRange(
            CreateTitle(1, 550, "movie", now.AddDays(-31)),
            CreateTitle(2, 551, "movie", now.AddDays(-10)));
        await db.SaveChangesAsync();

        var details = new TmdbTitleDetails(
            550, "Movie", null, null, null, 2026, null, null, null, null, null, null, null,
            null, null, null, null, [], [], true, null, null, null, null, []);
        var service = new DetailCastService(
            db,
            new ScriptedProvider { Details = details },
            new FixedClock(now),
            NullLogger<DetailCastService>.Instance);

        var result = await service.ProcessBatchAsync();

        result.Processed.ShouldBe(1);
        var stale = await db.Titles.FindAsync(1);
        var fresh = await db.Titles.FindAsync(2);
        stale!.CastFetchedAt.ShouldBe(now);
        fresh!.CastFetchedAt.ShouldBe(now.AddDays(-10));
    }

    private static Title CreateTitle(int id, int tmdbId, string tmdbType, DateTime? fetchedAt = null) =>
        new()
        {
            Id = id,
            TmdbId = tmdbId,
            TmdbType = tmdbType,
            MediaType = tmdbType == "movie" ? "movie" : "series",
            Category = tmdbType == "movie" ? "movie" : "series",
            MetadataStatus = "Ready",
            CastFetchedAt = fetchedAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    private static async Task<PickarrDbContext> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
