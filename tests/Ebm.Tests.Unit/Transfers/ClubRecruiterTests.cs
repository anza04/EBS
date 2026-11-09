using Ebm.Domain.Clubs;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Transfers;

namespace Ebm.Tests.Unit.Transfers;

public sealed class ClubRecruiterTests
{
    private static readonly ClubRecruiter Recruiter = ClubRecruiter.Default;
    private static readonly ClubId Club = ClubId.New();
    private static readonly ClubId Other = ClubId.New();

    private static SquadMember Member(Position position, int overall = 60, long salary = 100_000, RosterRole role = RosterRole.Bench, bool foreign = false, int age = 27) =>
        new(PlayerId.New(), $"{position} {overall}", position, PositionSet.Empty, overall, age, salary, role, foreign);

    private static Candidate Free(Position position, int overall, long asking = 100_000, bool foreign = false, int age = 27) =>
        new(PlayerId.New(), $"Free {position} {overall}", position, PositionSet.Empty, overall, age, asking, 0, null, foreign);

    private static Candidate Paid(Position position, int overall, long fee, long asking = 100_000) =>
        new(PlayerId.New(), $"Paid {position} {overall}", position, PositionSet.Empty, overall, 27, asking, fee, Other, false);

    /// <summary>Two players per position: a complete, balanced ten-man squad.</summary>
    private static List<SquadMember> BalancedTen() =>
        [.. Enum.GetValues<Position>().SelectMany(p => new[] { Member(p, 65, role: RosterRole.Starter), Member(p, 55) })];

    private static ClubSituation Situation(IReadOnlyList<SquadMember> team, long balance = 1_000_000, long budget = 2_000_000, SquadRules? rules = null) =>
        new(Club, balance, budget, team.Sum(m => m.Salary), team, rules ?? SquadRules.Default);

    [Fact]
    public void A_short_squad_signs_the_best_affordable_players_and_prefers_free_agents()
    {
        var team = BalancedTen();
        var situation = Situation(team);
        var candidates = new[] { Free(Position.Center, 70), Paid(Position.Center, 73, 500_000), Free(Position.PointGuard, 50) };

        var plan = Recruiter.Plan(situation, candidates);

        Assert.Equal(3, plan.Needs.Count); // 10 players, target 13
        Assert.All(plan.Needs, n => Assert.Null(n.Position));
        Assert.Equal(["Free Center 70", "Paid Center 73", "Free PointGuard 50"], plan.Signings.Select(s => s.Candidate.Name)); // free agent first: within 5 points of the paid player
        Assert.Empty(plan.Unmet);
        Assert.Equal(2, plan.Signings[0].Years);
    }

    [Fact]
    public void A_clearly_better_player_is_bought_despite_the_fee()
    {
        var plan = Recruiter.Plan(Situation(BalancedTen()), [Free(Position.Center, 60), Paid(Position.Center, 70, 500_000)]);

        Assert.Equal("Paid Center 70", plan.Signings[0].Candidate.Name);
        Assert.Equal("Free Center 60", plan.Signings[1].Candidate.Name);
    }

    [Fact]
    public void Position_holes_are_filled_first()
    {
        var team = BalancedTen().Where(m => m.Position != Position.Center).ToList(); // 8 players, nobody at center
        var plan = Recruiter.Plan(Situation(team), [Free(Position.PointGuard, 80), Free(Position.Center, 60), Free(Position.Center, 58), Free(Position.ShootingGuard, 75)]);

        Assert.Equal(Position.Center, plan.Needs[0].Position);
        Assert.Equal(Position.Center, plan.Needs[1].Position);
        Assert.Contains("nobody can play Center", plan.Needs[0].Reason, StringComparison.Ordinal);
        Assert.Equal(["Free Center 60", "Free Center 58", "Free PointGuard 80", "Free ShootingGuard 75"], plan.Signings.Select(s => s.Candidate.Name));
    }

