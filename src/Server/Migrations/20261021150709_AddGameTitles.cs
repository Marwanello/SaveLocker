using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveLocker.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGameTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TitleEdition",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TitleId",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TitleLocked",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GameTitles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlatformKey = table.Column<string>(type: "TEXT", nullable: false),
                    BaseName = table.Column<string>(type: "TEXT", nullable: false),
                    RegionKey = table.Column<string>(type: "TEXT", nullable: false),
                    LeadGameId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameTitles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Games_TitleId",
                table: "Games",
                column: "TitleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Games_GameTitles_TitleId",
                table: "Games",
                column: "TitleId",
                principalTable: "GameTitles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Games_GameTitles_TitleId",
                table: "Games");

            migrationBuilder.DropTable(
                name: "GameTitles");

            migrationBuilder.DropIndex(
                name: "IX_Games_TitleId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "TitleEdition",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "TitleId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "TitleLocked",
                table: "Games");
        }
    }
}
