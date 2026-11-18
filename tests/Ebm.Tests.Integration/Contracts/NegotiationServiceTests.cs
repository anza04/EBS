using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Services.Contracts;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Contracts;

/// <summary>Step 25: rounds of talks, agents, competing offers and playing-time promises through <see cref="IContractService"/>.</summary>
public sealed class NegotiationServiceTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private readonly TestClock _clock = new(Today);
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private IContractService Contracts => _db.Services.GetRequiredService<IContractService>();

    private ClubId Club => _world.AwayClub.Id;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(_clock);
        _world = await SampleWorld.SeedAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Player> FreeAgentAsync(int rating = 60, int reputation = 1)
    {
        var player = new Player(PlayerId.New(), "Free", $"Agent{Guid.NewGuid():N}"[..20], new DateOnly(1998, 2, 1), _world.Italy.Id, 198, 95, Position.SmallForward, PlayerAttributes.Uniform(rating));
        player.UpdateProfile(player.WingspanCm, player.SecondaryPositions, player.Experience, reputation, player.Potential);
        await _db.WriteAsync(s => s.Repo<Player, PlayerId>().Add(player));
        return player;
    }

    private ContractTerms Terms(long salary) => new(_clock.Today, _clock.Today.AddYears(2).AddDays(-1), salary);

    private async Task<ContractTerms> AcceptableAsync(PlayerId player) =>
        ContractNegotiator.Default.Meet(Terms(0), (await Contracts.GetNegotiationAsync(player, Club)).Demands);

    [Fact]
    public async Task Asking_for_demands_is_free_but_every_offer_is_a_round()
    {
        var player = await FreeAgentAsync();
        var before = await Contracts.GetNegotiationAsync(player.Id, Club);
        Assert.Equal(0, before.Talks.Round);
        Assert.Equal(4, before.Talks.Patience);
        Assert.Null(before.Talks.Agent); // reputation 1: he negotiates himself

        var answer = await Contracts.NegotiateAsync(player.Id, Club, Terms(before.Demands.AskingSalary * 9 / 10));
        Assert.Equal(NegotiationOutcome.CounterOffer, answer.Outcome);
        Assert.Equal((1, 3), (answer.Talks!.Round, answer.Talks.Patience));
        Assert.Equal((1, 3), ((await Contracts.GetNegotiationAsync(player.Id, Club)).Talks.Round, (await Contracts.GetNegotiationAsync(player.Id, Club)).Talks.Patience));
    }

    [Fact]
    public async Task A_player_out_of_patience_breaks_off_and_refuses_to_sign_for_thirty_days()
    {
        var player = await FreeAgentAsync();
        var terms = await AcceptableAsync(player.Id);

        Assert.Equal(NegotiationOutcome.Rejected, (await Contracts.NegotiateAsync(player.Id, Club, Terms(15_000))).Outcome);
        var last = await Contracts.NegotiateAsync(player.Id, Club, Terms(15_000));
        Assert.Equal(NegotiationOutcome.Rejected, last.Outcome);
        Assert.Contains("breaks off the talks", last.Message, StringComparison.Ordinal);
        Assert.Equal(Today.AddDays(30), last.Talks!.BrokenOffUntil);

        var refused = await Contracts.NegotiateAsync(player.Id, Club, terms);
        Assert.Equal(NegotiationOutcome.Rejected, refused.Outcome);
        Assert.Contains("refuses to negotiate", refused.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<DomainException>(() => Contracts.SignAsync(player.Id, Club, terms));

        // Other clubs can still talk to him; after the cooling-off period, so can this one.
        Assert.True((await Contracts.NegotiateAsync(player.Id, _world.HomeClub.Id, await MeetAsync(player.Id, _world.HomeClub.Id))).IsAccepted);
        _clock.Today = Today.AddDays(30);
        await Contracts.SignAsync(player.Id, Club, await AcceptableAsync(player.Id));
    }

    [Fact]
    public async Task Agents_remember_broken_off_talks_and_signed_deals()
    {
        var player = await FreeAgentAsync(reputation: 40);
        var talks = await Contracts.GetNegotiationAsync(player.Id, Club);
        Assert.NotNull(talks.Talks.Agent);
        for (var i = 0; i < talks.Talks.Patience; i++)
        {
            await Contracts.NegotiateAsync(player.Id, Club, Terms(15_000));
        }

        var after = await Contracts.GetNegotiationAsync(player.Id, Club);
        Assert.Equal(AgentPool.TalksBrokenOff, after.Talks.Relationship);
        Assert.NotNull(after.Talks.BrokenOffUntil);

        // The same agent's other clients see the same relationship with the club.
        var other = await ClientOfAsync(talks.Talks.Agent!);
        Assert.Equal(AgentPool.TalksBrokenOff, (await Contracts.GetNegotiationAsync(other.Id, Club)).Talks.Relationship);
        await Contracts.SignAsync(other.Id, Club, await AcceptableAsync(other.Id));
        Assert.Equal(AgentPool.TalksBrokenOff + AgentPool.DealSigned, (await Contracts.GetNegotiationAsync(player.Id, Club)).Talks.Relationship);
    }

    [Fact]
    public async Task Competing_offers_raise_what_a_free_agent_asks()
    {
        var player = await FreeAgentAsync(reputation: 30);
        var alone = (await Contracts.GetNegotiationAsync(player.Id, Club)).Demands;
        Assert.Equal(alone.MarketSalary, alone.AskingSalary);

        await _db.WriteAsync(s =>
        {
            for (var i = 0; i < 4; i++)
            {
                var rival = new Club(ClubId.New(), $"Rival {i}", $"RV{i}", _world.Italy.Id);
                rival.UpdateProfile(null, 40 + i, 0, 50_000_000, BoardExpectation.ReachPlayoffs);
                s.Repo<Club, ClubId>().Add(rival);
            }
        });

        var contested = await Contracts.GetNegotiationAsync(player.Id, Club);
        Assert.Equal(["Rival 3", "Rival 2", "Rival 1"], contested.Talks.CompetingOffers.Select(o => o.Club));
        Assert.Equal((long)Math.Round(alone.MarketSalary * 1.12 / 1000, MidpointRounding.AwayFromZero) * 1000, contested.Demands.AskingSalary);
    }

    [Fact]
    public async Task A_star_insists_on_his_role_and_signs_the_promise()
    {
        var star = await FreeAgentAsync(rating: 85, reputation: 70);
        var demands = (await Contracts.GetNegotiationAsync(star.Id, Club)).Demands;
        Assert.Equal(SquadRole.Star, demands.ExpectedRole);
        Assert.Equal(SquadRole.Star, demands.RequiredRole);

        var answer = await Contracts.NegotiateAsync(star.Id, Club, Terms(demands.AskingSalary * 2));
        Assert.Equal(NegotiationOutcome.CounterOffer, answer.Outcome);
        await Assert.ThrowsAsync<DomainException>(() => Contracts.SignAsync(star.Id, Club, Terms(demands.AskingSalary * 2)));

        var id = await Contracts.SignAsync(star.Id, Club, answer.CounterOffer!);
        var contract = await _db.ReadAsync(async s => (await s.Repo<Contract, ContractId>().FindAsync(id))!);
        Assert.Equal(SquadRole.Star, contract.PromisedRole);
        Assert.Equal(PromiseStatus.None, contract.PromiseStatus);
    }

    [Fact]
    public async Task Promises_are_reviewed_every_four_weeks_against_minutes_played()
    {
        var benched = await FreeAgentAsync(reputation: 40);
        var used = await FreeAgentAsync();
        var promise = new ContractPromises(SquadRole.Starter); // 25 minutes a game
        var benchedId = await Contracts.SignAsync(benched.Id, Club, await AcceptableAsync(benched.Id) with { Promises = promise });
        var usedId = await Contracts.SignAsync(used.Id, Club, await AcceptableAsync(used.Id) with { Promises = promise });
        Assert.Equal(PromiseReview.None, await Contracts.ReviewPromisesAsync()); // nothing due yet

        // Three games in the four weeks: 10 minutes a game for one player, 24 for the other.
        await _db.WriteAsync(s =>
        {
            for (var game = 0; game < 3; game++)
            {
                var fixture = new Fixture(FixtureId.New(), _world.Championship.Id, _world.Season.Id, 20 + game, Today.AddDays(7 * (game + 1)), _world.HomeClub.Id, Club);
                var match = new Match(MatchId.New(), fixture.Id, 80, 75);
                s.Repo<Fixture, FixtureId>().Add(fixture);
                s.Repo<Match, MatchId>().Add(match);
                s.Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>().Add(new(PlayerMatchStatisticsId.New(), match.Id, benched.Id, Club, new BoxScore { Minutes = 10 }));
                s.Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>().Add(new(PlayerMatchStatisticsId.New(), match.Id, used.Id, Club, new BoxScore { Minutes = 24 }));
            }
        });

        _clock.Today = Today.AddDays(PromiseRules.ReviewDays);
        Assert.Equal(new PromiseReview(1, 0, 1), await Contracts.ReviewPromisesAsync());
        Assert.Equal(PromiseReview.None, await Contracts.ReviewPromisesAsync()); // reviewed today

        var (b, u, bc, uc) = await _db.ReadAsync(async s => (
            (await s.Repo<Player, PlayerId>().FindAsync(benched.Id))!,
            (await s.Repo<Player, PlayerId>().FindAsync(used.Id))!,
            (await s.Repo<Contract, ContractId>().FindAsync(benchedId))!,
            (await s.Repo<Contract, ContractId>().FindAsync(usedId))!));
        Assert.Equal(Player.DefaultMorale + PromiseRules.BrokenMorale, b.Morale);
        Assert.Equal(Player.DefaultMorale + PromiseRules.KeptMorale, u.Morale);
        Assert.Equal(PromiseStatus.Broken, bc.PromiseStatus);
        Assert.Equal(PromiseStatus.Kept, uc.PromiseStatus);

        // The broken promise costs the club with his agent, and he asks more to renew.
        var renewal = await Contracts.GetNegotiationAsync(benched.Id, Club, benchedId);
        Assert.Equal(AgentPool.DealSigned + AgentPool.PromiseBroken, renewal.Talks.Relationship);
        Assert.True(renewal.Demands.AskingSalary > renewal.Demands.MarketSalary);
    }

    private async Task<ContractTerms> MeetAsync(PlayerId player, ClubId club) =>
        ContractNegotiator.Default.Meet(Terms(0), (await Contracts.GetNegotiationAsync(player, club)).Demands);

    /// <summary>A new free agent represented by <paramref name="agent"/>.</summary>
    private async Task<Player> ClientOfAsync(string agent)
    {
        for (var i = 0; i < 500; i++)
        {
            var candidate = await FreeAgentAsync(reputation: 40);
            if ((await Contracts.GetNegotiationAsync(candidate.Id, Club)).Talks.Agent == agent)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No client found.");
    }
}
