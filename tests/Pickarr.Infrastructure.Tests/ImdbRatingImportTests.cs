using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Metadata;
using Pickarr.Infrastructure.Metadata;
using Pickarr.Infrastructure.Persistence;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class ImdbRatingImportTests
{
    private static readonly DateTime ImportedAt = new(2026, 11, 29, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Import_replaces_the_dump_and_realigns_titles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        await using var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ImdbTitleRatings.Add(new ImdbTitleRating
        {
            Tconst = "tt9999999",
            AverageRating = 1,
            NumVotes = 10,
        });
        db.ImdbRatingSnapshots.Add(new ImdbRatingSnapshot
        {
            Id = ImdbRatingSnapshot.SingletonId,
            MeanVote = 1,
            PriorMean = 1,
            RowCount = 1,
            ImportedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SourceUrl = "https://example.test/old.gz",
        });
        db.Titles.AddRange(
            Title(1, "tt0000001", voteTmdb: 6, voteTmdbCount: 500, voteImdb: 1, voteImdbCount: 1, voteBlended: 1),
            Title(2, "tt9999999", voteTmdb: 7.4, voteTmdbCount: 80, voteImdb: 9, voteImdbCount: 9, voteBlended: 9),
            Title(3, null, voteTmdb: 7.4, voteTmdbCount: 80),
            Title(4, null, voteBlended: 3));
        await db.SaveChangesAsync();

        await using var gzip = File.OpenRead(FixturePath());
        var result = await new ImdbRatingImporter(db, new FixedClock(ImportedAt)).ImportAsync(gzip);

        result.MeanVote.ShouldBe(7);
        // Only title 1 is in the new dump: C is its IMDb rating, too few pairs for an offset.
        result.Prior.ShouldBe(new AudiencePrior(8, 0));
        result.PreviousPrior.ShouldBe(new AudiencePrior(1, 0));
        result.RowCount.ShouldBe(2);
        result.TitlesUpdated.ShouldBe(3);

        var rows = await db.ImdbTitleRatings.OrderBy(row => row.Tconst).ToListAsync();
        rows.Select(row => row.Tconst).ShouldBe(["tt0000001", "tt0000002"]);
        var snapshot = await db.ImdbRatingSnapshots.SingleAsync();
        snapshot.MeanVote.ShouldBe(7);
        snapshot.PriorMean.ShouldBe(8);
        snapshot.TmdbOffset.ShouldBe(0);
        snapshot.RowCount.ShouldBe(2);
        snapshot.ImportedAt.ShouldBe(ImportedAt);
        snapshot.SourceUrl.ShouldBe(HttpImdbRatingsSource.DatasetUrl);

        var matched = await db.Titles.SingleAsync(title => title.Id == 1);
        matched.VoteImdb.ShouldBe(8);
        matched.VoteImdbCount.ShouldBe(500);
        matched.VoteBlended!.Value.ShouldBe((8.0 * 500 + 6.0 * 500 + 8.0 * 500) / 1500, 1e-12);
        matched.VoteTmdb.ShouldBe(6);
        matched.VoteTmdbCount.ShouldBe(500);

        var tmdbScore = (7.4 * 80 + 8.0 * 500) / 580;
        var removed = await db.Titles.SingleAsync(title => title.Id == 2);
        removed.VoteImdb.ShouldBeNull();
        removed.VoteImdbCount.ShouldBeNull();
        removed.VoteBlended!.Value.ShouldBe(tmdbScore, 1e-12);
        removed.VoteTmdb.ShouldBe(7.4);
        removed.VoteTmdbCount.ShouldBe(80);

        var tmdbOnly = await db.Titles.SingleAsync(title => title.Id == 3);
        tmdbOnly.VoteImdb.ShouldBeNull();
        tmdbOnly.VoteBlended!.Value.ShouldBe(tmdbScore, 1e-12);
        tmdbOnly.VoteTmdb.ShouldBe(7.4);

        var untouched = await db.Titles.SingleAsync(title => title.Id == 4);
        untouched.VoteBlended.ShouldBe(3);
    }

    [Fact]
    public async Task Second_import_drops_tconsts_missing_from_the_new_file()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        await using var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var importer = new ImdbRatingImporter(db, new FixedClock(ImportedAt));
        await using (var first = File.OpenRead(FixturePath()))
        {
            await importer.ImportAsync(first);
        }

        db.Titles.Add(Title(1, "tt0000001", voteTmdb: 6, voteTmdbCount: 500));
        await db.SaveChangesAsync();
        await using var second = Gzip(
            "tconst\taverageRating\tnumVotes\ntt0000002\t5.0\t100\n");
        var result = await importer.ImportAsync(second);

        // No catalog title is in either dump: C falls back to the dump mean.
        result.PreviousPrior.ShouldBe(new AudiencePrior(7, 0));
        result.Prior.ShouldBe(new AudiencePrior(5, 0));
        result.MeanVote.ShouldBe(5);
        (await db.ImdbTitleRatings.SingleAsync()).Tconst.ShouldBe("tt0000002");
        var title = await db.Titles.SingleAsync();
        title.VoteImdb.ShouldBeNull();
        title.VoteImdbCount.ShouldBeNull();
        title.VoteBlended.ShouldBe(5.5);
        title.VoteTmdb.ShouldBe(6);
        title.VoteTmdbCount.ShouldBe(500);
    }

    [Fact]
    public async Task Import_fits_the_catalog_mean_and_tmdb_offset()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        await using var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var dump = new StringBuilder("tconst\taverageRating\tnumVotes\n");
        for (var id = 1; id <= AudienceScore.CalibrationMinimumPairs; id++)
        {
            var tconst = $"tt{id:D7}";
            dump.Append(CultureInfo.InvariantCulture, $"{tconst}\t8.0\t500\n");
            db.Titles.Add(Title(id, tconst, voteTmdb: 7.6, voteTmdbCount: 100));
        }

        dump.Append("tt9000000\t2.0\t1000000\n");
        db.Titles.Add(Title(100, null, voteTmdb: 7.6, voteTmdbCount: 500));
        await db.SaveChangesAsync();

        await using var gzip = Gzip(dump.ToString());
        var result = await new ImdbRatingImporter(db, new FixedClock(ImportedAt)).ImportAsync(gzip);

        result.Prior.Mean.ShouldBe(8.0, 1e-12);
        result.Prior.TmdbOffset.ShouldBe(0.4, 1e-12);
        var snapshot = await db.ImdbRatingSnapshots.SingleAsync();
        snapshot.PriorMean.ShouldBe(8.0, 1e-12);
        snapshot.TmdbOffset.ShouldBe(0.4, 1e-12);

        var tmdbOnly = await db.Titles.SingleAsync(title => title.Id == 100);
        tmdbOnly.VoteBlended!.Value.ShouldBe(8.0, 1e-12);
    }

    [Theory]
    [InlineData("hello\n")]
    [InlineData("tconst\taverageRating\tnumVotes\n")]
    [InlineData("tconst\taverageRating\tnumVotes\nbogus\n")]
    [InlineData("tconst\taverageRating\tnumVotes\ntt0000001\t8.0\t0\n")]
    public async Task Rejected_file_leaves_the_database_unchanged(string text)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        await using var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await SeedPreviousAsync(db);

        await using var gzip = Gzip(text);
        await Should.ThrowAsync<ImdbRatingImportException>(() =>
            new ImdbRatingImporter(db, new FixedClock(ImportedAt)).ImportAsync(gzip));

        await AssertPreviousUnchangedAsync(db);
    }

    [Fact]
    public async Task Invalid_gzip_leaves_the_database_unchanged()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        await using var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await SeedPreviousAsync(db);

        await using var garbage = new MemoryStream([1, 2, 3, 4]);
        await Should.ThrowAsync<ImdbRatingImportException>(() =>
            new ImdbRatingImporter(db, new FixedClock(ImportedAt)).ImportAsync(garbage));

        await AssertPreviousUnchangedAsync(db);
    }

    [Fact]
    public async Task Malformed_rows_are_skipped_and_duplicate_tconst_keeps_the_last_row()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        await using var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();

        await using var gzip = Gzip(
            "tconst\taverageRating\tnumVotes\ntt0000001\t1.0\t10\nnot a row\ntt0000001\t9.0\t10\n");
        var result = await new ImdbRatingImporter(db, new FixedClock(ImportedAt)).ImportAsync(gzip);

        result.RowCount.ShouldBe(1);
        result.MeanVote.ShouldBe(9);
        var row = await db.ImdbTitleRatings.SingleAsync();
        row.Tconst.ShouldBe("tt0000001");
        row.AverageRating.ShouldBe(9);
        row.NumVotes.ShouldBe(10);
    }

    [Fact]
    public async Task Download_requests_the_public_dataset_url()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(GzipBytes("tconst\taverageRating\tnumVotes\ntt0000001\t8.0\t10\n")),
            };
        });
        using var client = new HttpClient(handler);
        await using var stream = await new HttpImdbRatingsSource(client).OpenAsync();

        captured.ShouldNotBeNull();
        captured!.RequestUri.ShouldBe(new Uri(HttpImdbRatingsSource.DatasetUrl));
        stream.Length.ShouldBeGreaterThan(0);
    }

    private static async Task SeedPreviousAsync(PickarrDbContext db)
    {
        db.ImdbTitleRatings.Add(new ImdbTitleRating
        {
            Tconst = "tt9999999",
            AverageRating = 1,
            NumVotes = 10,
        });
        db.ImdbRatingSnapshots.Add(new ImdbRatingSnapshot
        {
            Id = ImdbRatingSnapshot.SingletonId,
            MeanVote = 1,
            RowCount = 1,
            ImportedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SourceUrl = "https://example.test/old.gz",
        });
        db.Titles.Add(Title(1, "tt9999999", voteTmdb: 5, voteTmdbCount: 10, voteImdb: 1, voteImdbCount: 10, voteBlended: 1));
        await db.SaveChangesAsync();
    }

    private static async Task AssertPreviousUnchangedAsync(PickarrDbContext db)
    {
        var row = await db.ImdbTitleRatings.SingleAsync();
        row.Tconst.ShouldBe("tt9999999");
        var snapshot = await db.ImdbRatingSnapshots.SingleAsync();
        snapshot.MeanVote.ShouldBe(1);
        snapshot.SourceUrl.ShouldBe("https://example.test/old.gz");
        var title = await db.Titles.SingleAsync();
        title.VoteImdb.ShouldBe(1);
        title.VoteBlended.ShouldBe(1);
        title.VoteTmdb.ShouldBe(5);
    }

    private static Title Title(
        int id,
        string? imdbId,
        double? voteTmdb = null,
        int? voteTmdbCount = null,
        double? voteImdb = null,
        int? voteImdbCount = null,
        double? voteBlended = null) => new()
    {
        Id = id,
        ImdbId = imdbId,
        MediaType = MediaTypes.Movie,
        Category = TitleCategories.Movie,
        MetadataStatus = MetadataStatuses.Ready,
        VoteTmdb = voteTmdb,
        VoteTmdbCount = voteTmdbCount,
        VoteImdb = voteImdb,
        VoteImdbCount = voteImdbCount,
        VoteBlended = voteBlended,
        CreatedAt = ImportedAt,
        UpdatedAt = ImportedAt,
    };

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "imdb", "title.ratings.tsv.gz");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate tests/fixtures/imdb/title.ratings.tsv.gz");
    }

    private static MemoryStream Gzip(string text)
    {
        var stream = new MemoryStream(GzipBytes(text));
        return stream;
    }

    private static byte[] GzipBytes(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(text);
        }

        return output.ToArray();
    }

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> next) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(next(request));
    }
}
