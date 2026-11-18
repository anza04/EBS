using Ebm.Domain.Players;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Matches.Simulation;

/// <summary>
/// Step 27: matchups, switches, situational play (end of quarters and games, intentional fouls) and the engine's
/// coach adapting the tactic (docs/TACTICS.md §7, docs/MATCH_ENGINE.md §8).
/// </summary>
public sealed partial class MatchSimulation
{
    private readonly List<TacticalNote> _notes = [];

    /// <summary>A situational clock plan for a half-court possession: its length, a forced three and a make change.</summary>
    private readonly record struct ClockPlanned(double Seconds, bool ForceThree, double Edge);

    // ---------------------------------------------------------------- matchups

    /// <summary>
    /// Who guards whom in a man-based defense: explicit matchups first (a perimeter stopper without one takes the best
    /// scorer), then everyone else by the nearest free slot.
    /// </summary>
    private static Dictionary<Runner, Runner> Guards(Team off, Team def)
    {
        var attackers = off.OnCourt.ToList();
        var free = def.OnCourt.ToList();
        var map = new Dictionary<Runner, Runner>();
        foreach (var defender in def.OnCourt)
        {
            var matchup = defender.Assignment.Matchup == DefensiveMatchup.Positional && defender.Assignment.Role == TacticalRole.PerimeterStopper
                ? DefensiveMatchup.BestScorer
                : defender.Assignment.Matchup;
            if (matchup == DefensiveMatchup.Positional
                || attackers.Where(a => !map.ContainsKey(a)).MaxBy(a => Target(off, a, matchup)) is not { } target)
            {
                continue;
            }

            map[target] = defender;
            free.Remove(defender);
        }

        foreach (var attacker in attackers.Where(a => !map.ContainsKey(a)))
        {
            var slot = off.SlotOf(attacker);
            if (free.MinBy(d => Math.Abs(def.SlotOf(d) - slot)) is not { } defender)
            {
                break;
            }

            map[attacker] = defender;
            free.Remove(defender);
        }

        return map;
    }

    private static double Target(Team off, Runner a, DefensiveMatchup matchup) => matchup switch
    {
        DefensiveMatchup.BestScorer => a.Line.Points + (8 * Usage(off, a)),
        DefensiveMatchup.BestShooter => a.A.ThreePoint,
        DefensiveMatchup.BestPlaymaker => a.A.Passing + a.A.BallHandling,
        _ => a.A.PostMoves + a.A.CloseShot + ((a.Player.HeightCm - 195) * 1.5),
    };

    private static double DefenseRating(Runner r) => (r.A.PerimeterDefense + r.A.InteriorDefense) / 2.0;

    /// <summary>Whether the offense hunts a matchup (it needs a man-based defense).</summary>
    private static bool Hunting(Team off, Team def) =>
        off.Tactics.Situations.MatchupTargeting != MatchupTargeting.Off && TacticalModel.IsManBased(def.Tactics.DefensiveSystem);

    /// <summary>The attacker the offense hunts: guarded by the weakest defender, or with the largest size advantage (6 cm+).</summary>
    private static Runner? Hunted(Team off, Dictionary<Runner, Runner>? guards)
    {
        if (guards is null)
        {
            return null;
        }

        Runner? hunted = null;
        var best = double.MinValue;
        foreach (var (attacker, defender) in guards)
        {
            var value = off.Tactics.Situations.MatchupTargeting switch
            {
                MatchupTargeting.WeakestDefender => -DefenseRating(defender),
                MatchupTargeting.SizeMismatch when attacker.Player.HeightCm - defender.Player.HeightCm >= 6 => attacker.Player.HeightCm - defender.Player.HeightCm,
                _ => double.MinValue,
            };
            if (value > best)
            {
                (hunted, best) = (attacker, value);
            }
        }

        return hunted;
    }

