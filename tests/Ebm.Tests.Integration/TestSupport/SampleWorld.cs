using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Domain.Staff;
using Ebm.Domain.Standings;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.TestSupport;

/// <summary>
/// Mock data set with one record of every entity type: a country with a two-division league, a cup,
/// two clubs, a player with a contract and club history, a staff member, a played match and standings.
/// </summary>
public sealed class SampleWorld
{
    public static readonly DateOnly SeasonStart = new(2026, 9, 1);

    public Country Italy { get; } = new(CountryId.New(), "ITA", "Italy");

    public League League { get; private set; } = null!;

    public Division TopDivision { get; private set; } = null!;

    public Division SecondDivision { get; private set; } = null!;

    public Season Season { get; private set; } = null!;

    public Competition Championship { get; private set; } = null!;

    public Competition Cup { get; private set; } = null!;

    public Arena Arena { get; private set; } = null!;

    public Club HomeClub { get; private set; } = null!;

    public Club AwayClub { get; private set; } = null!;

    public Player Player { get; private set; } = null!;

    public StaffMember Coach { get; private set; } = null!;

    public PlayerClubHistoryEntry PlayerSpell { get; private set; } = null!;

    public Fixture Fixture { get; private set; } = null!;

    public Match Match { get; private set; } = null!;

    public PlayerMatchStatistics PlayerStats { get; private set; } = null!;

    public static async Task<SampleWorld> SeedAsync(TestDatabase database)
    {
        var world = new SampleWorld();
        await database.WriteAsync(world.AddAll);
        return world;
    }

    private void AddAll(TestDatabase.Scope s)
    {
        League = new League(LeagueId.New(), "Lega Basket", "LBA", Italy.Id);
        TopDivision = new Division(DivisionId.New(), League.Id, "Serie A", 1);
        SecondDivision = new Division(DivisionId.New(), League.Id, "Serie A2", 2);
        Season = new Season(SeasonId.New(), "2026-27", SeasonStart, new DateOnly(2027, 6, 30));
        Championship = new Competition(CompetitionId.New(), "Serie A", "SA", CompetitionType.League, Italy.Id, TopDivision.Id, """{"pointsForWin":2,"pointsForLoss":0}""");
        Cup = new Competition(CompetitionId.New(), "Italian Cup", "CUP", CompetitionType.Cup, Italy.Id, null);
        Arena = new Arena(ArenaId.New(), "Mock Arena", "Milan", Italy.Id, 12_000);
        HomeClub = new Club(ClubId.New(), "Mock Milano", "MIL", Italy.Id, Arena.Id, 1936);
        AwayClub = new Club(ClubId.New(), "Mock Bologna", "BOL", Italy.Id);
        Player = new Player(PlayerId.New(), "Mario", "Mock", new DateOnly(2000, 5, 10), Italy.Id, 201, 98, Position.SmallForward, new PlayerAttributes(70, 60, 55, 65, 50, 62, 75, 68));
        Coach = new StaffMember(StaffMemberId.New(), "Carlo", "Mock", new DateOnly(1970, 1, 15), Italy.Id, StaffRole.HeadCoach);
        PlayerSpell = new PlayerClubHistoryEntry(PlayerClubHistoryEntryId.New(), Player.Id, HomeClub.Id, SeasonStart);
        Fixture = new Fixture(FixtureId.New(), Championship.Id, Season.Id, 1, SeasonStart.AddDays(30), HomeClub.Id, AwayClub.Id);
        Match = new Match(MatchId.New(), Fixture.Id, 88, 81);
        PlayerStats = new PlayerMatchStatistics(PlayerMatchStatisticsId.New(), Match.Id, Player.Id, HomeClub.Id, new BoxScore
        {
            Minutes = 32, TwoPointMade = 6, TwoPointAttempted = 10, ThreePointMade = 3, ThreePointAttempted = 7,
            FreeThrowMade = 4, FreeThrowAttempted = 5, DefensiveRebounds = 6, Assists = 3,
        });
        var homeStanding = new LeagueStanding(LeagueStandingId.New(), Championship.Id, Season.Id, HomeClub.Id);
        homeStanding.RecordResult(88, 81, 2);
        var awayStanding = new LeagueStanding(LeagueStandingId.New(), Championship.Id, Season.Id, AwayClub.Id);
        awayStanding.RecordResult(81, 88, 0);

        Add(s, Italy); Add(s, League); Add(s, TopDivision); Add(s, SecondDivision); Add(s, Season);
        Add(s, Championship); Add(s, Cup); Add(s, Arena); Add(s, HomeClub); Add(s, AwayClub);
        Add(s, Player); Add(s, Coach); Add(s, PlayerSpell); Add(s, Fixture); Add(s, Match); Add(s, PlayerStats);
        Add(s, homeStanding); Add(s, awayStanding);
        Add(s, Contract.ForPlayer(ContractId.New(), HomeClub.Id, Player.Id, SeasonStart, SeasonStart.AddYears(2), 300_000));
        Add(s, Contract.ForStaff(ContractId.New(), HomeClub.Id, Coach.Id, SeasonStart, SeasonStart.AddYears(1), 150_000));
        foreach (var competition in new[] { Championship, Cup })
        {
            Add(s, new CompetitionParticipant(CompetitionParticipantId.New(), competition.Id, Season.Id, HomeClub.Id));
        }

        Add(s, new CompetitionParticipant(CompetitionParticipantId.New(), Championship.Id, Season.Id, AwayClub.Id));
    }

    // Adding through the DbContext keeps the seed generic; tests read back through the repositories.
    private static void Add(TestDatabase.Scope scope, object entity) =>
        scope.Services.GetRequiredService<Ebm.Persistence.EbmDbContext>().Add(entity);
}
