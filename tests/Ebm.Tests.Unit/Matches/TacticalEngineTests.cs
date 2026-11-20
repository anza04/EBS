using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Tactics;
using Xunit.Abstractions;

namespace Ebm.Tests.Unit.Matches;

/// <summary>Step 27: every advanced tactical instruction has a measurable effect (docs/TACTICS.md §7).</summary>
public sealed class TacticalEngineTests(ITestOutputHelper output)
{
    private const int Games = 150;

    private static TeamTactics Tactics(
        SituationSettings? situations = null,
        DefensiveSystem defense = DefensiveSystem.ManToMan,
        DefensiveSettings? defensive = null,
        OffensiveSystem offense = OffensiveSystem.MotionOffense,
        IReadOnlyDictionary<PlayerId, TacticalAssignment>? assignments = null) =>
        new(offense, OffensiveSettings.Default, defense, defensive ?? DefensiveSettings.Default, assignments ?? new Dictionary<PlayerId, TacticalAssignment>())
        {
            Situations = situations ?? SituationSettings.Default,
        };

    private static void Set(SheetPlayer player, params (PlayerAttribute Attribute, int Value)[] values)
    {
        var attributes = player.Player.Attributes;
        foreach (var (attribute, value) in values)
        {
            attributes = attributes.With(attribute, value);
        }

        player.Player.UpdateAttributes(attributes);
    }

    private static List<MatchSimulation> Play(int games, Func<int, (TeamSheet Home, TeamSheet Away)> teams) =>
    [
        .. Enumerable.Range(0, games).Select(i =>
        {
            var (home, away) = teams(i);
            var sim = new MatchSimulation(new MatchSetup(home, away, i));
            sim.RunToEnd();
            return sim;
        }),
    ];

    private static (TeamSheet Home, TeamSheet Away) Pair(int i, TeamTactics? home = null, TeamTactics? away = null, int homeQuality = 60, int awayQuality = 60) =>
        (SimulationTeams.Team("Home", homeQuality, i * 2, home), SimulationTeams.Team("Away", awayQuality, (i * 2) + 1, away));

    private static double Average(IEnumerable<MatchSimulation> games, TeamSide side, Func<IReadOnlyList<PlayerLine>, double> stat) =>
        games.Average(g => stat(g.Lines(side)));

    [Fact]
    public void A_defender_assigned_to_the_best_scorer_cuts_his_scoring()
    {
        // The away shooting guard is a star; the home shooting guard cannot defend, the home point guard is elite.
        (TeamSheet, TeamSheet) Teams(int i, DefensiveMatchup matchup)
        {
            var home = SimulationTeams.Team("Home", 60, i * 2);
            var away = SimulationTeams.Team("Away", 60, (i * 2) + 1);
            Set(away.Players[1], (PlayerAttribute.ThreePoint, 85), (PlayerAttribute.MidRange, 85));
            Set(home.Players[1], (PlayerAttribute.PerimeterDefense, 25), (PlayerAttribute.Agility, 30));
            Set(home.Players[0], (PlayerAttribute.PerimeterDefense, 95), (PlayerAttribute.Agility, 90));
            var star = away.Players[1].Player;
            var stopper = home.Players[0].Player;
            var awayTactics = Tactics(assignments: new Dictionary<PlayerId, TacticalAssignment> { [star.Id] = new(star.Id, TacticalRole.ShotCreator, 5) });
            var homeTactics = Tactics(assignments: new Dictionary<PlayerId, TacticalAssignment> { [stopper.Id] = new(stopper.Id, TacticalRole.PrimaryBallHandler, 3, null, matchup) });
            return (home with { Tactics = homeTactics }, away with { Tactics = awayTactics });
        }

        double StarPoints(DefensiveMatchup matchup) => Play(Games, i => Teams(i, matchup)).Average(g => g.Lines(TeamSide.Away)[1].Points);
        var positional = StarPoints(DefensiveMatchup.Positional);
        var assigned = StarPoints(DefensiveMatchup.BestScorer);
        output.WriteLine($"Star points: positional {positional:F1}, stopper {assigned:F1}");
        Assert.True(assigned < positional - 1.5, $"{assigned:F1} vs {positional:F1}");
    }

