using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Scouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Ability",
                table: "Staff",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.CreateTable(
                name: "ScoutingAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScoutId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Focus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CountryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompetitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Position = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    MaxAge = table.Column<int>(type: "INTEGER", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    EndedOn = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoutingAssignments", x => x.Id);
                    table.CheckConstraint("CK_ScoutingAssignments_Dates", "DueDate > StartDate");
                    table.ForeignKey(
                        name: "FK_ScoutingAssignments_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScoutingAssignments_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScoutingAssignments_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScoutingAssignments_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScoutingAssignments_Staff_ScoutId",
                        column: x => x.ScoutId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScoutReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScoutId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AssignmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Accuracy = table.Column<int>(type: "INTEGER", nullable: false),
                    Margin = table.Column<int>(type: "INTEGER", nullable: false),
                    PotentialMargin = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedOverall = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedPotential = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedValue = table.Column<long>(type: "INTEGER", nullable: false),
                    EstimatedSalary = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoutReports", x => x.Id);
                    table.CheckConstraint("CK_ScoutReports_Accuracy", "Accuracy BETWEEN 5 AND 98");
                    table.CheckConstraint("CK_ScoutReports_Money", "EstimatedValue >= 0 AND EstimatedSalary >= 0");
                    table.ForeignKey(
                        name: "FK_ScoutReports_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScoutReports_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScoutReports_ScoutingAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "ScoutingAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScoutReports_Staff_ScoutId",
                        column: x => x.ScoutId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScoutReportAttributes",
                columns: table => new
                {
                    ScoutReportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CloseShot = table.Column<int>(type: "INTEGER", nullable: false),
                    MidRange = table.Column<int>(type: "INTEGER", nullable: false),
                    ThreePoint = table.Column<int>(type: "INTEGER", nullable: false),
                    FreeThrow = table.Column<int>(type: "INTEGER", nullable: false),
                    Layup = table.Column<int>(type: "INTEGER", nullable: false),
                    Dunk = table.Column<int>(type: "INTEGER", nullable: false),
                    PostMoves = table.Column<int>(type: "INTEGER", nullable: false),
                    Passing = table.Column<int>(type: "INTEGER", nullable: false),
                    CourtVision = table.Column<int>(type: "INTEGER", nullable: false),
                    BallHandling = table.Column<int>(type: "INTEGER", nullable: false),
                    DecisionMaking = table.Column<int>(type: "INTEGER", nullable: false),
                    PerimeterDefense = table.Column<int>(type: "INTEGER", nullable: false),
                    InteriorDefense = table.Column<int>(type: "INTEGER", nullable: false),
                    Steal = table.Column<int>(type: "INTEGER", nullable: false),
                    Block = table.Column<int>(type: "INTEGER", nullable: false),
                    DefensivePositioning = table.Column<int>(type: "INTEGER", nullable: false),
                    OffensiveRebound = table.Column<int>(type: "INTEGER", nullable: false),
                    DefensiveRebound = table.Column<int>(type: "INTEGER", nullable: false),
                    Speed = table.Column<int>(type: "INTEGER", nullable: false),
                    Strength = table.Column<int>(type: "INTEGER", nullable: false),
                    Vertical = table.Column<int>(type: "INTEGER", nullable: false),
                    Agility = table.Column<int>(type: "INTEGER", nullable: false),
                    Stamina = table.Column<int>(type: "INTEGER", nullable: false),
                    Concentration = table.Column<int>(type: "INTEGER", nullable: false),
                    Leadership = table.Column<int>(type: "INTEGER", nullable: false),
                    Consistency = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkRate = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoutReportAttributes", x => x.ScoutReportId);
                    table.ForeignKey(
                        name: "FK_ScoutReportAttributes_ScoutReports_ScoutReportId",
                        column: x => x.ScoutReportId,
                        principalTable: "ScoutReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScoutingAssignments_ClubId_Status",
                table: "ScoutingAssignments",
                columns: new[] { "ClubId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScoutingAssignments_CompetitionId",
                table: "ScoutingAssignments",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoutingAssignments_CountryId",
                table: "ScoutingAssignments",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoutingAssignments_PlayerId",
                table: "ScoutingAssignments",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoutingAssignments_ScoutId",
                table: "ScoutingAssignments",
                column: "ScoutId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoutingAssignments_Status_DueDate",
                table: "ScoutingAssignments",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ScoutReports_AssignmentId",
                table: "ScoutReports",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoutReports_ClubId_PlayerId_Date",
                table: "ScoutReports",
                columns: new[] { "ClubId", "PlayerId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ScoutReports_PlayerId",
                table: "ScoutReports",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoutReports_ScoutId",
                table: "ScoutReports",
                column: "ScoutId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScoutReportAttributes");

            migrationBuilder.DropTable(
                name: "ScoutReports");

            migrationBuilder.DropTable(
                name: "ScoutingAssignments");

            migrationBuilder.DropColumn(
                name: "Ability",
                table: "Staff");
        }
    }
}