    [Fact]
    public void Financial_and_registration_limits_are_respected()
    {
        var team = BalancedTen();
        var tight = Situation(team, balance: 100_000, budget: team.Sum(m => m.Salary) + 150_000, rules: new SquadRules { MaxForeignPlayers = 1, MaxPlayers = 12 });
        var candidates = new[]
        {
            Paid(Position.Center, 90, 500_000), // too expensive
            Free(Position.Center, 80, asking: 200_000), // over the wage budget
            Free(Position.Center, 70, asking: 100_000, foreign: true), // one foreigner fits...
            Free(Position.Center, 69, asking: 20_000, foreign: true), // ...the second does not
            Free(Position.Center, 50, asking: 50_000), // fits the remaining budget
            Free(Position.PointGuard, 50, asking: 1_000), // no room: squad limit reached
        };

        var plan = Recruiter.Plan(tight, candidates);

        Assert.Equal(2, Recruiter.TargetPlayers(tight.Rules) - team.Count); // target is min(MaxPlayers, matchday + 1) = 12
        Assert.Equal(["Free Center 70", "Free PointGuard 50"], plan.Signings.Select(s => s.Candidate.Name)); // equal rating: the cheaper one
        Assert.Empty(plan.Unmet);
    }

    [Fact]
    public void Surplus_players_are_sold_weakest_first_then_foreigners_then_the_most_expensive()
    {
        var team = BalancedTen();
        team.AddRange(
        [
            Member(Position.Center, 40, salary: 50_000, role: RosterRole.Inactive), // over the limit: weakest
            Member(Position.Center, 42, salary: 60_000, role: RosterRole.Inactive),
            Member(Position.Center, 44, salary: 70_000, role: RosterRole.Inactive),
            Member(Position.Center, 46, salary: 80_000, role: RosterRole.Inactive),
            Member(Position.Center, 48, salary: 90_000, role: RosterRole.Inactive),
            Member(Position.Center, 50, salary: 100_000, role: RosterRole.Inactive),
            Member(Position.Center, 52, salary: 110_000, role: RosterRole.Inactive),
            Member(Position.Center, 54, salary: 120_000, role: RosterRole.Inactive),
            Member(Position.Center, 70, salary: 900_000, foreign: true), // too many foreigners
            Member(Position.Center, 72, salary: 950_000, foreign: true),
        ]);
        var situation = Situation(team, budget: 1_500_000, rules: new SquadRules { MaxForeignPlayers = 1 });

        var sales = Recruiter.Surplus(situation);

        Assert.Equal("Center 40", sales[0].Player.Name);
        Assert.Equal("Center 42", sales[1].Player.Name);
        Assert.Contains("squad over the limit", sales[0].Reason, StringComparison.Ordinal);
        Assert.Equal("Center 70", sales[2].Player.Name); // weakest foreigner
        Assert.Contains("foreign", sales[2].Reason, StringComparison.Ordinal);
        Assert.Equal("Center 72", sales[3].Player.Name); // most expensive non-starter while over budget
        Assert.Contains("wage bill", sales[3].Reason, StringComparison.Ordinal);
        var remaining = team.Except(sales.Select(s => s.Player)).ToList();
        Assert.True(remaining.Sum(m => m.Salary) <= 1_500_000 || remaining.Count <= SquadRules.Default.MinPlayers);
        Assert.Empty(Recruiter.Needs(situation, sales));
    }

    [Fact]
    public void The_same_situation_always_gives_the_same_plan()
    {
        var team = BalancedTen();
        var candidates = Enumerable.Range(0, 20).Select(i => Free((Position)(i % 5), 50 + i, asking: 50_000 + (i * 1_000))).ToList();

        var first = Recruiter.Plan(Situation(team), candidates);
        var second = Recruiter.Plan(Situation(team), candidates);

        Assert.Equal(first.Signings.Select(s => s.Candidate.PlayerId), second.Signings.Select(s => s.Candidate.PlayerId));
        Assert.Equal(3, first.Signings.Count); // 10 players, target 13
        Assert.Equal([69, 68, 67], first.Signings.Select(s => s.Candidate.Overall)); // best first
    }
}
