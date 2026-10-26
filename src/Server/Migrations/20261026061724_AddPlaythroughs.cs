using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveLocker.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaythroughs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leases_GameId",
                table: "Leases");

            migrationBuilder.AddColumn<Guid>(
                name: "PlaythroughId",
                table: "SaveVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "PlaythroughId",
                table: "Leases",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "PlaythroughId",
                table: "Conflicts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "PlaythroughId",
                table: "AgentCommands",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Playthroughs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ForkedFromVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    HeadVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playthroughs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Playthroughs_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceSelections",
                columns: table => new
                {
                    MachineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaythroughId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SelectedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSelections", x => new { x.MachineId, x.GameId });
                    table.ForeignKey(
                        name: "FK_DeviceSelections_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceSelections_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceSelections_Playthroughs_PlaythroughId",
                        column: x => x.PlaythroughId,
                        principalTable: "Playthroughs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Every existing game becomes one playthrough, "Main" — the default — holding all of its versions, conflicts
            // and lease, and Game.HeadVersionId stays as the mirror of its head. Its id IS the game's id: unique, already in
            // the database's own Guid format, and it makes this plain SQL that can run again without minting anything.
            // Only games created by this build get a random playthrough id.
            migrationBuilder.Sql("""
                INSERT INTO "Playthroughs" ("Id", "GameId", "Name", "CreatedAt", "ForkedFromVersionId", "HeadVersionId", "ArchivedAt", "IsDefault")
                SELECT g."Id", g."Id", 'Main',
                       COALESCE((SELECT MIN(v."CreatedAt") FROM "SaveVersions" v WHERE v."GameId" = g."Id"),
                                strftime('%Y-%m-%d %H:%M:%f', 'now')),
                       NULL, g."HeadVersionId", NULL, 1
                FROM "Games" g
                """);
            migrationBuilder.Sql("""UPDATE "SaveVersions" SET "PlaythroughId" = "GameId" """);
            migrationBuilder.Sql("""UPDATE "Leases" SET "PlaythroughId" = "GameId" """);
            migrationBuilder.Sql("""UPDATE "Conflicts" SET "PlaythroughId" = "GameId" """);
            // Every device that already syncs a game is on Main, written down now: the default can change later, and a
            // device with no row would follow the new one out from under its own files.
            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO "DeviceSelections" ("MachineId", "GameId", "PlaythroughId", "SelectedAt")
                SELECT s."MachineId", s."GameId", s."GameId", strftime('%Y-%m-%d %H:%M:%f', 'now')
                FROM (SELECT "MachineId", "GameId" FROM "MachineSavePaths"
                      UNION
                      SELECT "MachineId", "GameId" FROM "SaveVersions" WHERE "MachineId" IS NOT NULL) s
                JOIN "Machines" m ON m."Id" = s."MachineId"
                JOIN "Games" g ON g."Id" = s."GameId"
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SaveVersions_PlaythroughId_CreatedAt",
                table: "SaveVersions",
                columns: new[] { "PlaythroughId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_GameId_PlaythroughId",
                table: "Leases",
                columns: new[] { "GameId", "PlaythroughId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conflicts_PlaythroughId_Status",
                table: "Conflicts",
                columns: new[] { "PlaythroughId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSelections_GameId",
                table: "DeviceSelections",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSelections_PlaythroughId",
                table: "DeviceSelections",
                column: "PlaythroughId");

            migrationBuilder.CreateIndex(
                name: "IX_Playthroughs_GameId_Default",
                table: "Playthroughs",
                column: "GameId",
                unique: true,
                filter: "\"IsDefault\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Playthroughs_GameId_Name",
                table: "Playthroughs",
                columns: new[] { "GameId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceSelections");

            migrationBuilder.DropTable(
                name: "Playthroughs");

            migrationBuilder.DropIndex(
                name: "IX_SaveVersions_PlaythroughId_CreatedAt",
                table: "SaveVersions");

            migrationBuilder.DropIndex(
                name: "IX_Leases_GameId_PlaythroughId",
                table: "Leases");

            migrationBuilder.DropIndex(
                name: "IX_Conflicts_PlaythroughId_Status",
                table: "Conflicts");

            migrationBuilder.DropColumn(
                name: "PlaythroughId",
                table: "SaveVersions");

            migrationBuilder.DropColumn(
                name: "PlaythroughId",
                table: "Leases");

            migrationBuilder.DropColumn(
                name: "PlaythroughId",
                table: "Conflicts");

            migrationBuilder.DropColumn(
                name: "PlaythroughId",
                table: "AgentCommands");

            migrationBuilder.CreateIndex(
                name: "IX_Leases_GameId",
                table: "Leases",
                column: "GameId",
                unique: true);
        }
    }
}
