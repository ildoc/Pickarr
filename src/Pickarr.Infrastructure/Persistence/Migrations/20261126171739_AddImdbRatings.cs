using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImdbRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "VoteBlended",
                table: "Titles",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VoteImdb",
                table: "Titles",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoteImdbCount",
                table: "Titles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Titles" SET "VoteBlended" = "VoteTmdb" WHERE "VoteTmdb" IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "ImdbRatingSnapshot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    MeanVote = table.Column<double>(type: "REAL", nullable: false),
                    RowCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImdbRatingSnapshot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImdbTitleRatings",
                columns: table => new
                {
                    Tconst = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AverageRating = table.Column<double>(type: "REAL", nullable: false),
                    NumVotes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImdbTitleRatings", x => x.Tconst);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Titles_VoteBlended",
                table: "Titles",
                column: "VoteBlended");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImdbRatingSnapshot");

            migrationBuilder.DropTable(
                name: "ImdbTitleRatings");

            migrationBuilder.DropIndex(
                name: "IX_Titles_VoteBlended",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "VoteBlended",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "VoteImdb",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "VoteImdbCount",
                table: "Titles");
        }
    }
}
