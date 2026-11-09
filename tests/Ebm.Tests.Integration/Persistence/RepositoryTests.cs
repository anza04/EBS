using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Domain.Standings;
using Ebm.Tests.Integration.TestSupport;

namespace Ebm.Tests.Integration.Persistence;

/// <summary>Sample records of every entity type can be inserted and read back.</summary>
public sealed class RepositoryTests
{
    [Fact]
    public async Task Player_round_trips_with_attributes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var world = await SampleWorld.SeedAsync(db);

        var player = await db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(world.Player.Id));

        Assert.NotNull(player);
        Assert.Equal("Mario Mock", player.FullName);
        Assert.Equal(world.Italy.Id, player.NationalityId);
        Assert.Equal(Position.SmallForward, player.Position);
        Assert.Equal(72, player.Attributes.ThreePoint);
        Assert.Equal(65, player.Attributes.WorkRate);
    }

    [Fact]
    public async Task Competition_rules_are_stored_as_data()
    {
        await using var db = await TestDatabase.CreateAsync();
        var world = await SampleWorld.SeedAsync(db);

        var competition = await db.ReadAsync(s => s.Repo<Competition, CompetitionId>().FindAsync(world.Championship.Id));

        Assert.Equal("""{"pointsForWin":2,"pointsForLoss":0}""", competition!.RulesJson);
        Assert.Equal(CompetitionType.League, competition.Type);
        Assert.Equal(world.TopDivision.Id, competition.DivisionId);
    }

    [Fact]
    public async Task Every_entity_type_is_persisted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var world = await SampleWorld.SeedAsync(db);

        var counts = await db.ReadAsync(async s => new Dictionary<string, int>
        {
            ["divisions"] = (await s.Repo<Division, DivisionId>().ListAsync(d => d.LeagueId == world.League.Id)).Count,
            ["clubs"] = (await s.Repo<Club, ClubId>().ListAsync()).Count,
            ["arenas"] = (await s.Repo<Arena, ArenaId>().ListAsync()).Count,
            ["contracts"] = (await s.Repo<Contract, ContractId>().ListAsync(c => c.ClubId == world.HomeClub.Id)).Count,
            ["fixtures"] = (await s.Repo<Fixture, FixtureId>().ListAsync()).Count,
            ["matches"] = (await s.Repo<Match, MatchId>().ListAsync()).Count,
            ["standings"] = (await s.Repo<LeagueStanding, LeagueStandingId>().ListAsync(x => x.SeasonId == world.Season.Id)).Count,
        });

        Assert.Equal(2, counts["divisions"]);
        Assert.Equal(2, counts["clubs"]);
        Assert.Equal(1, counts["arenas"]);
        Assert.Equal(2, counts["contracts"]);
        Assert.Equal(1, counts["fixtures"]);
        Assert.Equal(1, counts["matches"]);
        Assert.Equal(2, counts["standings"]);
    }

    [Fact]
    public async Task Box_score_and_standing_values_round_trip()
    {
        await using var db = await TestDatabase.CreateAsync();
        var world = await SampleWorld.SeedAsync(db);

        var stats = await db.ReadAsync(s => s.Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>().FindAsync(world.PlayerStats.Id));
        var standing = Assert.Single(await db.ReadAsync(s =>
            s.Repo<LeagueStanding, LeagueStandingId>().ListAsync(x => x.ClubId == world.HomeClub.Id)));

        Assert.Equal(25, stats!.BoxScore.Points);
        Assert.Equal(32, stats.BoxScore.Minutes);
        Assert.Equal((1, 0, 88, 81, 2), (standing.Won, standing.Lost, standing.PointsFor, standing.PointsAgainst, standing.StandingPoints));
    }

    [Fact]
    public async Task Club_participates_in_multiple_competitions()
    {
        await using var db = await TestDatabase.CreateAsync();
        var world = await SampleWorld.SeedAsync(db);

        var entries = await db.ReadAsync(s => s.Repo<CompetitionParticipant, CompetitionParticipantId>()
            .ListAsync(p => p.ClubId == world.HomeClub.Id && p.SeasonId == world.Season.Id));

        Assert.Equal(
            new[] { world.Championship.Id, world.Cup.Id }.Select(id => id.Value).Order(),
            entries.Select(e => e.CompetitionId.Value).Order());
    }

    [Fact]
    public async Task Updates_are_saved()
    {
        await using var db = await TestDatabase.CreateAsync();
        var world = await SampleWorld.SeedAsync(db);

        await db.WriteAsync(s =>
        {
            var club = s.Repo<Club, ClubId>().FindAsync(world.AwayClub.Id).GetAwaiter().GetResult()!;
            club.ChangeHomeArena(world.Arena.Id);
        });

        var reloaded = await db.ReadAsync(s => s.Repo<Club, ClubId>().FindAsync(world.AwayClub.Id));
        Assert.Equal(world.Arena.Id, reloaded!.HomeArenaId);
    }
}
