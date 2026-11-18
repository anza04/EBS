using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TacticalEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SituationCoachAdjusts",
                table: "Tactics",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SituationEndOfQuarter",
                table: "Tactics",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<string>(
                name: "SituationFoulManagement",
                table: "Tactics",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<bool>(
                name: "SituationFoulUpThree",
                table: "Tactics",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SituationFoulWhenTrailing",
                table: "Tactics",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SituationHurryWhenTrailing",
                table: "Tactics",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SituationMatchupTargeting",
                table: "Tactics",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Off");

            migrationBuilder.AddColumn<bool>(
                name: "SituationMilkClockWhenLeading",
                table: "Tactics",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SituationPlayCalling",
                table: "Tactics",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Flow");

            migrationBuilder.AddColumn<bool>(
                name: "SituationRestStartersInBlowouts",
                table: "Tactics",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SituationRotation",
                table: "Tactics",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "SituationZoneOffense",
                table: "Tactics",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "TacticalNotes",
                table: "Matches",
                type: "TEXT",
                nullable: true);

            // Existing AI tactics get the AI coach's game management until they are planned again.
            migrationBuilder.Sql("UPDATE Tactics SET SituationCoachAdjusts = 1, SituationEndOfQuarter = 'TwoForOne' WHERE Name = 'AI plan';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SituationCoachAdjusts",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "SituationEndOfQuarter",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "SituationFoulManagement",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "SituationFoulUpThree",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "SituationFoulWhenTrailing",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "SituationHurryWhenTrailing",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "SituationMatchupTargeting",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "SituationMilkClockWhenLeading",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "SituationPlayCalling",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "SituationRestStartersInBlowouts",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "SituationRotation",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "SituationZoneOffense",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "TacticalNotes",
                table: "Matches");
        }
    }
}
