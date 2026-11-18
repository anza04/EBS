using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractNegotiations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PromiseReviewedOn",
                table: "Contracts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromiseStatus",
                table: "Contracts",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<int>(
                name: "PromisedMinutes",
                table: "Contracts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromisedRole",
                table: "Contracts",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Toughness = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContractNegotiations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RenewalOfId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LastActivityOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Rounds = table.Column<int>(type: "INTEGER", nullable: false),
                    Patience = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ClosedOn = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractNegotiations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractNegotiations_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractNegotiations_Contracts_RenewalOfId",
                        column: x => x.RenewalOfId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractNegotiations_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentRelationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRelationships_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentRelationships_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRelationships_AgentId_ClubId",
                table: "AgentRelationships",
                columns: new[] { "AgentId", "ClubId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentRelationships_ClubId",
                table: "AgentRelationships",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_Name",
                table: "Agents",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractNegotiations_ClubId",
                table: "ContractNegotiations",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractNegotiations_PlayerId_ClubId_StartedOn",
                table: "ContractNegotiations",
                columns: new[] { "PlayerId", "ClubId", "StartedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractNegotiations_RenewalOfId",
                table: "ContractNegotiations",
                column: "RenewalOfId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRelationships");

            migrationBuilder.DropTable(
                name: "ContractNegotiations");

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropColumn(
                name: "PromiseReviewedOn",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PromiseStatus",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PromisedMinutes",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PromisedRole",
                table: "Contracts");
        }
    }
}
