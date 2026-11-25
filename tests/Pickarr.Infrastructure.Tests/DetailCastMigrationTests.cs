using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Domain.Entities;
using Pickarr.Infrastructure.Persistence;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class DetailCastMigrationTests
{
    private const string SchemaBeforeDetailCast = "20261110105718_AddTitleTmdbRecommendations";

    [Fact]
    public async Task Arr_slug_migration_preserves_existing_library_rows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        await using var db = new PickarrDbContext(options);
        await db.Database.MigrateAsync("20261125082232_AddCastFetchAttemptAt");
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ArrLibraryItems" ("Kind", "ArrId", "ArrTitle", "Monitored", "State", "SizeOnDisk", "SyncedAt")
            VALUES ('Radarr', 10, 'Existing movie', 1, 'Downloaded', 123, '2026-11-25');
            """);

        await db.Database.MigrateAsync();
        var item = await db.ArrLibraryItems.SingleAsync();
        item.ArrTitle.ShouldBe("Existing movie");
        item.SizeOnDisk.ShouldBe(123);
        item.TitleSlug.ShouldBeNull();
    }

    [Fact]
    public async Task Migration_preserves_existing_credits_and_supports_shared_ordered_cast()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        var fetchedAt = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);

        await using (var db = new PickarrDbContext(options))
        {
            await db.Database.MigrateAsync(SchemaBeforeDetailCast);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Titles" ("Id", "MediaType", "Category", "MetadataStatus", "MetadataAttempts", "CreatedAt", "UpdatedAt", "CastJson")
                VALUES (1, 'movie', 'movie', 'Ready', 0, {0}, {0}, '["Legacy"]'),
                       (2, 'series', 'series', 'Ready', 0, {0}, {0}, NULL);
                """,
                fetchedAt);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "People" ("Id", "Name") VALUES (100, 'Legacy');
                INSERT INTO "TitleCredits" ("TitleId", "PersonId", "Role", "Order")
                VALUES (1, 100, 'Cast', 0);
                """);
        }

        await using (var db = new PickarrDbContext(options))
        {
            await db.Database.MigrateAsync();
            var oldTitle = await db.Titles.SingleAsync(t => t.Id == 1);
            oldTitle.CastJson.ShouldBe("[\"Legacy\"]");
            oldTitle.CastFetchedAt.ShouldBeNull();
            (await db.TitleCredits.SingleAsync()).PersonId.ShouldBe(100);
            (await db.TitleCastMembers.CountAsync()).ShouldBe(0);

            oldTitle.CastFetchedAt = fetchedAt;
            var emptyTitle = await db.Titles.SingleAsync(t => t.Id == 2);
            emptyTitle.CastFetchedAt = fetchedAt;
            for (var position = 0; position < 12; position++)
            {
                var person = new CastPerson
                {
                    Id = 200 + position,
                    Name = $"Actor {position}",
                    ProfilePath = position == 0 ? "/actor.jpg" : null,
                    ImdbId = position == 0 ? "nm1234567" : null,
                    ImdbFetchedAt = fetchedAt,
                };
                db.CastPeople.Add(person);
                db.TitleCastMembers.Add(new TitleCastMember
                {
                    TitleId = oldTitle.Id,
                    Person = person,
                    Position = position,
                });
            }

            await db.SaveChangesAsync();
        }

        await using (var db = new PickarrDbContext(options))
        {
            var cast = await db.TitleCastMembers
                .Where(member => member.TitleId == 1)
                .Include(member => member.Person)
                .OrderBy(member => member.Position)
                .ToListAsync();
            cast.Count.ShouldBe(12);
            cast.Select(member => member.Position).ShouldBe(Enumerable.Range(0, 12));
            cast[0].Person!.ImdbId.ShouldBe("nm1234567");
            cast[0].Person!.ProfilePath.ShouldBe("/actor.jpg");
            cast[1].Person!.ImdbId.ShouldBeNull();
            cast[1].Person!.ImdbFetchedAt.ShouldNotBeNull();
            var emptyTitle = await db.Titles.SingleAsync(t => t.Id == 2);
            emptyTitle.CastFetchedAt.ShouldNotBeNull();
            (await db.TitleCastMembers.CountAsync(member => member.TitleId == 2)).ShouldBe(0);

            db.TitleCastMembers.Add(new TitleCastMember { TitleId = 2, PersonId = 200, Position = 0 });
            await db.SaveChangesAsync();
            (await db.CastPeople.CountAsync()).ShouldBe(12);
            (await db.TitleCredits.SingleAsync()).PersonId.ShouldBe(100);
            await Should.ThrowAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "TitleCastMembers" ("TitleId", "PersonId", "Position") VALUES (2, 200, 1);"""));
        }
    }

    [Fact]
    public async Task Cast_positions_must_be_unique_per_title()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        await using var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Titles.Add(new Title
        {
            Id = 1,
            MediaType = "movie",
            Category = "movie",
            MetadataStatus = "Ready",
        });
        db.CastPeople.AddRange(
            new CastPerson { Id = 1, Name = "First" },
            new CastPerson { Id = 2, Name = "Second" });
        db.TitleCastMembers.AddRange(
            new TitleCastMember { TitleId = 1, PersonId = 1, Position = 0 },
            new TitleCastMember { TitleId = 1, PersonId = 2, Position = 0 });

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
