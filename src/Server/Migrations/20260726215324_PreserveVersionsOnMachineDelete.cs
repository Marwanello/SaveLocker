using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveLocker.Server.Migrations
{
    /// <inheritdoc />
    public partial class PreserveVersionsOnMachineDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SaveVersions_Machines_MachineId",
                table: "SaveVersions");

            migrationBuilder.AlterColumn<Guid>(
                name: "MachineId",
                table: "SaveVersions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "MachineName",
                table: "SaveVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "FK_SaveVersions_Machines_MachineId",
                table: "SaveVersions",
                column: "MachineId",
                principalTable: "Machines",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Existing history has no snapshot yet. Take it from the machine while the link is
            // still there — after the first machine deletion there is nothing left to read it from.
            migrationBuilder.Sql(@"
                UPDATE SaveVersions
                   SET MachineName = COALESCE(
                        (SELECT Name FROM Machines WHERE Machines.Id = SaveVersions.MachineId), '')
                 WHERE MachineName = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SaveVersions_Machines_MachineId",
                table: "SaveVersions");

            migrationBuilder.DropColumn(
                name: "MachineName",
                table: "SaveVersions");

            // Up() (and the code it ships) can leave rows with MachineId = NULL — that is the whole
            // point of this migration. SQLite's ALTER COLUMN is a table-rebuild that copies existing
            // rows via INSERT...SELECT, which does not apply the column's new DEFAULT to a NULL that
            // is already there — only to rows inserted afterward — so without this backfill the
            // rebuild would violate the NOT NULL constraint it is about to add and the downgrade
            // would throw instead of reversing.
            migrationBuilder.Sql(@"
                UPDATE SaveVersions
                   SET MachineId = '00000000-0000-0000-0000-000000000000'
                 WHERE MachineId IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "MachineId",
                table: "SaveVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SaveVersions_Machines_MachineId",
                table: "SaveVersions",
                column: "MachineId",
                principalTable: "Machines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
