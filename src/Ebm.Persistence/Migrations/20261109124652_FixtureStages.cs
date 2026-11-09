using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixtureStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameNumber",
                table: "Fixtures",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "Fixtures",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "RegularSeason");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GameNumber",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "Fixtures");
        }
    }
}
