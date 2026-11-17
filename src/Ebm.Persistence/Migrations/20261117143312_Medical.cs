using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Medical : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Injuries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecoveryDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Progress = table.Column<double>(type: "REAL", nullable: false),
                    EstimatedReturn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AssessedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IsRecurrence = table.Column<bool>(type: "INTEGER", nullable: false),
                    Aggravations = table.Column<int>(type: "INTEGER", nullable: false),
                    RecoveredOn = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Injuries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Injuries_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Injuries_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Injuries_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Injuries_ClubId",
                table: "Injuries",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Injuries_MatchId",
                table: "Injuries",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Injuries_PlayerId_OccurredOn",
                table: "Injuries",
                columns: new[] { "PlayerId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Injuries_RecoveredOn",
                table: "Injuries",
                column: "RecoveredOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Injuries");
        }
    }
}
