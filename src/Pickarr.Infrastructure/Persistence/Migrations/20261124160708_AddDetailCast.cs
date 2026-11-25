using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailCast : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CastFetchedAt",
                table: "Titles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CastPeople",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ProfilePath = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ImdbId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ImdbFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CastPeople", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TitleCastMembers",
                columns: table => new
                {
                    TitleId = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleCastMembers", x => new { x.TitleId, x.PersonId });
                    table.ForeignKey(
                        name: "FK_TitleCastMembers_CastPeople_PersonId",
                        column: x => x.PersonId,
                        principalTable: "CastPeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TitleCastMembers_Titles_TitleId",
                        column: x => x.TitleId,
                        principalTable: "Titles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TitleCastMembers_PersonId",
                table: "TitleCastMembers",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_TitleCastMembers_TitleId_Position",
                table: "TitleCastMembers",
                columns: new[] { "TitleId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TitleCastMembers");

            migrationBuilder.DropTable(
                name: "CastPeople");

            migrationBuilder.DropColumn(
                name: "CastFetchedAt",
                table: "Titles");
        }
    }
}
