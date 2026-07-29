using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveLocker.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandVisibilityLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClaimCount",
                table: "AgentCommands",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ClaimToken",
                table: "AgentCommands",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "AgentCommands",
                type: "TEXT",
                nullable: true);

            // Commands the old code already stranded in Dispatched have no lease, and the claim
            // predicate needs one to consider them due. Backdate it to their dispatch time so the
            // upgrade hands them back to the agent instead of leaving them lost forever.
            // Status 1 = Dispatched.
            migrationBuilder.Sql(@"
                UPDATE AgentCommands
                   SET LeaseExpiresAt = COALESCE(DispatchedAt, CreatedAt),
                       ClaimCount     = 1
                 WHERE Status = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimCount",
                table: "AgentCommands");

            migrationBuilder.DropColumn(
                name: "ClaimToken",
                table: "AgentCommands");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "AgentCommands");
        }
    }
}
