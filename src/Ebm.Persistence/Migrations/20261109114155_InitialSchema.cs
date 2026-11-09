using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.CheckConstraint("CK_Seasons_Dates", "EndDate > StartDate");
                });

            migrationBuilder.CreateTable(
                name: "Arenas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CountryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Arenas", x => x.Id);
                    table.CheckConstraint("CK_Arenas_Capacity", "Capacity > 0");
                    table.ForeignKey(
                        name: "FK_Arenas_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Leagues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CountryId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leagues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leagues_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    NationalityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HeightCm = table.Column<int>(type: "INTEGER", nullable: false),
                    WeightKg = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.CheckConstraint("CK_Players_Height", "HeightCm BETWEEN 150 AND 250");
                    table.CheckConstraint("CK_Players_Weight", "WeightKg BETWEEN 50 AND 200");
                    table.ForeignKey(
                        name: "FK_Players_Countries_NationalityId",
                        column: x => x.NationalityId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Staff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    NationalityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Staff", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Staff_Countries_NationalityId",
                        column: x => x.NationalityId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CountryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HomeArenaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FoundedYear = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clubs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clubs_Arenas_HomeArenaId",
                        column: x => x.HomeArenaId,
                        principalTable: "Arenas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Clubs_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Divisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeagueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Divisions", x => x.Id);
                    table.CheckConstraint("CK_Divisions_Level", "Level >= 1");
                    table.ForeignKey(
                        name: "FK_Divisions_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StaffMemberId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AnnualSalary = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                    table.CheckConstraint("CK_Contracts_Dates", "EndDate > StartDate");
                    table.CheckConstraint("CK_Contracts_OnePerson", "(PlayerId IS NULL) <> (StaffMemberId IS NULL)");
                    table.CheckConstraint("CK_Contracts_Salary", "AnnualSalary >= 0");
                    table.ForeignKey(
                        name: "FK_Contracts_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contracts_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contracts_Staff_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerClubHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerClubHistory", x => x.Id);
                    table.CheckConstraint("CK_PlayerClubHistory_Dates", "EndDate IS NULL OR EndDate >= StartDate");
                    table.ForeignKey(
                        name: "FK_PlayerClubHistory_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerClubHistory_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Competitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CountryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DivisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RulesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitions", x => x.Id);
                    table.CheckConstraint("CK_Competitions_RulesJson", "json_valid(RulesJson) AND json_type(RulesJson) = 'object'");
                    table.ForeignKey(
                        name: "FK_Competitions_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Competitions_Divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalTable: "Divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionParticipants_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionParticipants_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionParticipants_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Fixtures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    HomeClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AwayClubId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fixtures", x => x.Id);
                    table.CheckConstraint("CK_Fixtures_DifferentClubs", "HomeClubId <> AwayClubId");
                    table.CheckConstraint("CK_Fixtures_Round", "Round >= 1");
                    table.ForeignKey(
                        name: "FK_Fixtures_Clubs_AwayClubId",
                        column: x => x.AwayClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fixtures_Clubs_HomeClubId",
                        column: x => x.HomeClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fixtures_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fixtures_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeagueStandings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Won = table.Column<int>(type: "INTEGER", nullable: false),
                    Lost = table.Column<int>(type: "INTEGER", nullable: false),
                    PointsFor = table.Column<int>(type: "INTEGER", nullable: false),
                    PointsAgainst = table.Column<int>(type: "INTEGER", nullable: false),
                    StandingPoints = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueStandings", x => x.Id);
                    table.CheckConstraint("CK_LeagueStandings_NonNegative", "Won >= 0 AND Lost >= 0 AND PointsFor >= 0 AND PointsAgainst >= 0 AND StandingPoints >= 0");
                    table.ForeignKey(
                        name: "FK_LeagueStandings_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeagueStandings_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeagueStandings_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FixtureId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HomeScore = table.Column<int>(type: "INTEGER", nullable: false),
                    AwayScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Overtimes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.CheckConstraint("CK_Matches_NoDraw", "HomeScore <> AwayScore");
                    table.CheckConstraint("CK_Matches_Scores", "HomeScore >= 0 AND AwayScore >= 0 AND Overtimes >= 0");
                    table.ForeignKey(
                        name: "FK_Matches_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerMatchStatistics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    TwoPointMade = table.Column<int>(type: "INTEGER", nullable: false),
                    TwoPointAttempted = table.Column<int>(type: "INTEGER", nullable: false),
                    ThreePointMade = table.Column<int>(type: "INTEGER", nullable: false),
                    ThreePointAttempted = table.Column<int>(type: "INTEGER", nullable: false),
                    FreeThrowMade = table.Column<int>(type: "INTEGER", nullable: false),
                    FreeThrowAttempted = table.Column<int>(type: "INTEGER", nullable: false),
                    OffensiveRebounds = table.Column<int>(type: "INTEGER", nullable: false),
                    DefensiveRebounds = table.Column<int>(type: "INTEGER", nullable: false),
                    Assists = table.Column<int>(type: "INTEGER", nullable: false),
                    Steals = table.Column<int>(type: "INTEGER", nullable: false),
                    Blocks = table.Column<int>(type: "INTEGER", nullable: false),
                    Turnovers = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonalFouls = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMatchStatistics", x => x.Id);
                    table.CheckConstraint("CK_PlayerMatchStatistics_Shots", "TwoPointMade BETWEEN 0 AND TwoPointAttempted AND ThreePointMade BETWEEN 0 AND ThreePointAttempted AND FreeThrowMade BETWEEN 0 AND FreeThrowAttempted");
                    table.ForeignKey(
                        name: "FK_PlayerMatchStatistics_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerMatchStatistics_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerMatchStatistics_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Arenas_CountryId",
                table: "Arenas",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_CountryId",
                table: "Clubs",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_HomeArenaId",
                table: "Clubs",
                column: "HomeArenaId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionParticipants_ClubId_SeasonId",
                table: "CompetitionParticipants",
                columns: new[] { "ClubId", "SeasonId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionParticipants_CompetitionId_SeasonId_ClubId",
                table: "CompetitionParticipants",
                columns: new[] { "CompetitionId", "SeasonId", "ClubId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionParticipants_SeasonId",
                table: "CompetitionParticipants",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_CountryId",
                table: "Competitions",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_DivisionId",
                table: "Competitions",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ClubId",
                table: "Contracts",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_PlayerId",
                table: "Contracts",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_StaffMemberId",
                table: "Contracts",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_Code",
                table: "Countries",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Divisions_LeagueId_Level",
                table: "Divisions",
                columns: new[] { "LeagueId", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_AwayClubId",
                table: "Fixtures",
                column: "AwayClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_CompetitionId_SeasonId_Round",
                table: "Fixtures",
                columns: new[] { "CompetitionId", "SeasonId", "Round" });

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_Date",
                table: "Fixtures",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_HomeClubId",
                table: "Fixtures",
                column: "HomeClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_SeasonId",
                table: "Fixtures",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_CountryId",
                table: "Leagues",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueStandings_ClubId",
                table: "LeagueStandings",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueStandings_CompetitionId_SeasonId_ClubId",
                table: "LeagueStandings",
                columns: new[] { "CompetitionId", "SeasonId", "ClubId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueStandings_SeasonId",
                table: "LeagueStandings",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_FixtureId",
                table: "Matches",
                column: "FixtureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerClubHistory_ClubId",
                table: "PlayerClubHistory",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerClubHistory_PlayerId_Current",
                table: "PlayerClubHistory",
                column: "PlayerId",
                unique: true,
                filter: "EndDate IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerClubHistory_PlayerId_StartDate",
                table: "PlayerClubHistory",
                columns: new[] { "PlayerId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStatistics_ClubId",
                table: "PlayerMatchStatistics",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStatistics_MatchId_PlayerId",
                table: "PlayerMatchStatistics",
                columns: new[] { "MatchId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStatistics_PlayerId",
                table: "PlayerMatchStatistics",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_LastName_FirstName",
                table: "Players",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_NationalityId",
                table: "Players",
                column: "NationalityId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_Name",
                table: "Seasons",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Staff_NationalityId",
                table: "Staff",
                column: "NationalityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetitionParticipants");

            migrationBuilder.DropTable(
                name: "Contracts");

            migrationBuilder.DropTable(
                name: "LeagueStandings");

            migrationBuilder.DropTable(
                name: "PlayerAttributes");

            migrationBuilder.DropTable(
                name: "PlayerClubHistory");

            migrationBuilder.DropTable(
                name: "PlayerMatchStatistics");

            migrationBuilder.DropTable(
                name: "Staff");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Fixtures");

            migrationBuilder.DropTable(
                name: "Clubs");

            migrationBuilder.DropTable(
                name: "Competitions");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropTable(
                name: "Arenas");

            migrationBuilder.DropTable(
                name: "Divisions");

            migrationBuilder.DropTable(
                name: "Leagues");

            migrationBuilder.DropTable(
                name: "Countries");
        }
    }
}
