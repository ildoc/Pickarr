using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAudienceScorePrior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same vote-weighted mean as AudienceScore.Blend.
            migrationBuilder.Sql(
                """
                UPDATE "Titles" SET "VoteBlended" = CASE
                    WHEN ("VoteImdb" IS NOT NULL AND "VoteImdbCount" > 0)
                      OR ("VoteTmdb" IS NOT NULL AND "VoteTmdbCount" > 0)
                    THEN (
                        CASE WHEN "VoteImdb" IS NOT NULL AND "VoteImdbCount" > 0 THEN "VoteImdb" * "VoteImdbCount" ELSE 0 END
                      + CASE WHEN "VoteTmdb" IS NOT NULL AND "VoteTmdbCount" > 0 THEN "VoteTmdb" * "VoteTmdbCount" ELSE 0 END
                    ) * 1.0 / (
                        CASE WHEN "VoteImdb" IS NOT NULL AND "VoteImdbCount" > 0 THEN "VoteImdbCount" ELSE 0 END
                      + CASE WHEN "VoteTmdb" IS NOT NULL AND "VoteTmdbCount" > 0 THEN "VoteTmdbCount" ELSE 0 END)
                    ELSE NULL
                END;
                """);

            migrationBuilder.DropColumn(
                name: "PriorMean",
                table: "ImdbRatingSnapshot");

            migrationBuilder.DropColumn(
                name: "TmdbOffset",
                table: "ImdbRatingSnapshot");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
        }
    }
}
