using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Finances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClubBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reputation = table.Column<int>(type: "INTEGER", nullable: false),
                    OpeningBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    Sponsorship = table.Column<long>(type: "INTEGER", nullable: false),
                    TicketPrice = table.Column<long>(type: "INTEGER", nullable: false),
                    Attendance = table.Column<int>(type: "INTEGER", nullable: false),
                    HomeGames = table.Column<int>(type: "INTEGER", nullable: false),
                    TicketSales = table.Column<long>(type: "INTEGER", nullable: false),
                    ArenaCosts = table.Column<long>(type: "INTEGER", nullable: false),
                    TravelCosts = table.Column<long>(type: "INTEGER", nullable: false),
                    StaffWages = table.Column<long>(type: "INTEGER", nullable: false),
                    WageBudget = table.Column<long>(type: "INTEGER", nullable: false),
                    TransferBudget = table.Column<long>(type: "INTEGER", nullable: false),
                    MinimumBalance = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubBudgets_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClubBudgets_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialTransactions_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubBudgets_ClubId_SeasonId",
                table: "ClubBudgets",
                columns: new[] { "ClubId", "SeasonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubBudgets_SeasonId",
                table: "ClubBudgets",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_ClubId_Date",
                table: "FinancialTransactions",
                columns: new[] { "ClubId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubBudgets");

            migrationBuilder.DropTable(
                name: "FinancialTransactions");
        }
    }
}
