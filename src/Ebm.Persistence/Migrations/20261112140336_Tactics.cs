using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tactics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tactics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    OffensiveSystem = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OffenseTempo = table.Column<int>(type: "INTEGER", nullable: false),
                    OffenseShotSelection = table.Column<int>(type: "INTEGER", nullable: false),
                    OffenseBallMovement = table.Column<int>(type: "INTEGER", nullable: false),
                    OffenseSpacing = table.Column<int>(type: "INTEGER", nullable: false),
                    OffensePickAndRollFrequency = table.Column<int>(type: "INTEGER", nullable: false),
                    OffenseFocus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DefensiveSystem = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DefenseIntensity = table.Column<int>(type: "INTEGER", nullable: false),
                    DefenseBallPressure = table.Column<int>(type: "INTEGER", nullable: false),
                    DefenseHelpDefense = table.Column<int>(type: "INTEGER", nullable: false),
                    DefenseSwitching = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DefensePickAndRollCoverage = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DefenseRebounding = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false),
                    Assignments = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tactics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tactics_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tactics_ClubId_Name",
                table: "Tactics",
                columns: new[] { "ClubId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tactics_SelectedPerClub",
                table: "Tactics",
                column: "ClubId",
                unique: true,
                filter: "IsSelected = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tactics");
        }
    }
}
