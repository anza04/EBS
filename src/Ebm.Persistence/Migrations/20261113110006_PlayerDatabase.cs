using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttributesStatus",
                table: "Players",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unverified");

            migrationBuilder.AddColumn<string>(
                name: "ProfileStatus",
                table: "Players",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unverified");

            // SQLite adds a column with its foreign key in place; EF's AddForeignKey would rebuild the table (decision 21).
            migrationBuilder.Sql(
                "ALTER TABLE \"Players\" ADD COLUMN \"SecondNationalityId\" TEXT NULL " +
                "CONSTRAINT \"FK_Players_Countries_SecondNationalityId\" REFERENCES \"Countries\" (\"Id\") ON DELETE RESTRICT;");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Players",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Contracts",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsStatus",
                table: "Contracts",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unverified");

            migrationBuilder.AddColumn<string>(
                name: "DataStatus",
                table: "Clubs",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unverified");

            migrationBuilder.CreateIndex(
                name: "IX_Players_SecondNationalityId",
                table: "Players",
                column: "SecondNationalityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_Countries_SecondNationalityId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Players_SecondNationalityId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "AttributesStatus",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ProfileStatus",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "SecondNationalityId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "TermsStatus",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "DataStatus",
                table: "Clubs");
        }
    }
}