    /// <summary>
    /// The shooter's defender in a man-based defense. On a pick and roll the defenders of the ball handler and the
    /// screener switch (switch coverage, switch everything, or same size within 7 cm): the play loses the coverage edge
    /// for every shooter, and the handler and the screener face each other's defender.
    /// </summary>
    private static (Runner? Defender, bool Switched) ManDefender(Team off, Team def, Dictionary<Runner, Runner> guards, Runner shooter, bool pickAndRoll)
    {
        var defender = guards.GetValueOrDefault(shooter);
        if (!pickAndRoll)
        {
            return (defender, false);
        }

        var handler = BallHandler(off);
        var screener = off.OnCourt.Where(r => r != handler)
            .MaxBy(r => r.Player.HeightCm + (r.Assignment.Role == TacticalRole.RollMan ? 30 : 0));
        if (screener is null || !guards.TryGetValue(handler, out var onHandler) || !guards.TryGetValue(screener, out var onScreener))
        {
            return (defender, false);
        }

        var d = def.Tactics.Defense;
        var switches = d.PickAndRollCoverage == PickAndRollCoverage.Switch || d.Switching switch
        {
            SwitchingPolicy.Everything => true,
            SwitchingPolicy.SameSize => Math.Abs(onHandler.Player.HeightCm - onScreener.Player.HeightCm) <= 7,
            _ => false,
        };
        if (!switches)
        {
            return (defender, false);
        }

        return (shooter == handler ? onScreener : shooter == screener ? onHandler : defender, true);
    }

    /// <summary>Hot-hand play calling: +15% shot share per made field goal (less half the misses), up to +150%.</summary>
    private static double HotHand(Team off, Runner r)
    {
        if (off.Tactics.Situations.PlayCalling != PlayCalling.HotHand)
        {
            return 1;
        }

        var made = r.Line.TwoPointMade + r.Line.ThreePointMade;
        var missed = r.Line.TwoPointAttempted + r.Line.ThreePointAttempted - made;
        return 1 + (0.15 * Math.Clamp(made - (missed / 2.0), 0, 10));
    }

    // ---------------------------------------------------------------- end of quarters and games

    /// <summary>The clock plan of a half-court possession at the end of a quarter or of the game (default: none).</summary>
    private ClockPlanned ClockPlan(Team off, Team def, double shotClock)
    {
        var s = off.Tactics.Situations;
        var lead = off.Score - def.Score;
        if (Period >= _rules.Periods)
        {
            if (_clock > 120)
            {
                return default;
            }

            if (lead > 0 && s.MilkClockWhenLeading && _clock > 4)
            {
                Situation(off, "possessions milking the clock", null);
                return new ClockPlanned(Math.Min(shotClock - 0.5 - (1.5 * _random.NextDouble()), _clock), false, 0);
            }

            if (lead <= 0 && s.HurryWhenTrailing)
            {
                if (lead >= -3 && _clock <= shotClock && _clock > 4)
                {
                    var three = lead == -3;
                    Situation(off, three ? "plays for the tying three" : "last-shot plays", $"{off.Sheet.Name} hold the ball for {(three ? "a tying three" : "the last shot")}.");
                    return new ClockPlanned(Math.Max(2, _clock - 1.5 - (2 * _random.NextDouble())), three, 0);
                }

                if (lead <= -4)
                {
                    return new ClockPlanned(7 + (3 * _random.NextDouble()), false, TacticalModel.TwoForOneEdge);
                }
            }

            return default;
        }

        switch (s.EndOfQuarter)
        {
            case EndOfQuarter.TwoForOne when _clock is >= 30 and <= 44:
                Situation(off, "two-for-ones", null);
                return new ClockPlanned(Math.Max(2, _clock - 30 - (4 * _random.NextDouble())), false, TacticalModel.TwoForOneEdge);
            case EndOfQuarter.HoldForLastShot when _clock <= shotClock + 0.5 && _clock > 4:
                Situation(off, "last-shot plays", $"{off.Sheet.Name} hold for the last shot of the quarter.");
                return new ClockPlanned(Math.Max(2, _clock - 0.5 - (2 * _random.NextDouble())), false, 0);
            default:
                return default;
        }
    }

