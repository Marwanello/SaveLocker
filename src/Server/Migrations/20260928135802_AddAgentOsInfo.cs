using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveLocker.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentOsInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OsDevice",
                table: "AgentHealth",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsId",
                table: "AgentHealth",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsIdLike",
                table: "AgentHealth",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsName",
                table: "AgentHealth",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsVariantId",
                table: "AgentHealth",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OsDevice",
                table: "AgentHealth");

            migrationBuilder.DropColumn(
                name: "OsId",
                table: "AgentHealth");

            migrationBuilder.DropColumn(
                name: "OsIdLike",
                table: "AgentHealth");

            migrationBuilder.DropColumn(
                name: "OsName",
                table: "AgentHealth");

            migrationBuilder.DropColumn(
                name: "OsVariantId",
                table: "AgentHealth");
        }
    }
}
