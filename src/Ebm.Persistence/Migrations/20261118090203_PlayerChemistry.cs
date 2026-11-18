using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerChemistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerRelationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerAId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerBId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedOn = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerRelationships_Players_PlayerAId",
                        column: x => x.PlayerAId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerRelationships_Players_PlayerBId",
                        column: x => x.PlayerBId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRelationships_PlayerAId_PlayerBId",
                table: "PlayerRelationships",
                columns: new[] { "PlayerAId", "PlayerBId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRelationships_PlayerBId",
                table: "PlayerRelationships",
                column: "PlayerBId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerRelationships");
        }
    }
}
