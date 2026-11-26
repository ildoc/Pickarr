using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudienceScorePrior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PriorMean",
                table: "ImdbRatingSnapshot",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TmdbOffset",
                table: "ImdbRatingSnapshot",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.Sql(
                """
                UPDATE "ImdbRatingSnapshot" SET "PriorMean" = "MeanVote";
                """);

            // Bayesian average with m = 500; RemoveAudienceScorePrior replaces it with the plain vote-weighted mean.
            migrationBuilder.Sql(
                """
                UPDATE "Titles" SET "VoteBlended" = CASE
                    WHEN ("VoteImdb" IS NOT NULL AND "VoteImdbCount" > 0)
                      OR ("VoteTmdb" IS NOT NULL AND "VoteTmdbCount" > 0)
                    THEN (
                        CASE WHEN "VoteImdb" IS NOT NULL AND "VoteImdbCount" > 0 THEN "VoteImdb" * "VoteImdbCount" ELSE 0 END
                      + CASE WHEN "VoteTmdb" IS NOT NULL AND "VoteTmdbCount" > 0 THEN "VoteTmdb" * "VoteTmdbCount" ELSE 0 END
                      + 500.0 * COALESCE(
                            (SELECT "PriorMean" FROM "ImdbRatingSnapshot" WHERE "Id" = 1),
                            (SELECT AVG("VoteTmdb") FROM "Titles" WHERE "VoteTmdb" IS NOT NULL AND "VoteTmdbCount" > 0),
                            0)
                    ) / (
                        CASE WHEN "VoteImdb" IS NOT NULL AND "VoteImdbCount" > 0 THEN "VoteImdbCount" ELSE 0 END
                      + CASE WHEN "VoteTmdb" IS NOT NULL AND "VoteTmdbCount" > 0 THEN "VoteTmdbCount" ELSE 0 END
                      + 500.0)
                    ELSE NULL
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriorMean",
                table: "ImdbRatingSnapshot");

            migrationBuilder.DropColumn(
                name: "TmdbOffset",
                table: "ImdbRatingSnapshot");
        }
    }
}