    [Fact]
    public void Matchup_hunting_feeds_the_player_guarded_by_the_weakest_defender()
    {
        (TeamSheet, TeamSheet) Teams(int i, MatchupTargeting targeting)
        {
            var (home, away) = Pair(i, away: Tactics(SituationSettings.Default with { MatchupTargeting = targeting }));
            Set(home.Players[2], (PlayerAttribute.PerimeterDefense, 15), (PlayerAttribute.InteriorDefense, 15));
            return (home, away);
        }

        double Share(MatchupTargeting targeting) => Play(Games, i => Teams(i, targeting)).Average(g =>
        {
            var lines = g.Lines(TeamSide.Away);
            return (lines[2].TwoPointAttempted + lines[2].ThreePointAttempted) / (double)lines.Sum(l => l.TwoPointAttempted + l.ThreePointAttempted);
        });
        var off = Share(MatchupTargeting.Off);
        var hunting = Share(MatchupTargeting.WeakestDefender);
        output.WriteLine($"Hunted player's shot share: off {off:P1}, hunting {hunting:P1}");
        Assert.True(hunting > off * 1.2, $"{hunting:P1} vs {off:P1}");
    }

    [Fact]
    public void Pick_and_roll_coverages_change_where_the_offense_scores()
    {
        double Pct(PickAndRollCoverage coverage, bool three) => Play(Games, i => Pair(
            i,
            home: Tactics(defensive: DefensiveSettings.Default with { PickAndRollCoverage = coverage, Switching = SwitchingPolicy.Never }),
            away: Tactics(offense: OffensiveSystem.PickAndRoll))).Select(g => g.Result().Statistics(TeamSide.Away)).Average(s => three ? s.ThreePointPercentage : s.TwoPointPercentage);
        var (iceThree, underThree) = (Pct(PickAndRollCoverage.Ice, true), Pct(PickAndRollCoverage.Under, true));
        var (iceTwo, underTwo) = (Pct(PickAndRollCoverage.Ice, false), Pct(PickAndRollCoverage.Under, false));
        output.WriteLine($"3P%: ice {iceThree:P1}, under {underThree:P1}; 2P%: ice {iceTwo:P1}, under {underTwo:P1}");
        Assert.True(underThree > iceThree + 0.02);
    }

    [Fact]
    public void Switching_everything_trades_the_coverage_edge_for_size_mismatches()
    {
        // A blitz gives the pick and roll open threes; switching takes the screen away and creates mismatches instead.
        List<MatchSimulation> Run(SwitchingPolicy policy) => Play(Games, i => Pair(
            i,
            home: Tactics(defensive: DefensiveSettings.Default with { Switching = policy, PickAndRollCoverage = PickAndRollCoverage.Blitz }),
            away: Tactics(offense: OffensiveSystem.PickAndRoll)));
        double PostShots(List<MatchSimulation> g) => g.Average(s => s.Events.Count(e => e.Side == TeamSide.Away && e.Kind is MatchEventKind.FieldGoalMade or MatchEventKind.FieldGoalMissed && e.Text.Contains("in the post", StringComparison.Ordinal)));
        double ThreePct(List<MatchSimulation> g) => g.Average(s => s.Result().Statistics(TeamSide.Away).ThreePointPercentage);
        var never = Run(SwitchingPolicy.Never);
        var everything = Run(SwitchingPolicy.Everything);
        output.WriteLine($"Against a blitz: post shots never {PostShots(never):F1} / switch everything {PostShots(everything):F1}; 3P% {ThreePct(never):P1} / {ThreePct(everything):P1}");
        Assert.True(PostShots(everything) > PostShots(never));
        Assert.True(ThreePct(everything) < ThreePct(never) - 0.01);
    }

    [Fact]
    public void Set_plays_slow_the_game_and_cut_turnovers_and_the_hot_hand_concentrates_shots()
    {
        List<MatchSimulation> Run(PlayCalling calling) => Play(Games, i => Pair(i, Tactics(SituationSettings.Default with { PlayCalling = calling }), Tactics(SituationSettings.Default with { PlayCalling = calling })));
        var flow = Run(PlayCalling.Flow);
        var sets = Run(PlayCalling.SetPlays);
        var hot = Run(PlayCalling.HotHand);
        double Possessions(List<MatchSimulation> g) => g.Average(s => s.Result().Possessions);
        double Turnovers(List<MatchSimulation> g) => Average(g, TeamSide.Home, l => l.Sum(x => x.Turnovers));
        double TopShare(List<MatchSimulation> g) => Average(g, TeamSide.Home, l => l.Max(x => x.TwoPointAttempted + x.ThreePointAttempted) / (double)l.Sum(x => x.TwoPointAttempted + x.ThreePointAttempted));
        output.WriteLine($"Possessions flow {Possessions(flow):F1} / sets {Possessions(sets):F1}; turnovers {Turnovers(flow):F1} / {Turnovers(sets):F1}; top shooter share {TopShare(flow):P1} / hot hand {TopShare(hot):P1}");
        Assert.True(Possessions(sets) < Possessions(flow) - 2);
        Assert.True(Turnovers(sets) < Turnovers(flow));
        Assert.True(TopShare(hot) > TopShare(flow) + 0.01);
    }