    /// <summary>
    /// A deliberate foul by the defense late in the game: trailing by 3–8 in the last minute (by 1–2 in the last 15 s
    /// when the offense could run out the clock), or leading by 3 in the last 7 s in the bonus. Returns <c>null</c> when
    /// there is none, <c>true</c> when the possession ended, <c>false</c> when play goes on (not yet in the bonus).
    /// </summary>
    private bool? IntentionalFoul(Team off, Team def, ref double shotClock)
    {
        if (Period < _rules.Periods || _clock <= 0 || IsFinished)
        {
            return null;
        }

        var s = def.Tactics.Situations;
        var deficit = off.Score - def.Score;
        var why = s.FoulWhenTrailing && deficit is >= 1 and <= 8 && ((deficit >= 3 && _clock <= 60) || (_clock <= 15 && shotClock >= _clock))
            ? "to stop the clock"
            : s.FoulUpThree && deficit == -3 && _clock <= 7 && def.PeriodFouls >= _rules.BonusFouls
                ? "before the tying three"
                : null;
        if (why is null)
        {
            return null;
        }

        var seconds = Math.Min(_clock, 1 + (2 * _random.NextDouble()));
        Elapse(seconds);
        shotClock -= seconds;
        var fouled = Pick(off.OnCourt.ToList(), r => Math.Pow(Math.Max(5, 100 - r.A.FreeThrow), 2));
        var fouler = def.OnCourt.OrderBy(r => r.Line.PersonalFouls).ThenByDescending(r => r.Priority).First();
        var bonus = def.PeriodFouls >= _rules.BonusFouls;
        Situation(def, "intentional fouls", $"{fouler.Name} fouls {fouled.Name} on purpose {why}.");
        AddFoul(def, fouler, fouled);
        if (IsFinished)
        {
            return true;
        }

        if (!bonus)
        {
            shotClock = Math.Max(shotClock, _rules.ResetShotClock);
            return _clock <= 0;
        }

        if (FreeThrows(off, fouled, 2))
        {
            ResolveRebound(off, def, ShotType.Rim, freeThrow: true);
        }
        else
        {
            ChangePossession(stolen: false);
        }

        return true;
    }

    private void Situation(Team team, string what, string? commentary)
    {
        team.Situations[what] = team.Situations.GetValueOrDefault(what) + 1;
        if (commentary is not null)
        {
            Emit(MatchEventKind.Situation, team.Side, null, null, 0, commentary);
        }
    }

    private void Note(Team team, string text) =>
        _notes.Add(new TacticalNote(Period, SecondsLeft, team.Side == TeamSide.Home, text.Length <= TacticalNote.TextMaxLength ? text : text[..TacticalNote.TextMaxLength]));

    // ---------------------------------------------------------------- the engine's coach

    /// <summary>Once per period, with four minutes left in the fourth quarter or an overtime.</summary>
    private void ClosingReview(Team team)
    {
        if (team.Tactics.Situations.CoachAdjusts && Period >= _rules.Periods && _clock <= 240 && team.ClosingReviewed != Period)
        {
            team.ClosingReviewed = Period;
            Review(team);
        }
    }

