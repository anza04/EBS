using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Transfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromClubId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ToClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Fee = table.Column<long>(type: "INTEGER", nullable: false),
                    ContractId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ParentContractId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LoanEndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ReturnedOn = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.CheckConstraint("CK_Transfers_Clubs", "FromClubId IS NULL OR FromClubId <> ToClubId");
                    table.CheckConstraint("CK_Transfers_Fee", "Fee >= 0");
                    table.CheckConstraint("CK_Transfers_Loan", "(Kind = 'Loan') = (LoanEndDate IS NOT NULL) AND (LoanEndDate IS NULL OR LoanEndDate > Date)");
                    table.CheckConstraint("CK_Transfers_Return", "ReturnedOn IS NULL OR (Kind = 'Loan' AND ReturnedOn > Date)");
                    table.ForeignKey(
                        name: "FK_Transfers_Clubs_FromClubId",
                        column: x => x.FromClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Clubs_ToClubId",
                        column: x => x.ToClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Contracts_ParentContractId",
                        column: x => x.ParentContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_ContractId",
                table: "Transfers",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_FromClubId_Date",
                table: "Transfers",
                columns: new[] { "FromClubId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_Kind_ReturnedOn",
                table: "Transfers",
                columns: new[] { "Kind", "ReturnedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_ParentContractId",
                table: "Transfers",
                column: "ParentContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_PlayerId_Date",
                table: "Transfers",
                columns: new[] { "PlayerId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_ToClubId_Date",
                table: "Transfers",
                columns: new[] { "ToClubId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transfers");
        }
    }
}
