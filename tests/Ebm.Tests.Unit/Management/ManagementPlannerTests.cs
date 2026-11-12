using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Management;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Domain.Tactics;

namespace Ebm.Tests.Unit.Management;

public sealed class ManagementPlannerTests
{
    private static readonly CountryId Italy = CountryId.New();
    private static readonly PlayerRatingCalculator Calculator = PlayerRatingCalculator.Default;

    private static Player NewPlayer(Position position, int level, PlayerAttributes? attributes = null) =>
        new(PlayerId.New(), "Test", $"P{Guid.NewGuid():N}"[..10], new DateOnly(2000, 1, 1), Italy, 200, 95, position, attributes ?? PlayerAttributes.Uniform(level));

    private static LineupCandidate Candidate(Player p, RosterRole role = RosterRole.Bench, PlayerAvailability availability = PlayerAvailability.Available) =>
        new(p.Id, role, availability, Calculator.Evaluate(p));

    [Fact]
    public void Lineup_puts_the_best_players_at_the_five_positions_then_rotation_bench_and_inactive()
    {
        var positions = Enum.GetValues<Position>();
        var players = Enumerable.Range(0, 15).Select(i => NewPlayer(positions[i % 5], 40 + (i * 3))).ToList();
        var injured = NewPlayer(Position.Center, 99);
        var youth = NewPlayer(Position.PointGuard, 98);
        var squad = players.Select(p => Candidate(p)).Append(Candidate(injured, RosterRole.Starter, PlayerAvailability.Unavailable)).Append(Candidate(youth, RosterRole.Youth)).ToList();

        var plan = LineupPlanner.Plan(squad);

        Assert.Equal(squad.Count, plan.Count);
        var starters = plan.Where(p => p.Role == RosterRole.Starter).ToList();
        Assert.Equal(positions.ToHashSet(), starters.Select(s => s.LineupPosition!.Value).ToHashSet());
        Assert.True(starters.Select(s => s.PlayerId).ToHashSet().SetEquals(players.TakeLast(5).Select(p => p.Id)));
        Assert.Equal(LineupPlanner.RotationPlayers, plan.Count(p => p.Role == RosterRole.Rotation));
        Assert.Equal(SquadRules.Default.MatchdayPlayers, plan.Count(p => p.Role.IsActive()));
        Assert.Equal(RosterRole.Inactive, plan.Single(p => p.PlayerId == injured.Id).Role);
        Assert.Equal(RosterRole.Youth, plan.Single(p => p.PlayerId == youth.Id).Role);
        Assert.Contains(plan.First(p => p.Role == RosterRole.Rotation).PlayerId, new[] { players[8].Id, players[9].Id }); // best non-starters first
    }

    [Fact]
    public void Tactics_follow_the_squad_strengths()
    {
        var shooters = PlayerCatalog.Attributes.ToDictionary(a => a, a => PlayerCatalog.CategoryOf(a) == AttributeCategory.Shooting ? 85 : 55);
        var squad = Enumerable.Range(0, 9).Select(i => NewPlayer((Position)(i % 5), 0, new PlayerAttributes(shooters))).Select(p => (p, Calculator.Evaluate(p))).ToList();

        var plan = TacticPlanner.Plan(squad);

        Assert.Equal(OffensiveSystem.FiveOut, plan.OffensiveSystem);
        Assert.Equal(OffensiveFocus.Perimeter, plan.Offense.Focus);
        Assert.Equal(9, plan.Assignments.Count);
        Assert.Equal(3, plan.Assignments.Count(a => a.ShotPriority == 4));

        var balanced = Enumerable.Range(0, 9).Select(i => NewPlayer((Position)(i % 5), 60)).Select(p => (p, Calculator.Evaluate(p))).ToList();
        Assert.Equal(OffensiveSystem.MotionOffense, TacticPlanner.Plan(balanced).OffensiveSystem);
        Assert.Empty(TacticPlanner.Plan([]).Assignments);
    }