    /// <summary>The pre-match plan against this opponent (docs/TACTICS.md §7.3).</summary>
    private void Prepare(Team team)
    {
        var opp = Get(team.Side.Other());
        var rotation = opp.Runners.Where(r => r.Priority <= 4).ToList();
        if (rotation.Count == 0 || !opp.OnCourt.Any())
        {
            return;
        }

        var t = team.Tactics;
        var (system, d, s) = (t.DefensiveSystem, t.Defense, t.Situations);
        var reasons = new List<string>();
        var three = rotation.Average(r => r.A.ThreePoint);
        var inside = rotation.Average(r => (r.A.PostMoves + r.A.CloseShot) / 2.0);
        if (system == DefensiveSystem.ManToMan && three <= 42 && inside >= three + 6)
        {
            system = DefensiveSystem.Zone23;
            reasons.Add("a 2-3 zone against poor shooters");
        }
        else if (TacticalModel.IsZone(system) && three >= 58)
        {
            system = DefensiveSystem.ManToMan;
            reasons.Add("man-to-man against good shooters");
        }

        var handler = BallHandler(opp);
        if ((handler.A.BallHandling + handler.A.DecisionMaking) / 2.0 <= 45 && d.BallPressure < TacticLevel.Max)
        {
            d = d with { BallPressure = d.BallPressure + 1 };
            reasons.Add($"pressure on {handler.Name}");
        }

        if (TacticalModel.IsManBased(opp.Tactics.DefensiveSystem) && s.MatchupTargeting == MatchupTargeting.Off
            && opp.OnCourt.MinBy(DefenseRating) is { } weak && DefenseRating(weak) <= 45)
        {
            s = s with { MatchupTargeting = MatchupTargeting.WeakestDefender };
            reasons.Add($"attack {weak.Name}'s matchup");
        }

        var tactics = t with { DefensiveSystem = system, Defense = d, Situations = s };
        tactics = ZoneAttack(team, opp, tactics, reasons);
        if (TacticalModel.IsManBased(system) && !HasStopper(team) && opp.OnCourt.MaxBy(r => Usage(opp, r)) is { } star
            && Stopper(team, star) is { } stopper && StopperRating(stopper, star) >= 55)
        {
            tactics = tactics.WithMatchup(stopper.Player, DefensiveMatchup.BestScorer);
            reasons.Add($"{stopper.Name} on {star.Name}");
        }

        Apply(team, tactics, "Game plan", reasons);
    }

    /// <summary>In-game review: score, the opponent's shot profile and scorers, the own shooting (docs/TACTICS.md §7.3).</summary>
    private void Review(Team team)
    {
        var opp = Get(team.Side.Other());
        var t = team.Tactics;
        var (system, d, o) = (t.DefensiveSystem, t.Defense, t.Offense);
        var reasons = new List<string>();
        var lead = team.Score - opp.Score;
        var closing = Period >= _rules.Periods && _clock <= 240;
        var press = system is DefensiveSystem.FullCourtPress or DefensiveSystem.HalfCourtPress;
        if (closing && lead <= -8)
        {
            if (system != DefensiveSystem.FullCourtPress)
            {
                (system, o, d) = (DefensiveSystem.FullCourtPress, o with { Tempo = TacticLevel.Max }, d with { Intensity = TacticLevel.Max });
                reasons.Add("full-court press and top speed to chase the game");
            }
        }
        else if (closing && lead < -3)
        {
            if (d.BallPressure < TacticLevel.Max || o.Tempo < 4)
            {
                (o, d) = (o with { Tempo = Math.Max(o.Tempo, 4) }, d with { BallPressure = TacticLevel.Max });
                reasons.Add("more ball pressure and pace while trailing");
            }
        }
        else if (closing && lead >= 8)
        {
            if (o.Tempo > TacticLevel.Min || press)
            {
                (system, o) = (press ? DefensiveSystem.ManToMan : system, o with { Tempo = TacticLevel.Min });
                reasons.Add("slows the game down to protect the lead");
            }
        }
        else if (!closing)
        {
            var fieldGoals = opp.FieldGoalPoints;
            if (system == DefensiveSystem.ManToMan && fieldGoals >= 16 && opp.InsidePoints >= 0.6 * fieldGoals
                && (opp.ThreeAttempted < 5 || opp.ThreeMade < 0.34 * opp.ThreeAttempted))
            {
                system = DefensiveSystem.Zone23;
                reasons.Add("a 2-3 zone: they score inside and miss outside");
            }
            else if (TacticalModel.IsZone(system) && opp.ThreeAttempted >= 7 && opp.ThreeMade >= 0.4 * opp.ThreeAttempted)
            {
                system = DefensiveSystem.ManToMan;
                reasons.Add("back to man-to-man: they hit threes against the zone");
            }
            else if (opp.Possessions >= 15 && opp.Turnovers >= 0.2 * opp.Possessions && d.BallPressure < TacticLevel.Max)
            {
                d = d with { BallPressure = d.BallPressure + 1 };
                reasons.Add("more ball pressure: their ball handlers struggle");
            }

            if (team.ThreeAttempted >= 10 && team.ThreeMade <= 0.25 * team.ThreeAttempted && o.Focus != OffensiveFocus.Inside)
            {
                o = o with { Focus = OffensiveFocus.Inside };
                reasons.Add("attack inside: the threes are not falling");
            }
        }

        var tactics = ZoneAttack(team, opp, t with { DefensiveSystem = system, Defense = d, Offense = o }, reasons);
        if (TacticalModel.IsManBased(system) && !HasStopper(team)
            && opp.Runners.MaxBy(r => r.Line.Points) is { } star && star.Line.Points >= 10 && star.Line.Points >= 0.3 * opp.Score
            && Stopper(team, star) is { } stopper)
        {
            tactics = tactics.WithMatchup(stopper.Player, DefensiveMatchup.BestScorer);
            reasons.Add($"{stopper.Name} now guards {star.Name} ({star.Line.Points} points)");
        }

        Apply(team, tactics, "Adjustment", reasons);
    }

