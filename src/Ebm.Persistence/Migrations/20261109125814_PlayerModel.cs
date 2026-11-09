using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <summary>
    /// Step 05: full player model. Adds profile and condition columns to Players, replaces the 8 initial ratings
    /// with the 27-attribute catalogue (values mapped from the old ratings) and adds PlayerTendencies (suggested
    /// from the attributes). Hand-written so that no table is rebuilt: the upgrade stays in one transaction
    /// (decision 21). Mapping: docs/PLAYER_MODEL.md §7.
    /// </summary>
    public partial class PlayerModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Experience",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Fitness",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InjuryReturnDate",
                table: "Players",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InjuryStatus",
                table: "Players",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Healthy");

            migrationBuilder.AddColumn<int>(
                name: "Morale",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<int>(
                name: "Potential",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<int>(
                name: "Reputation",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryPositions",
                table: "Players",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "WingspanCm",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.RenameTable(name: "PlayerAttributes", newName: "PlayerAttributes_Step04");

            migrationBuilder.CreateTable(
                name: "PlayerAttributes",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CloseShot = table.Column<int>(type: "INTEGER", nullable: false),
                    MidRange = table.Column<int>(type: "INTEGER", nullable: false),
                    ThreePoint = table.Column<int>(type: "INTEGER", nullable: false),
                    FreeThrow = table.Column<int>(type: "INTEGER", nullable: false),
                    Layup = table.Column<int>(type: "INTEGER", nullable: false),
                    Dunk = table.Column<int>(type: "INTEGER", nullable: false),
                    PostMoves = table.Column<int>(type: "INTEGER", nullable: false),
                    Passing = table.Column<int>(type: "INTEGER", nullable: false),
                    CourtVision = table.Column<int>(type: "INTEGER", nullable: false),
                    BallHandling = table.Column<int>(type: "INTEGER", nullable: false),
                    DecisionMaking = table.Column<int>(type: "INTEGER", nullable: false),
                    PerimeterDefense = table.Column<int>(type: "INTEGER", nullable: false),
                    InteriorDefense = table.Column<int>(type: "INTEGER", nullable: false),
                    Steal = table.Column<int>(type: "INTEGER", nullable: false),
                    Block = table.Column<int>(type: "INTEGER", nullable: false),
                    DefensivePositioning = table.Column<int>(type: "INTEGER", nullable: false),
                    OffensiveRebound = table.Column<int>(type: "INTEGER", nullable: false),
                    DefensiveRebound = table.Column<int>(type: "INTEGER", nullable: false),
                    Speed = table.Column<int>(type: "INTEGER", nullable: false),
                    Strength = table.Column<int>(type: "INTEGER", nullable: false),
                    Vertical = table.Column<int>(type: "INTEGER", nullable: false),
                    Agility = table.Column<int>(type: "INTEGER", nullable: false),
                    Stamina = table.Column<int>(type: "INTEGER", nullable: false),
                    Concentration = table.Column<int>(type: "INTEGER", nullable: false),
                    Leadership = table.Column<int>(type: "INTEGER", nullable: false),
                    Consistency = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkRate = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAttributes", x => x.PlayerId);
                    table.CheckConstraint("CK_PlayerAttributes_Range", "CloseShot BETWEEN 1 AND 100 AND MidRange BETWEEN 1 AND 100 AND ThreePoint BETWEEN 1 AND 100 AND FreeThrow BETWEEN 1 AND 100 AND Layup BETWEEN 1 AND 100 AND Dunk BETWEEN 1 AND 100 AND PostMoves BETWEEN 1 AND 100 AND Passing BETWEEN 1 AND 100 AND CourtVision BETWEEN 1 AND 100 AND BallHandling BETWEEN 1 AND 100 AND DecisionMaking BETWEEN 1 AND 100 AND PerimeterDefense BETWEEN 1 AND 100 AND InteriorDefense BETWEEN 1 AND 100 AND Steal BETWEEN 1 AND 100 AND Block BETWEEN 1 AND 100 AND DefensivePositioning BETWEEN 1 AND 100 AND OffensiveRebound BETWEEN 1 AND 100 AND DefensiveRebound BETWEEN 1 AND 100 AND Speed BETWEEN 1 AND 100 AND Strength BETWEEN 1 AND 100 AND Vertical BETWEEN 1 AND 100 AND Agility BETWEEN 1 AND 100 AND Stamina BETWEEN 1 AND 100 AND Concentration BETWEEN 1 AND 100 AND Leadership BETWEEN 1 AND 100 AND Consistency BETWEEN 1 AND 100 AND WorkRate BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "FK_PlayerAttributes_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO PlayerAttributes (PlayerId, CloseShot, MidRange, ThreePoint, FreeThrow, Layup, Dunk, PostMoves, Passing, CourtVision, BallHandling, DecisionMaking, PerimeterDefense, InteriorDefense, Steal, Block, DefensivePositioning, OffensiveRebound, DefensiveRebound, Speed, Strength, Vertical, Agility, Stamina, Concentration, Leadership, Consistency, WorkRate)
                SELECT PlayerId, (Shooting + InsideScoring) / 2, Shooting, Shooting, Shooting, InsideScoring, (InsideScoring + Athleticism) / 2, InsideScoring, Passing, (Passing + BasketballIq) / 2, BallHandling, BasketballIq, Defense, Defense, Defense, (Defense + Rebounding) / 2, (Defense + BasketballIq) / 2, Rebounding, Rebounding, Athleticism, (Athleticism + Rebounding) / 2, Athleticism, Athleticism, Athleticism, BasketballIq, BasketballIq, BasketballIq, BasketballIq
                FROM PlayerAttributes_Step04;
                """);

            migrationBuilder.Sql("""
                UPDATE Players SET
                    WingspanCm = HeightCm,
                    Potential = COALESCE((SELECT (Shooting + InsideScoring + Passing + BallHandling + Rebounding + Defense + Athleticism + BasketballIq) / 8 FROM PlayerAttributes_Step04 l WHERE l.PlayerId = Players.Id), Potential);
                """);

            migrationBuilder.DropTable(name: "PlayerAttributes_Step04");

            migrationBuilder.CreateTable(
                name: "PlayerTendencies",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ThreePointShot = table.Column<int>(type: "INTEGER", nullable: false),
                    MidRangeShot = table.Column<int>(type: "INTEGER", nullable: false),
                    DriveToBasket = table.Column<int>(type: "INTEGER", nullable: false),
                    PostUp = table.Column<int>(type: "INTEGER", nullable: false),
                    PassFirst = table.Column<int>(type: "INTEGER", nullable: false),
                    Isolation = table.Column<int>(type: "INTEGER", nullable: false),
                    CrashOffensiveGlass = table.Column<int>(type: "INTEGER", nullable: false),
                    DefensiveGambling = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerTendencies", x => x.PlayerId);
                    table.CheckConstraint("CK_PlayerTendencies_Range", "ThreePointShot BETWEEN 1 AND 100 AND MidRangeShot BETWEEN 1 AND 100 AND DriveToBasket BETWEEN 1 AND 100 AND PostUp BETWEEN 1 AND 100 AND PassFirst BETWEEN 1 AND 100 AND Isolation BETWEEN 1 AND 100 AND CrashOffensiveGlass BETWEEN 1 AND 100 AND DefensiveGambling BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "FK_PlayerTendencies_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO PlayerTendencies (PlayerId, ThreePointShot, MidRangeShot, DriveToBasket, PostUp, PassFirst, Isolation, CrashOffensiveGlass, DefensiveGambling)
                SELECT PlayerId, CAST(ROUND((3.0 * ThreePoint + 1.0 * MidRange) / 4.0) AS INTEGER), CAST(ROUND((3.0 * MidRange + 1.0 * CloseShot) / 4.0) AS INTEGER), CAST(ROUND((2.0 * Layup + 1.0 * BallHandling + 1.0 * Speed) / 4.0) AS INTEGER), CAST(ROUND((3.0 * PostMoves + 1.0 * Strength) / 4.0) AS INTEGER), CAST(ROUND((1.0 * Passing + 1.0 * CourtVision) / 2.0) AS INTEGER), CAST(ROUND((2.0 * BallHandling + 1.0 * MidRange) / 3.0) AS INTEGER), CAST(ROUND((3.0 * OffensiveRebound + 1.0 * WorkRate) / 4.0) AS INTEGER), CAST(ROUND((2.0 * Steal + 1.0 * Block) / 3.0) AS INTEGER)
                FROM PlayerAttributes;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlayerTendencies");

            migrationBuilder.RenameTable(name: "PlayerAttributes", newName: "PlayerAttributes_Step05");

            migrationBuilder.CreateTable(
                name: "PlayerAttributes",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Shooting = table.Column<int>(type: "INTEGER", nullable: false),
                    InsideScoring = table.Column<int>(type: "INTEGER", nullable: false),
                    Passing = table.Column<int>(type: "INTEGER", nullable: false),
                    BallHandling = table.Column<int>(type: "INTEGER", nullable: false),
                    Rebounding = table.Column<int>(type: "INTEGER", nullable: false),
                    Defense = table.Column<int>(type: "INTEGER", nullable: false),
                    Athleticism = table.Column<int>(type: "INTEGER", nullable: false),
                    BasketballIq = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAttributes", x => x.PlayerId);
                    table.CheckConstraint("CK_PlayerAttributes_Range", "Shooting BETWEEN 1 AND 100 AND InsideScoring BETWEEN 1 AND 100 AND Passing BETWEEN 1 AND 100 AND BallHandling BETWEEN 1 AND 100 AND Rebounding BETWEEN 1 AND 100 AND Defense BETWEEN 1 AND 100 AND Athleticism BETWEEN 1 AND 100 AND BasketballIq BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "FK_PlayerAttributes_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO PlayerAttributes (PlayerId, Shooting, InsideScoring, Passing, BallHandling, Rebounding, Defense, Athleticism, BasketballIq)
                SELECT PlayerId, (CloseShot + MidRange + ThreePoint + FreeThrow) / 4, (Layup + Dunk + PostMoves) / 3, Passing, BallHandling, (OffensiveRebound + DefensiveRebound) / 2, (PerimeterDefense + InteriorDefense + Steal + Block + DefensivePositioning) / 5, (Speed + Strength + Vertical + Agility + Stamina) / 5, (DecisionMaking + Concentration) / 2
                FROM PlayerAttributes_Step05;
                """);

            migrationBuilder.DropTable(name: "PlayerAttributes_Step05");

            migrationBuilder.DropColumn(
                name: "Experience",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Fitness",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "InjuryReturnDate",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "InjuryStatus",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Morale",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Potential",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Reputation",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "SecondaryPositions",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "WingspanCm",
                table: "Players");
        }
    }
}
