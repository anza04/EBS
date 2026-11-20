using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvancedSimulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefendedAttempted",
                table: "PlayerMatchStatistics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DefendedMade",
                table: "PlayerMatchStatistics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DefensivePossessions",
                table: "PlayerMatchStatistics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OffensivePossessions",
                table: "PlayerMatchStatistics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PointsAgainst",
                table: "PlayerMatchStatistics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PointsFor",
                table: "PlayerMatchStatistics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ShotData",
                table: "PlayerMatchStatistics",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefendedAttempted",
                table: "PlayerMatchStatistics");

            migrationBuilder.DropColumn(
                name: "DefendedMade",
                table: "PlayerMatchStatistics");

            migrationBuilder.DropColumn(
                name: "DefensivePossessions",
                table: "PlayerMatchStatistics");

            migrationBuilder.DropColumn(
                name: "OffensivePossessions",
                table: "PlayerMatchStatistics");

            migrationBuilder.DropColumn(
                name: "PointsAgainst",
                table: "PlayerMatchStatistics");

            migrationBuilder.DropColumn(
                name: "PointsFor",
                table: "PlayerMatchStatistics");

            migrationBuilder.DropColumn(
                name: "ShotData",
                table: "PlayerMatchStatistics");
        }
    }
}
