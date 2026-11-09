using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Contracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contracts_ClubId",
                table: "Contracts");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ClubOptionUntil",
                table: "Contracts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentFrequency",
                table: "Contracts",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Monthly");

            migrationBuilder.AddColumn<string>(
                name: "PerformanceBonuses",
                table: "Contracts",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlayerOptionUntil",
                table: "Contracts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReleaseClause",
                table: "Contracts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SigningBonus",
                table: "Contracts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Contracts",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<long>(
                name: "TerminationCompensation",
                table: "Contracts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TerminationDate",
                table: "Contracts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ClubId_EndDate",
                table: "Contracts",
                columns: new[] { "ClubId", "EndDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contracts_ClubId_EndDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ClubOptionUntil",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PaymentFrequency",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PerformanceBonuses",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PlayerOptionUntil",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ReleaseClause",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SigningBonus",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "TerminationCompensation",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "TerminationDate",
                table: "Contracts");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ClubId",
                table: "Contracts",
                column: "ClubId");
        }
    }
}
