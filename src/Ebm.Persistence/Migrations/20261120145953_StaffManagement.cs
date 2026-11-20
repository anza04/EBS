using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Ability",
                table: "Staff",
                newName: "TacticalKnowledge");

            migrationBuilder.AddColumn<int>(
                name: "Development",
                table: "Staff",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<int>(
                name: "Experience",
                table: "Staff",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MedicalExpertise",
                table: "Staff",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<int>(
                name: "Motivation",
                table: "Staff",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<string>(
                name: "PreferredDefense",
                table: "Staff",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredOffense",
                table: "Staff",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Reputation",
                table: "Staff",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<int>(
                name: "Scouting",
                table: "Staff",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<string>(
                name: "Style",
                table: "Staff",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Balanced");

            // Every attribute takes the old ability (kept in TacticalKnowledge), so each member's role ability is unchanged.
            migrationBuilder.Sql("UPDATE Staff SET Development = TacticalKnowledge, Motivation = TacticalKnowledge, Scouting = TacticalKnowledge, MedicalExpertise = TacticalKnowledge, Reputation = TacticalKnowledge;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Development",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "Experience",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "MedicalExpertise",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "Motivation",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "PreferredDefense",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "PreferredOffense",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "Reputation",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "Scouting",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "Style",
                table: "Staff");

            migrationBuilder.RenameColumn(
                name: "TacticalKnowledge",
                table: "Staff",
                newName: "Ability");
        }
    }
}
