using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveLocker.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGameIncludeGlobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IncludeGlobs",
                table: "Games",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludeGlobs",
                table: "Games");
        }
    }
}
