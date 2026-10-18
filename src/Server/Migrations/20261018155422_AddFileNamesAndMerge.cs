using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveLocker.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFileNamesAndMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MergedFrom",
                table: "SaveVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileNames",
                table: "MachineSavePaths",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MergedGameIds",
                table: "Games",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MergedFrom",
                table: "SaveVersions");

            migrationBuilder.DropColumn(
                name: "FileNames",
                table: "MachineSavePaths");

            migrationBuilder.DropColumn(
                name: "MergedGameIds",
                table: "Games");
        }
    }
}