    /// <summary>Against a zone the coach picks a zone offense for his players: shooters overload, a skilled big flashes high, else drive the gaps.</summary>
    private static TeamTactics ZoneAttack(Team team, Team opp, TeamTactics tactics, List<string> reasons)
    {
        if (!TacticalModel.IsZone(opp.Tactics.DefensiveSystem) || tactics.Situations.ZoneOffense != ZoneOffense.Standard || !team.OnCourt.Any())
        {
            return tactics;
        }

        var court = team.OnCourt.ToList();
        var attack = court.Average(r => r.A.ThreePoint) >= 55 ? ZoneOffense.Overload
            : court.Where(r => team.SlotOf(r) >= 3).Select(r => Math.Max(r.A.MidRange, r.A.PostMoves)).DefaultIfEmpty(0).Max() >= 60 ? ZoneOffense.HighPost
            : ZoneOffense.AttackGaps;
        reasons.Add($"{PlayerCatalog.DisplayName(attack).ToLowerInvariant()} against the zone");
        return tactics with { Situations = tactics.Situations with { ZoneOffense = attack } };
    }

    private static bool HasStopper(Team team) => team.Runners.Any(r =>
        r.Assignment.Matchup == DefensiveMatchup.BestScorer
        || (r.Assignment.Matchup == DefensiveMatchup.Positional && r.Assignment.Role == TacticalRole.PerimeterStopper));

    /// <summary>The best available defender for that scorer: interior defense against bigs, perimeter defense otherwise.</summary>
    private static Runner? Stopper(Team team, Runner star) =>
        team.Runners.Where(r => !r.Line.FouledOut && r.Priority <= 4).MaxBy(r => StopperRating(r, star));

    private static int StopperRating(Runner defender, Runner star) =>
        star.Player.Position >= Position.PowerForward ? defender.A.InteriorDefense : defender.A.PerimeterDefense;

    private void Apply(Team team, TeamTactics tactics, string what, List<string> reasons)
    {
        if (reasons.Count == 0)
        {
            return;
        }

        team.SetTactics(tactics);
        var text = $"{what}: {string.Join("; ", reasons)}.";
        Note(team, text);
        Emit(MatchEventKind.TacticsChanged, team.Side, null, null, 0, $"{team.Sheet.Name} — {text}");
    }
}