    [Theory]
    [InlineData(BoardExpectation.WinTitle, 1, true, false, false, true)]
    [InlineData(BoardExpectation.WinTitle, 2, false, false, false, false)]
    [InlineData(BoardExpectation.ReachPlayoffs, 4, false, false, false, true)]
    [InlineData(BoardExpectation.ReachPlayoffs, 5, false, false, false, false)]
    [InlineData(BoardExpectation.MidTable, 6, false, false, false, true)]
    [InlineData(BoardExpectation.MidTable, 7, false, false, true, false)]
    [InlineData(BoardExpectation.AvoidRelegation, 6, false, false, false, true)]
    [InlineData(BoardExpectation.WinPromotion, 2, false, true, false, true)]
    public void Objectives_are_judged_on_the_final_table(BoardExpectation expectation, int position, bool champion, bool promoted, bool relegated, bool met) =>
        Assert.Equal(met, SeasonObjectives.IsMet(expectation, new SeasonFinish(position, 8, champion, promoted, relegated, [1, 2, 3, 4])));

    [Fact]
    public void Objectives_are_set_from_the_reputation_rank()
    {
        Assert.Equal(BoardExpectation.WinTitle, SeasonObjectives.For(1, 8, 4, 0, 2));
        Assert.Equal(BoardExpectation.ReachPlayoffs, SeasonObjectives.For(3, 8, 4, 0, 2));
        Assert.Equal(BoardExpectation.MidTable, SeasonObjectives.For(5, 8, 4, 0, 2));
        Assert.Equal(BoardExpectation.AvoidRelegation, SeasonObjectives.For(6, 8, 4, 0, 2));
        Assert.Equal(BoardExpectation.WinPromotion, SeasonObjectives.For(1, 6, 4, 2, 0));
        Assert.Equal(BoardExpectation.WinPromotion, SeasonObjectives.For(2, 6, 4, 2, 0));
    }

    [Fact]
    public void Salaries_are_due_on_the_instalment_dates_and_income_at_month_end()
    {
        var contract = Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(), new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30), 120_000);

        Assert.Equal(0, ClubFinances.SalaryDue(contract, new DateOnly(2026, 7, 30)));
        Assert.InRange(ClubFinances.SalaryDue(contract, new DateOnly(2026, 7, 31)), 9_000, 11_000);
        var year = Enumerable.Range(0, 365).Select(d => ClubFinances.SalaryDue(contract, new DateOnly(2026, 7, 1).AddDays(d))).Sum();
        Assert.Equal(120_000, year);
        Assert.True(ClubFinances.IsIncomeDay(new DateOnly(2027, 2, 28)));
        Assert.False(ClubFinances.IsIncomeDay(new DateOnly(2027, 2, 27)));
    }

    [Fact]
    public void Core_players_near_the_end_of_their_contract_are_renewed()
    {
        var today = new DateOnly(2027, 3, 1);
        Assert.True(RenewalPolicy.Wants(3, 28, new DateOnly(2027, 6, 30), today));
        Assert.False(RenewalPolicy.Wants(11, 28, new DateOnly(2027, 6, 30), today));
        Assert.False(RenewalPolicy.Wants(3, 34, new DateOnly(2027, 6, 30), today));
        Assert.False(RenewalPolicy.Wants(3, 28, new DateOnly(2028, 6, 30), today));
        Assert.Equal([3, 2, 1], new[] { 22, 29, 32 }.Select(RenewalPolicy.Years));
    }

    [Fact]
    public void Generated_players_are_young_unique_and_named_from_the_pool()
    {
        var names = new NamePool(Italy, ["Luca", "Marco"], ["Rossi", "Bianchi"]);
        var generator = new PlayerGenerator(7);
        var today = new DateOnly(2027, 6, 30);
        var keys = new HashSet<(string, string, DateOnly)>();

        var players = Enumerable.Range(0, 20).Select(_ =>
        {
            var p = generator.Create(names, generator.NextPosition(), today, (f, l, d) => keys.Contains((f, l, d)));
            keys.Add((p.FirstName, p.LastName, p.DateOfBirth));
            return p;
        }).ToList();

        Assert.Equal(20, keys.Count);
        Assert.All(players, p =>
        {
            Assert.InRange(p.AgeOn(today), PlayerGenerator.MinAge, PlayerGenerator.MaxAge);
            Assert.Contains(p.FirstName, names.FirstNames);
            Assert.Equal(Italy, p.NationalityId);
            Assert.InRange(Calculator.Evaluate(p).Overall, 25, 75);
        });
    }
}
