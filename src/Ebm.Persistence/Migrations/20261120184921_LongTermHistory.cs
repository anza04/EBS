using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LongTermHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FormerPlayerId",
                table: "Staff",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "YouthClubId",
                table: "Players",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HallOfFame",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InductedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Seasons = table.Column<int>(type: "INTEGER", nullable: false),
                    Games = table.Column<int>(type: "INTEGER", nullable: false),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    Titles = table.Column<int>(type: "INTEGER", nullable: false),
                    Awards = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HallOfFame", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeagueSeasonSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Clubs = table.Column<int>(type: "INTEGER", nullable: false),
                    MeanReputation = table.Column<int>(type: "INTEGER", nullable: false),
                    TopReputation = table.Column<int>(type: "INTEGER", nullable: false),
                    MeanOverall = table.Column<int>(type: "INTEGER", nullable: false),
                    Champion = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueSeasonSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeasonAwards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StaffMemberId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Value = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonAwards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HallOfFame_PlayerId",
                table: "HallOfFame",
                column: "PlayerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueSeasonSnapshots_CompetitionId_SeasonId",
                table: "LeagueSeasonSnapshots",
                columns: new[] { "CompetitionId", "SeasonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonAwards_ClubId",
                table: "SeasonAwards",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonAwards_PlayerId",
                table: "SeasonAwards",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonAwards_SeasonId_CompetitionId_Kind_Rank",
                table: "SeasonAwards",
                columns: new[] { "SeasonId", "CompetitionId", "Kind", "Rank" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HallOfFame");

            migrationBuilder.DropTable(
                name: "LeagueSeasonSnapshots");

            migrationBuilder.DropTable(
                name: "SeasonAwards");

            migrationBuilder.DropColumn(
                name: "FormerPlayerId",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "YouthClubId",
                table: "Players");
        }
    }
}
