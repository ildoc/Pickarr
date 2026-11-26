using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StandUpAsGenre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT OR IGNORE INTO "Genres" ("Id") VALUES (-1);
                """);

            migrationBuilder.Sql(
                """
                INSERT OR IGNORE INTO "GenreLocalizations" ("GenreId", "Language", "Name")
                SELECT -1, "Language", 'Stand-up' FROM (
                    SELECT "Language" FROM "GenreLocalizations"
                    UNION SELECT "MetadataLanguage" FROM "CatalogSources"
                    UNION SELECT 'en-US'
                )
                WHERE "Language" IS NOT NULL AND trim("Language") <> '';
                """);

            migrationBuilder.Sql(
                """
                INSERT OR IGNORE INTO "TitleGenres" ("TitleId", "GenreId")
                SELECT "Id", -1 FROM "Titles" WHERE "Category" = 'StandUp';
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Titles"
                SET "Category" = CASE WHEN "MediaType" = 'Movie' THEN 'Movie' ELSE 'Series' END
                WHERE "Category" = 'StandUp';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Titles" SET "Category" = 'StandUp'
                WHERE "Id" IN (SELECT "TitleId" FROM "TitleGenres" WHERE "GenreId" = -1);
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "TitleGenres" WHERE "GenreId" = -1;
                DELETE FROM "GenreLocalizations" WHERE "GenreId" = -1;
                DELETE FROM "Genres" WHERE "Id" = -1;
                """);
        }
    }
}