    [Fact]
    public void Zone_offenses_attack_the_zone_where_they_promise()
    {
        double ThreeShare(ZoneOffense attack) => Play(Games, i => Pair(i, Tactics(defense: DefensiveSystem.Zone23), Tactics(SituationSettings.Default with { ZoneOffense = attack })))
            .Select(g => g.Result().Statistics(TeamSide.Away)).Average(s => s.ThreePointAttempted / (double)(s.ThreePointAttempted + s.TwoPointAttempted));
        var standard = ThreeShare(ZoneOffense.Standard);
        var overload = ThreeShare(ZoneOffense.Overload);
        var gaps = ThreeShare(ZoneOffense.AttackGaps);
        output.WriteLine($"Three share against a 2-3 zone: standard {standard:P1}, overload {overload:P1}, attack gaps {gaps:P1}");
        Assert.True(overload > standard + 0.02);
        Assert.True(gaps < standard);
    }

    [Fact]
    public void Two_for_one_gets_more_possessions_than_holding_for_the_last_shot()
    {
        List<MatchSimulation> Run(EndOfQuarter plan) => Play(Games, i => Pair(i, Tactics(SituationSettings.Default with { EndOfQuarter = plan }), Tactics(SituationSettings.Default with { EndOfQuarter = plan })));
        var twoForOne = Run(EndOfQuarter.TwoForOne);
        var hold = Run(EndOfQuarter.HoldForLastShot);
        double Possessions(List<MatchSimulation> g) => g.Average(s => s.Result().Possessions);
        output.WriteLine($"Possessions: two-for-one {Possessions(twoForOne):F1}, hold {Possessions(hold):F1}");
        Assert.True(Possessions(twoForOne) > Possessions(hold) + 1);
        Assert.True(twoForOne.Take(20).Count(g => g.TacticalNotes.Any(n => n.Text.Contains("two-for-ones", StringComparison.Ordinal))) >= 18);
        Assert.Contains(hold.SelectMany(g => g.Events), e => e.Kind == MatchEventKind.Situation && e.Text.Contains("last shot of the quarter", StringComparison.Ordinal));
    }

    [Fact]
    public void Trailing_teams_foul_on_purpose_late_and_leaders_milk_the_clock()
    {
        var off = SituationSettings.Default with { FoulWhenTrailing = false, MilkClockWhenLeading = false, HurryWhenTrailing = false };
        List<MatchSimulation> Run(SituationSettings s) => Play(Games, i => Pair(i, Tactics(s), Tactics(s)));
        var plain = Run(off);
        var managed = Run(SituationSettings.Default);
        double LateFreeThrows(List<MatchSimulation> g) => g.Average(s => s.Events.Count(e => e.Period >= 4 && e.Clock <= 60 && e.Kind is MatchEventKind.FreeThrowMade or MatchEventKind.FreeThrowMissed));
        output.WriteLine($"Free throws in the last minute: {LateFreeThrows(plain):F2} → {LateFreeThrows(managed):F2}");
        Assert.True(LateFreeThrows(managed) > LateFreeThrows(plain) + 0.5);
        Assert.Contains(managed.SelectMany(g => g.Events), e => e.Kind == MatchEventKind.Situation && e.Text.Contains("on purpose", StringComparison.Ordinal));
        Assert.Contains(managed.SelectMany(g => g.TacticalNotes), n => n.Text.Contains("milking the clock", StringComparison.Ordinal));
        Assert.DoesNotContain(plain.SelectMany(g => g.Events), e => e.Kind == MatchEventKind.Situation && e.Text.Contains("on purpose", StringComparison.Ordinal));
    }

