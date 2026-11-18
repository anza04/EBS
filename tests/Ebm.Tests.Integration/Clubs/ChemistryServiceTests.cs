using Ebm.Domain.Chemistry;
using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Services.Chemistry;
using Ebm.Services.Contracts;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Clubs;

/// <summary>Step 26: weekly morale, relationships and conflicts and the dressing room through <see cref="IChemistryService"/>.</summary>
public sealed class ChemistryServiceTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 11, 9); // a Monday

    private readonly TestClock _clock = new(Today);
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private IChemistryService Chemistry => _db.Services.GetRequiredService<IChemistryService>();

    private ClubId Club => _world.AwayClub.Id;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(_clock);
        _world = await SampleWorld.SeedAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task A_week_changes_morale_relationships_and_reports_conflicts()
    {
        var contracts = _db.Services.GetRequiredService<IContractService>();
        var star = await SignAsync(contracts, 80, morale: 33);
        var others = new[] { await SignAsync(contracts, 65), await SignAsync(contracts, 64), await SignAsync(contracts, 63) };
        Assert.Equal(ChemistryWeek.None, await Chemistry.ProcessWeekAsync()); // no games yet

        // Three lost games last week: the star sits (5 minutes), the others play 30.
        await _db.WriteAsync(s =>
        {
            for (var game = 1; game <= 3; game++)
            {
                var fixture = new Fixture(FixtureId.New(), _world.Championship.Id, _world.Season.Id, 20 + game, Today.AddDays(-game), _world.HomeClub.Id, Club);
                var match = new Match(MatchId.New(), fixture.Id, 80, 75);
                s.Repo<Fixture, FixtureId>().Add(fixture);
                s.Repo<Match, MatchId>().Add(match);
                s.Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>().Add(new(PlayerMatchStatisticsId.New(), match.Id, star.Id, Club, new BoxScore { Minutes = 5 }));
                foreach (var other in others)
                {
                    s.Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>().Add(new(PlayerMatchStatisticsId.New(), match.Id, other.Id, Club, new BoxScore { Minutes = 30 }));
                }
            }
        });

        var room = (await Chemistry.GetClubChemistryAsync(Club))!;
        var top = room.Members[0];
        Assert.Equal((star.Id, 1, SquadRole.Star, 30, 5.0, RoleSatisfaction.Unhappy), (top.Place.Player.Id, top.Place.Rank, top.Place.Role, top.Place.ExpectedMinutes, top.RecentMinutes, top.Satisfaction));
        Assert.All(room.Members.Skip(1), m => Assert.Equal(RoleSatisfaction.Happy, m.Satisfaction));

        var week = await Chemistry.ProcessWeekAsync();
        Assert.Equal(2, week.Clubs); // both sides of the three games
        var conflict = Assert.Single(week.Conflicts);
        Assert.Equal((Club, star.Id), (conflict.ClubId, conflict.PlayerId));
        Assert.Contains(conflict.Rival, others.Select(o => o.FullName)); // a teammate at his position playing ahead of him
        var morale = await _db.ReadAsync(async s => (await s.Repo<Player, PlayerId>().ListAsync()).ToDictionary(p => p.Id, p => p.Morale));
        Assert.Equal(33 - 2 - 3, morale[star.Id]); // three losses, unhappy star
        Assert.All(others, o => Assert.Equal(Player.DefaultMorale - 2 + 1, morale[o.Id])); // three losses, happy with minutes

        var relationships = await _db.ReadAsync(s => s.Repo<PlayerRelationship, PlayerRelationshipId>().ListAsync());
        Assert.Equal(4, relationships.Count); // three pairs of teammates on court, one conflict
        Assert.All(relationships.Where(r => r.PlayerAId != star.Id && r.PlayerBId != star.Id), r => Assert.Equal(4, r.Score)); // 3+ games, both Italian
        Assert.Equal(-6, Assert.Single(relationships, r => r.PlayerAId == star.Id || r.PlayerBId == star.Id).Score);

        room = (await Chemistry.GetClubChemistryAsync(Club))!;
        Assert.True(room.Members[0].InConflict);
        Assert.Contains(room.Relationships, r => r.Score == -6);
        Assert.InRange(room.Chemistry.Score, -1, 1);
    }

    private async Task<Player> SignAsync(IContractService contracts, int rating, int morale = Player.DefaultMorale)
    {
        var player = new Player(PlayerId.New(), "Team", $"Mate{Guid.NewGuid():N}"[..20], new DateOnly(1998, 2, 1), _world.Italy.Id, 198, 95, Position.SmallForward, PlayerAttributes.Uniform(rating));
        player.UpdateCondition(player.Fitness, morale, InjuryStatus.Healthy, null);
        await _db.WriteAsync(s => s.Repo<Player, PlayerId>().Add(player));
        var terms = ContractNegotiator.Default.Meet(
            new ContractTerms(Today, Today.AddYears(2).AddDays(-1), 0),
            (await contracts.GetNegotiationAsync(player.Id, Club)).Demands);
        await contracts.SignAsync(player.Id, Club, terms with { Promises = null });
        return player;
    }
}
