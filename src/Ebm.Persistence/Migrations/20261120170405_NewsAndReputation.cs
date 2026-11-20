using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NewsAndReputation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NewsItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Headline = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Sentiment = table.Column<int>(type: "INTEGER", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OtherClubId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FixtureId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TransferId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InjuryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReputationChangeId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReputationChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Cause = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Before = table.Column<int>(type: "INTEGER", nullable: false),
                    After = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReputationChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_ClubId",
                table: "NewsItems",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_Date",
                table: "NewsItems",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_Key",
                table: "NewsItems",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_OtherClubId",
                table: "NewsItems",
                column: "OtherClubId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_PlayerId",
                table: "NewsItems",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReputationChanges_ClubId_Date",
                table: "ReputationChanges",
                columns: new[] { "ClubId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ReputationChanges_Key",
                table: "ReputationChanges",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewsItems");

            migrationBuilder.DropTable(
                name: "ReputationChanges");
        }
    }
}