    [Fact]
    public void Foul_management_and_rotation_strategies_change_minutes_and_foul_outs()
    {
        List<MatchSimulation> Run(SituationSettings s) => Play(Games, i => Pair(i, Tactics(s with { RestStartersInBlowouts = false }), Tactics(s with { RestStartersInBlowouts = false })));
        double StarterMinutes(List<MatchSimulation> g) => Average(g, TeamSide.Home, l => l.Where(x => x.IsStarter).Average(x => x.SecondsPlayed / 60));
        double Used(List<MatchSimulation> g) => Average(g, TeamSide.Home, l => l.Count(x => x.SecondsPlayed > 0));
        double FouledOut(List<MatchSimulation> g) => g.Sum(s => s.Lines(TeamSide.Home).Count(x => x.FouledOut) + s.Lines(TeamSide.Away).Count(x => x.FouledOut));
        var tight = Run(SituationSettings.Default with { Rotation = RotationStrategy.Tight });
        var standard = Run(SituationSettings.Default);
        var deep = Run(SituationSettings.Default with { Rotation = RotationStrategy.Deep });
        var ignore = Run(SituationSettings.Default with { FoulManagement = FoulManagement.Ignore });
        var protect = Run(SituationSettings.Default with { FoulManagement = FoulManagement.Protect });
        output.WriteLine($"Starter minutes tight {StarterMinutes(tight):F1} / standard {StarterMinutes(standard):F1} / deep {StarterMinutes(deep):F1}; players used {Used(tight):F1} / {Used(standard):F1} / {Used(deep):F1}; foul-outs ignore {FouledOut(ignore)} / standard {FouledOut(standard)} / protect {FouledOut(protect)}");
        Assert.True(StarterMinutes(tight) > StarterMinutes(standard) + 1);
        Assert.True(StarterMinutes(deep) < StarterMinutes(standard) - 1);
        Assert.True(Used(tight) < Used(standard));
        Assert.True(FouledOut(ignore) > FouledOut(protect));
    }

    [Fact]
    public void Starters_rest_in_blowouts()
    {
        double StarterMinutes(bool rest) => Play(Games, i => Pair(i, Tactics(SituationSettings.Default with { RestStartersInBlowouts = rest }), homeQuality: 80, awayQuality: 45))
            .Average(g => g.Lines(TeamSide.Home).Where(x => x.IsStarter).Average(x => x.SecondsPlayed / 60));
        var (play, rest) = (StarterMinutes(false), StarterMinutes(true));
        output.WriteLine($"Winning starters' minutes: {play:F1} → {rest:F1} when resting in blowouts");
        Assert.True(rest < play - 1);
    }

    [Fact]
    public void The_coach_adapts_his_game_plan_to_the_opponent()
    {
        // Opponents who cannot shoot but score inside: the coach packs a zone and takes their main option.
        var (home, away) = Pair(1, Tactics(SituationSettings.Default with { CoachAdjusts = true }));
        foreach (var p in away.Players)
        {
            Set(p, (PlayerAttribute.ThreePoint, 25), (PlayerAttribute.PostMoves, 70), (PlayerAttribute.CloseShot, 70));
        }

        var sim = new MatchSimulation(new MatchSetup(home, away, 1));
        sim.Step();
        Assert.Equal(DefensiveSystem.Zone23, sim.Tactics(TeamSide.Home).DefensiveSystem);
        Assert.Contains(sim.TacticalNotes, n => n.Home && n.Text.StartsWith("Game plan: a 2-3 zone", StringComparison.Ordinal));
        Assert.Equal(DefensiveSystem.ManToMan, sim.Tactics(TeamSide.Away).DefensiveSystem);

        // A user-managed side keeps its tactic.
        var live = new MatchSimulation(new MatchSetup(home, away, 1), SimulationMode.Live);
        live.SetAutomatic(TeamSide.Home, false);
        live.Step();
        Assert.Equal(DefensiveSystem.ManToMan, live.Tactics(TeamSide.Home).DefensiveSystem);
    }

    [Fact]
    public void The_coach_presses_when_far_behind_late_and_the_notes_record_it()
    {
        var games = Play(60, i => Pair(i, Tactics(SituationSettings.Default with { CoachAdjusts = true }), homeQuality: 50, awayQuality: 70));
        var pressed = games.Count(g => g.TacticalNotes.Any(n => n.Home && n.Period >= 4 && n.Text.Contains("full-court press", StringComparison.Ordinal)));
        output.WriteLine($"Late full-court press in {pressed} of {games.Count} games");
        Assert.True(pressed >= games.Count / 2);
        Assert.All(games, g => Assert.Contains(g.Result().TacticalNotes, n => n.Text.StartsWith("Game plan", StringComparison.Ordinal)));
        Assert.Contains(games.SelectMany(g => g.Events), e => e.Kind == MatchEventKind.TacticsChanged && e.Side == TeamSide.Home);
    }
}
