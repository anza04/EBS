using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClubsAndRosters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Balance",
                table: "Clubs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "BoardExpectation",
                table: "Clubs",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "MidTable");

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Clubs",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            // Existing clubs take their home arena's city.
            migrationBuilder.Sql("UPDATE Clubs SET City = (SELECT a.City FROM Arenas a WHERE a.Id = Clubs.HomeArenaId) WHERE HomeArenaId IS NOT NULL;");

            migrationBuilder.AddColumn<int>(
                name: "Reputation",
                table: "Clubs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<long>(
                name: "WageBudget",
                table: "Clubs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "RosterSpots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LineupPosition = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    RotationOrder = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterSpots", x => x.Id);
                    table.CheckConstraint("CK_RosterSpots_LineupPosition", "(Role = 'Starter') = (LineupPosition IS NOT NULL)");
                    table.CheckConstraint("CK_RosterSpots_RotationOrder", "(Role = 'Rotation') = (RotationOrder IS NOT NULL) AND (RotationOrder IS NULL OR RotationOrder >= 1)");
                    table.ForeignKey(
                        name: "FK_RosterSpots_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RosterSpots_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RosterSpots_ClubId",
                table: "RosterSpots",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterSpots_PlayerId",
                table: "RosterSpots",
                column: "PlayerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RosterSpots");

            migrationBuilder.DropColumn(
                name: "Balance",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "BoardExpectation",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "Reputation",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "WageBudget",
                table: "Clubs");
        }
    }
}
