using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pickarr.Infrastructure.Persistence;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class ImdbRatingMigrationTests
{
    private const string SchemaBeforeImdbRatings = "20261125101540_AddArrTitleSlug";
    private const string ImdbRatingsMigration = "20261126171739_AddImdbRatings";
    private const string AudienceScorePriorMigration = "20261126192356_AddAudienceScorePrior";

    [Fact]
    public async Task Migration_backfills_blended_vote_from_tmdb_and_adds_imdb_tables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        var createdAt = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);

        await using (var db = new PickarrDbContext(options))
        {
            await db.Database.MigrateAsync(SchemaBeforeImdbRatings);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Titles" ("Id", "MediaType", "Category", "MetadataStatus", "MetadataAttempts", "CreatedAt", "UpdatedAt", "VoteTmdb")
                VALUES (1, 'movie', 'movie', 'Ready', 0, {0}, {0}, 8.5),
                       (2, 'movie', 'movie', 'Ready', 0, {0}, {0}, NULL);
                """,
                createdAt);
            await db.Database.MigrateAsync(ImdbRatingsMigration);

            var blended = await db.Database
                .SqlQueryRaw<double?>("""SELECT "VoteBlended" AS "Value" FROM "Titles" ORDER BY "Id" """)
                .ToListAsync();
            blended.ShouldBe([8.5, null]);
            (await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*) AS "Value" FROM "ImdbTitleRatings" """).SingleAsync())
                .ShouldBe(0);
            (await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*) AS "Value" FROM "ImdbRatingSnapshot" """).SingleAsync())
                .ShouldBe(0);
        }
    }

    [Fact]
    public async Task Removing_the_prior_restores_the_vote_weighted_title_rating()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        var createdAt = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);

        await using (var db = new PickarrDbContext(options))
        {
            await db.Database.MigrateAsync(ImdbRatingsMigration);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Titles" ("Id", "MediaType", "Category", "MetadataStatus", "MetadataAttempts", "CreatedAt", "UpdatedAt",
                                      "VoteTmdb", "VoteTmdbCount", "VoteImdb", "VoteImdbCount", "VoteBlended")
                VALUES (1, 'movie', 'movie', 'Ready', 0, {0}, {0}, 9.5, 3, NULL, NULL, 9.5),
                       (2, 'movie', 'movie', 'Ready', 0, {0}, {0}, 6.0, 500, 8.0, 500, 7.0),
                       (3, 'movie', 'movie', 'Ready', 0, {0}, {0}, 0, 0, NULL, NULL, 0);
                """,
                createdAt);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ImdbRatingSnapshot" ("Id", "MeanVote", "RowCount", "ImportedAt", "SourceUrl")
                VALUES (1, 7.0, 1, {0}, 'https://example.test/old.gz');
                """,
                createdAt);
            await db.Database.MigrateAsync(AudienceScorePriorMigration);

            var shrunk = await db.Database
                .SqlQueryRaw<double>("""SELECT "VoteBlended" AS "Value" FROM "Titles" WHERE "Id" = 1""")
                .SingleAsync();
            shrunk.ShouldBe((9.5 * 3 + 7.0 * 500) / 503, 1e-9);
        }

        await using (var db = new PickarrDbContext(options))
        {
            await db.Database.MigrateAsync();

            var snapshot = await db.ImdbRatingSnapshots.SingleAsync();
            snapshot.MeanVote.ShouldBe(7.0);

            var titles = await db.Titles.OrderBy(title => title.Id).ToListAsync();
            titles[0].VoteBlended!.Value.ShouldBe(9.5, 1e-9);
            titles[1].VoteBlended!.Value.ShouldBe(7.0, 1e-9);
            titles[2].VoteBlended.ShouldBeNull();
        }
    }
}
