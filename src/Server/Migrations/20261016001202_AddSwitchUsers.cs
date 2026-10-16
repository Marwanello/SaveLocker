using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveLocker.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSwitchUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SwitchUsersAt",
                table: "AgentHealth",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwitchUsersJson",
                table: "AgentHealth",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SwitchUsersAt",
                table: "AgentHealth");

            migrationBuilder.DropColumn(
                name: "SwitchUsersJson",
                table: "AgentHealth");
        }
    }
}
