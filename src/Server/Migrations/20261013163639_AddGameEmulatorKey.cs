using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveLocker.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGameEmulatorKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmulatorKey",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_EmulatorKey",
                table: "Games",
                column: "EmulatorKey",
                unique: true,
                filter: "\"EmulatorKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Games_EmulatorKey",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "EmulatorKey",
                table: "Games");
        }
    }
}
