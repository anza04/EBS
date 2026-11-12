using Ebm.Domain.Players;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Matches.Simulation;

/// <summary>Possession flow and formulas. Every number is documented in docs/MATCH_ENGINE.md.</summary>
public sealed partial class MatchSimulation
{
    private double _clock;
    private double _elapsed;
    private bool _nextFastBreak;

    private enum ShotOutcome
    {
        Scored,
        DefensiveRebound,
        OffensiveRebound,
    }

    private static double Level(int level) => TacticLevel.Offset(level);

    // ---------------------------------------------------------------- periods

    private void StartPeriod()
    {
        _periodRunning = true;
        _clock = Period <= _rules.Periods ? _rules.PeriodSeconds : _rules.OvertimeSeconds;
        SecondsLeft = (int)_clock;
        foreach (var team in new[] { _home, _away })
        {
            if (Period <= _rules.Periods)
            {
                team.PeriodFouls = 0; // overtimes continue the fourth period's team fouls
            }

            if (Period == 1)
            {
                team.TimeoutsLeft = _rules.TimeoutsFirstHalf;
            }
            else if (Period == 3)
            {
                team.TimeoutsLeft = _rules.TimeoutsSecondHalf;
            }
            else if (Period > _rules.Periods)
            {
                team.TimeoutsLeft = _rules.TimeoutsPerOvertime;
            }

            if (Period > 1)
            {
                Rest(team, Period == 3 ? 25 : 8);
            }
        }

        Emit(MatchEventKind.PeriodStart, null, null, null, 0, Period <= _rules.Periods ? $"Start of the {Ordinal(Period)} quarter." : $"Start of overtime {Period - _rules.Periods}.");
        if (Period == 1)
        {
            JumpBall();
        }
        else
        {
            // Alternating possession arrow.
            _possession = _arrow;
            _arrow = _arrow.Other();
        }

        foreach (var team in new[] { _home, _away })
        {
            Substitutions(team, periodStart: true);
        }
    }

    private void JumpBall()
    {
        static double Reach(Runner? r) => r is null ? 1 : Math.Max(1, r.A.Vertical + ((r.Player.HeightCm - 200) * 1.5) + 50);
        var home = Reach(_home.Court[4]);
        var away = Reach(_away.Court[4]);
        _possession = _random.NextDouble() < home / (home + away) ? TeamSide.Home : TeamSide.Away;
        _arrow = _possession.Other();
        var winner = Get(_possession);
        Emit(MatchEventKind.JumpBall, _possession, winner.Court[4]?.Player.Id, null, 0, $"{winner.Sheet.Name} win the opening tip.");
    }

    private void EndPeriod()
    {
        _periodRunning = false;
        Emit(MatchEventKind.PeriodEnd, null, null, null, 0, $"End of {(Period <= _rules.Periods ? $"the {Ordinal(Period)} quarter" : $"overtime {Period - _rules.Periods}")}: {_home.Sheet.Name} {_home.Score}–{_away.Score} {_away.Sheet.Name}.");
        if (Period >= _rules.Periods && _home.Score != _away.Score)
        {
            Finish();
        }
        else
        {
            Period++;
            SecondsLeft = Period <= _rules.Periods ? _rules.PeriodSeconds : _rules.OvertimeSeconds;
        }
    }

    private void Finish()
    {
        IsFinished = true;
        _periodRunning = false;
        var winner = _home.Score > _away.Score ? _home : _away;
        Emit(MatchEventKind.GameEnd, winner.Side, null, null, 0, $"Final: {_home.Sheet.Name} {_home.Score}–{_away.Score} {_away.Sheet.Name}.");
    }

    /// <summary>FIBA art. 21: a team with fewer than two players on court loses; the score stands if the opponent leads, else 2–0.</summary>
    private void EndByDefault(Team loser)
    {
        var winner = Get(loser.Side.Other());
        if (winner.Score <= loser.Score)
        {
            (winner.Score, loser.Score) = (2, 0);
        }

        _endedByDefault = true;
        Emit(MatchEventKind.PeriodEnd, null, null, null, 0, $"{loser.Sheet.Name} have fewer than two players left: the game is over.");
        Finish();
    }

    // ---------------------------------------------------------------- possessions

    private void PlayPossession()
    {
        foreach (var team in new[] { _home, _away })
        {
            if (team.Automatic)
            {
                AutoTimeout(team);
            }

            Substitutions(team, periodStart: false);
            if (team.OnCourt.Count() < 2)
            {
                EndByDefault(team);
                return;
            }
        }

        var off = Get(_possession);
        var def = Get(_possession.Other());
        _possessions++;
        var fastBreak = _nextFastBreak;
        _nextFastBreak = false;
        double shotClock = _rules.ShotClock;
        while (true)
        {
            var secondChance = shotClock < _rules.ShotClock;
            var mean = fastBreak ? 5.0 : secondChance ? 6.5 : PossessionMean(off, def);
            var seconds = Math.Max(2.0, mean + (Normal(_random) * (fastBreak ? 1.5 : secondChance ? 3.0 : 4.5)));
            var violation = !fastBreak && seconds > shotClock && _clock > shotClock;
            if (violation)
            {
                seconds = shotClock;
            }

            var lastShot = seconds >= _clock;
            seconds = Math.Min(seconds, _clock);
            Elapse(seconds);
            if (violation)
            {
                Emit(MatchEventKind.ShotClockViolation, off.Side, null, null, 0, $"Shot-clock violation, {off.Sheet.Name}.");
                ChangePossession(stolen: false);
                return;
            }

            if (lastShot && seconds < 1)
            {
                return;
            }

            if (!lastShot)
            {
                var handler = BallHandler(off);
                if (_random.NextDouble() < TurnoverChance(off, def, handler, fastBreak))
                {
                    Turnover(off, def);
                    return;
                }

                if (!fastBreak && _random.NextDouble() < NonShootingFoulChance(def))
                {
                    if (NonShootingFoul(off, def))
                    {
                        return;
                    }

                    shotClock = Math.Max(shotClock - seconds, _rules.ResetShotClock);
                    continue;
                }
            }

            var outcome = Shot(off, def, fastBreak, lastShot);
            if (outcome == ShotOutcome.OffensiveRebound && _clock > 0 && !IsFinished)
            {
                shotClock = _rules.ResetShotClock;
                fastBreak = false;
                continue;
            }

            return;
        }
    }

    /// <summary>Mean seconds of a half-court possession (docs/MATCH_ENGINE.md §2).</summary>
    private static double PossessionMean(Team off, Team def)
    {
        var o = off.Tactics.Offense;
        var mean = 15.6
            - (2.0 * Level(o.Tempo))
            + (1.0 * Level(o.BallMovement))
            - (1.0 * Level(o.ShotSelection))
            + TacticalModel.PaceSeconds(off.Tactics.OffensiveSystem)
            + TacticalModel.PaceSeconds(def.Tactics.DefensiveSystem);
        return Math.Clamp(mean, 8, 21);
    }

    private void ChangePossession(bool stolen)
    {
        _possession = _possession.Other();
        _nextFastBreak = stolen && _random.NextDouble() < 0.45;
    }

    private void Elapse(double seconds)
    {
        var before = _clock;
        _clock = Math.Max(0, _clock - seconds);
        _elapsed += seconds;
        SecondsLeft = (int)Math.Ceiling(_clock);
        if (Period == _rules.Periods && before > 120 && _clock <= 120)
        {
            foreach (var team in new[] { _home, _away })
            {
                team.TimeoutsLeft = Math.Min(team.TimeoutsLeft, _rules.TimeoutsLastTwoMinutes);
            }
        }

        foreach (var team in new[] { _home, _away })
        {
            var drain = 0.06 * (1 + (0.2 * Level(team.Tactics.Offense.Tempo)) + (0.25 * Level(team.Tactics.Defense.Intensity)) + TacticalModel.DefensiveLoad(team.Tactics.DefensiveSystem));
            foreach (var runner in team.Runners)
            {
                if (runner.Line.IsOnCourt)
                {
                    runner.Line.SecondsPlayed += seconds;
                    runner.Energy -= seconds * drain * (1.5 - (runner.A.Stamina / 100.0));
                }
                else
                {
                    runner.Energy += seconds * 0.055;
                }
            }
        }
    }

    private static void Rest(Team team, double energy)
    {
        foreach (var runner in team.Runners)
        {
            runner.Energy += energy;
        }
    }

    // ---------------------------------------------------------------- choices

    private static Runner BallHandler(Team off) =>
        off.OnCourt.OrderByDescending(r => r.Assignment.Role == TacticalRole.PrimaryBallHandler)
            .ThenByDescending(r => r.A.BallHandling + r.A.DecisionMaking)
            .First();

    private T Pick<T>(IReadOnlyList<T> items, Func<T, double> weight)
    {
        var weights = items.Select(i => Math.Max(0.0001, weight(i))).ToList();
        var roll = _random.NextDouble() * weights.Sum();
        for (var i = 0; i < items.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0)
            {
                return items[i];
            }
        }

        return items[^1];
    }

    private static double RoleUsage(TacticalRole role) => role switch
    {
        TacticalRole.ShotCreator => 1.35,
        TacticalRole.Slasher or TacticalRole.PostScorer => 1.15,
        TacticalRole.PrimaryBallHandler or TacticalRole.SpotUpShooter => 1.0,
        TacticalRole.StretchBig => 0.9,
        TacticalRole.RollMan => 0.8,
        TacticalRole.PerimeterStopper => 0.65,
        TacticalRole.Rebounder => 0.65,
        _ => 0.6,
    };

    private static double SystemUsage(OffensiveSystem system, TacticalRole role, int slot) => (system, role) switch
    {
        (OffensiveSystem.PickAndRoll, TacticalRole.PrimaryBallHandler) => 1.25,
        (OffensiveSystem.PickAndRoll, TacticalRole.RollMan) => 1.5,
        (OffensiveSystem.PostOriented, TacticalRole.PostScorer) => 1.5,
        (OffensiveSystem.PostOriented, _) when slot >= 3 => 1.3,
        (OffensiveSystem.FiveOut, TacticalRole.SpotUpShooter or TacticalRole.StretchBig) => 1.25,
        (OffensiveSystem.FourOutOneIn, TacticalRole.PostScorer or TacticalRole.RollMan) => 1.2,
        (OffensiveSystem.Isolation, TacticalRole.ShotCreator) => 1.4,
        (OffensiveSystem.Transition, TacticalRole.Slasher) => 1.25,
        _ => 1.0,
    };

    private static double ShotPriorityFactor(int priority) => priority switch
    {
        1 => 0.45,
        2 => 0.7,
        4 => 1.4,
        5 => 1.9,
        _ => 1.0,
    };

    /// <summary>Share of the team's shots a player takes (docs/MATCH_ENGINE.md §2.2).</summary>
    private static double Usage(Team off, Runner r)
    {
        var slot = off.SlotOf(r);
        var rating = r.Rating(Slots[Math.Max(0, slot)]);
        var priority = ShotPriorityFactor(r.Assignment.ShotPriority);
        if (off.Tactics.Offense.Focus == OffensiveFocus.KeyPlayers)
        {
            priority = Math.Pow(priority, 1.6);
        }

        var skill = Math.Pow(rating / 60.0, off.Tactics.OffensiveSystem == OffensiveSystem.Isolation ? 3.5 : 2.5);
        return RoleUsage(r.Assignment.Role) * SystemUsage(off.Tactics.OffensiveSystem, r.Assignment.Role, slot) * priority * skill * (0.6 + (0.4 * r.Energy / 100));
    }

    private static double RoleShotMix(TacticalRole role, ShotType type) => (role, type) switch
    {
        (TacticalRole.SpotUpShooter, ShotType.Three) => 1.5,
        (TacticalRole.SpotUpShooter, ShotType.Rim) => 0.6,
        (TacticalRole.StretchBig, ShotType.Three) => 1.4,
        (TacticalRole.StretchBig, ShotType.Post) => 0.6,
        (TacticalRole.Slasher, ShotType.Rim) => 1.4,
        (TacticalRole.PostScorer, ShotType.Post) => 1.6,
        (TacticalRole.RollMan, ShotType.Rim) => 1.6,
        (TacticalRole.RollMan, ShotType.Three) => 0.3,
        (TacticalRole.RimProtector or TacticalRole.Rebounder, ShotType.Three) => 0.4,
        (TacticalRole.RimProtector or TacticalRole.Rebounder, ShotType.Rim) => 1.2,
        (TacticalRole.ShotCreator, ShotType.MidRange) => 1.4,
        _ => 1.0,
    };

    /// <summary>Shot type of a half-court shot (docs/MATCH_ENGINE.md §2.3).</summary>
    private ShotType PickShotType(Team off, Team def, Runner shooter, bool fastBreak, bool lastShot)
    {
        if (fastBreak)
        {
            return _random.NextDouble() < 0.8 ? ShotType.Rim : ShotType.Three;
        }

        if (lastShot && _clock < 3)
        {
            return _random.NextDouble() < 0.6 ? ShotType.Three : ShotType.MidRange;
        }

        var o = off.Tactics.Offense;
        var d = def.Tactics.Defense;
        double Weight(ShotType type)
        {
            var (tendency, scale) = type switch
            {
                ShotType.Three => (shooter.T.ThreePointShot, 1.0),
                ShotType.MidRange => (shooter.T.MidRangeShot, 0.5),
                ShotType.Rim => (shooter.T.DriveToBasket, 0.95),
                _ => (shooter.T.PostUp, 0.45),
            };
            var w = scale * Math.Pow(tendency, 1.3)
                * RoleShotMix(shooter.Assignment.Role, type)
                * TacticalModel.ShotMix(off.Tactics.OffensiveSystem, type)
                * TacticalModel.ShotMix(def.Tactics.DefensiveSystem, type);
            w *= type switch
            {
                ShotType.Three => (1 + (0.2 * Level(o.Spacing))) * (1 + (0.08 * Level(d.HelpDefense))),
                ShotType.Rim => (1 + (0.08 * Level(o.Spacing))) * (1 - (0.1 * Level(d.HelpDefense))) * (1 + (0.1 * Level(o.PickAndRollFrequency))),
                _ => 1.0,
            };
            w *= (o.Focus, type) switch
            {
                (OffensiveFocus.Inside, ShotType.Rim) => 1.25,
                (OffensiveFocus.Inside, ShotType.Post) => 1.3,
                (OffensiveFocus.Inside, ShotType.Three) => 0.75,
                (OffensiveFocus.Perimeter, ShotType.Three) => 1.3,
                (OffensiveFocus.Perimeter, ShotType.MidRange) => 1.1,
                (OffensiveFocus.Perimeter, ShotType.Rim) => 0.85,
                (OffensiveFocus.Perimeter, ShotType.Post) => 0.7,
                _ => 1.0,
            };
            if (type == ShotType.Three && shooter.A.ThreePoint < 35)
            {
                w *= 0.5;
            }

            return w;
        }

        return Pick(Enum.GetValues<ShotType>(), Weight);
    }

    /// <summary>The defender of a shot: the opposite slot, or the perimeter stopper against the main scorer in man-to-man.</summary>
    private static Runner? Defender(Team off, Team def, Runner shooter, ShotType type)
    {
        if (def.Tactics.DefensiveSystem == DefensiveSystem.ManToMan
            && type != ShotType.Post
            && def.OnCourt.FirstOrDefault(r => r.Assignment.Role == TacticalRole.PerimeterStopper) is { } stopper
            && off.OnCourt.MaxBy(r => Usage(off, r)) == shooter)
        {
            return stopper;
        }

        var slot = off.SlotOf(shooter);
        return (slot >= 0 ? def.Court[slot] : null) ?? def.OnCourt.FirstOrDefault();
    }

    // ---------------------------------------------------------------- probabilities

    /// <summary>Make probability of a field goal (docs/MATCH_ENGINE.md §2.4).</summary>
    private double MakeChance(Team off, Team def, Runner shooter, Runner? defender, ShotType type, bool fastBreak, bool lastShot, bool pickAndRoll)
    {
        var a = shooter.A;
        var skill = type switch
        {
            ShotType.Rim => (0.45 * a.Layup) + (0.3 * a.Dunk) + (0.15 * a.CloseShot) + (0.1 * a.Vertical),
            ShotType.Post => (0.6 * a.PostMoves) + (0.25 * a.CloseShot) + (0.15 * a.Strength),
            ShotType.MidRange => a.MidRange,
            _ => a.ThreePoint,
        };
        var zone = def.Tactics.DefensiveSystem is DefensiveSystem.Zone23 or DefensiveSystem.Zone32 or DefensiveSystem.Zone131;
        double defense;
        if (defender is null)
        {
            defense = 30;
        }
        else
        {
            var d = defender.A;
            defense = type switch
            {
                ShotType.Rim => (0.5 * d.InteriorDefense) + (0.5 * def.OnCourt.Max(r => (0.6 * r.A.Block) + (0.4 * r.A.InteriorDefense))),
                ShotType.Post => (0.6 * d.InteriorDefense) + (0.4 * d.Strength),
                _ when zone => def.OnCourt.Average(r => (0.7 * r.A.PerimeterDefense) + (0.3 * r.A.Agility)),
                _ => (0.7 * d.PerimeterDefense) + (0.3 * d.Agility),
            };
        }

        var (basis, attack, guard) = type switch
        {
            ShotType.Rim => (0.57, 0.004, 0.003),
            ShotType.Post => (0.47, 0.004, 0.003),
            ShotType.MidRange => (0.38, 0.004, 0.002),
            _ => (0.31, 0.004, 0.0015),
        };
        var o = off.Tactics.Offense;
        var dd = def.Tactics.Defense;
        var p = basis + ((skill - 50) * attack) - ((defense - 50) * guard);
        p += TacticalModel.MakeEdge(off.Tactics.OffensiveSystem, def.Tactics.DefensiveSystem, type);
        p += (0.01 * Level(o.BallMovement)) - (0.015 * Level(o.ShotSelection)) - (0.012 * Level(dd.Intensity));
        p += type switch
        {
            ShotType.Rim => (0.01 * Level(o.Spacing)) - (0.02 * Level(dd.HelpDefense)),
            ShotType.Three => 0.012 * Level(dd.HelpDefense),
            _ => 0,
        };
        if (pickAndRoll)
        {
            p += TacticalModel.PickAndRollEdge(dd.PickAndRollCoverage, type);
        }

        p -= (100 - shooter.Energy) * 0.0012;
        p += defender is null ? 0 : (100 - defender.Energy) * 0.0006;
        p += shooter.Form;
        p += off.Side == TeamSide.Home ? 0.015 : 0;
        p += 0.05 * (5 - def.OnCourt.Count());
        p += fastBreak && type == ShotType.Rim ? 0.12 : 0;
        p -= lastShot && _clock < 3 ? 0.1 : 0;
        return Math.Clamp(p, 0.05, 0.9);
    }

    private double TurnoverChance(Team off, Team def, Runner handler, bool fastBreak)
    {
        var o = off.Tactics.Offense;
        var d = def.Tactics.Defense;
        var p = 0.165
            + TacticalModel.TurnoverEdge(def.Tactics.DefensiveSystem)
            + (0.02 * Level(d.BallPressure))
            + (0.012 * Level(d.Intensity))
            + (0.012 * Level(o.BallMovement))
            - (0.008 * Level(o.ShotSelection))
            + (off.Tactics.OffensiveSystem == OffensiveSystem.Transition ? 0.01 : 0)
            - ((((handler.A.BallHandling + handler.A.DecisionMaking) / 2.0) - 50) * 0.0012)
            + ((def.OnCourt.Average(r => r.A.Steal) - 50) * 0.0008)
            + ((100 - handler.Energy) * 0.0004);
        if (off.Tactics.OffensiveSystem == OffensiveSystem.PickAndRoll)
        {
            p += TacticalModel.PickAndRollTurnovers(d.PickAndRollCoverage);
        }

        return Math.Clamp(fastBreak ? p * 0.6 : p, 0.04, 0.3);
    }

    private static double NonShootingFoulChance(Team def)
    {
        var d = def.Tactics.Defense;
        var press = def.Tactics.DefensiveSystem switch
        {
            DefensiveSystem.FullCourtPress => 0.03,
            DefensiveSystem.HalfCourtPress => 0.02,
            _ => 0.0,
        };
        return Math.Clamp(0.15 + (0.025 * Level(d.Intensity)) + (0.02 * Level(d.BallPressure)) + press, 0.02, 0.25);
    }

    private static double ShootingFoulChance(Team def, ShotType type, bool fastBreak)
    {
        var basis = type switch
        {
            ShotType.Rim => fastBreak ? 0.27 : 0.23,
            ShotType.Post => 0.2,
            ShotType.MidRange => 0.06,
            _ => 0.025,
        };
        return basis * (1 + (0.25 * Level(def.Tactics.Defense.Intensity)));
    }

    private static double FreeThrowChance(Runner shooter) =>
        Math.Clamp(0.53 + (0.0042 * shooter.A.FreeThrow) - ((100 - shooter.Energy) * 0.0006) + (shooter.Form * 0.5), 0.3, 0.95);

    private static double AssistChance(Team off, Runner shooter, ShotType type, bool fastBreak)
    {
        var p = fastBreak ? 0.45 : type switch
        {
            ShotType.Three => 0.76,
            ShotType.MidRange => 0.45,
            ShotType.Rim => 0.5,
            _ => 0.28,
        };
        p *= 1 + (0.15 * Level(off.Tactics.Offense.BallMovement));
        p *= off.Tactics.OffensiveSystem switch
        {
            OffensiveSystem.MotionOffense => 1.08,
            OffensiveSystem.PickAndRoll => 1.05,
            OffensiveSystem.Isolation => 0.7,
            _ => 1.0,
        };
        var passers = off.OnCourt.Where(r => r != shooter).ToList();
        if (passers.Count == 0)
        {
            return 0;
        }

        p *= passers.Average(r => (r.A.Passing + r.A.CourtVision) / 2.0) / 60.0;
        return Math.Clamp(p, 0, 0.95);
    }

    // ---------------------------------------------------------------- events of a possession

    private void Turnover(Team off, Team def)
    {
        var court = off.OnCourt.ToList();
        var loser = Pick(court, r => Usage(off, r) * (110 - r.A.BallHandling) / 60.0);
        loser.Line.Turnovers++;
        var stealChance = 0.48 + (0.1 * Level(def.Tactics.Defense.BallPressure));
        if (_random.NextDouble() < stealChance)
        {
            var thief = Pick(def.OnCourt.ToList(), r => Math.Pow(r.A.Steal, 2) * (1 + (r.T.DefensiveGambling / 100.0)));
            thief.Line.Steals++;
            Emit(MatchEventKind.Turnover, off.Side, loser.Player.Id, thief.Player.Id, 0, $"{thief.Name} steals the ball from {loser.Name}.");
            ChangePossession(stolen: true);
            return;
        }

        if (_random.NextDouble() < 0.15)
        {
            Emit(MatchEventKind.Turnover, off.Side, loser.Player.Id, null, 0, $"Offensive foul on {loser.Name}.");
            AddFoul(off, loser, null);
            if (IsFinished)
            {
                return;
            }
        }
        else
        {
            var what = _random.Next(3) switch { 0 => "throws the ball away", 1 => "is called for travelling", _ => "loses the ball out of bounds" };
            Emit(MatchEventKind.Turnover, off.Side, loser.Player.Id, null, 0, $"{loser.Name} {what}.");
        }

        ChangePossession(stolen: false);
    }

    /// <summary>A defensive foul away from a shot. Returns <c>true</c> when the possession ended (bonus free throws).</summary>
    private bool NonShootingFoul(Team off, Team def)
    {
        var fouler = Pick(def.OnCourt.ToList(), r => 1 + ((100 - r.A.DefensivePositioning) / 100.0));
        var fouled = Pick(off.OnCourt.ToList(), r => Usage(off, r));
        var bonus = def.PeriodFouls >= _rules.BonusFouls;
        AddFoul(def, fouler, fouled);
        if (IsFinished)
        {
            return true;
        }

        if (!bonus)
        {
            return false;
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

    private ShotOutcome Shot(Team off, Team def, bool fastBreak, bool lastShot)
    {
        var shooter = Pick(off.OnCourt.ToList(), r => Usage(off, r));
        var type = PickShotType(off, def, shooter, fastBreak, lastShot);
        var defender = Defender(off, def, shooter, type);
        var pickAndRoll = off.Tactics.OffensiveSystem == OffensiveSystem.PickAndRoll
            || _random.NextDouble() < 0.15 * (1 + Level(off.Tactics.Offense.PickAndRollFrequency));
        var chance = MakeChance(off, def, shooter, defender, type, fastBreak, lastShot, pickAndRoll);
        var points = type == ShotType.Three ? 3 : 2;

        if (defender is not null && _random.NextDouble() < ShootingFoulChance(def, type, fastBreak))
        {
            var fouler = type is ShotType.Rim or ShotType.Post && _random.NextDouble() < 0.4
                ? def.OnCourt.MaxBy(r => r.A.Block + r.A.InteriorDefense)!
                : defender;
            AddFoul(def, fouler, shooter);
            if (IsFinished)
            {
                return ShotOutcome.Scored;
            }

            var andOne = _random.NextDouble() < chance * 0.5;
            if (andOne)
            {
                FieldGoal(off, def, shooter, type, made: true, fastBreak, andOne: true);
            }

            if (FreeThrows(off, shooter, andOne ? 1 : points))
            {
                return ResolveRebound(off, def, type, freeThrow: true);
            }

            ChangePossession(stolen: false);
            return ShotOutcome.Scored;
        }

        if (_random.NextDouble() < chance)
        {
            FieldGoal(off, def, shooter, type, made: true, fastBreak, andOne: false);
            ChangePossession(stolen: false);
            return ShotOutcome.Scored;
        }

        FieldGoal(off, def, shooter, type, made: false, fastBreak, andOne: false);
        return ResolveRebound(off, def, type, freeThrow: false);
    }

    private void FieldGoal(Team off, Team def, Runner shooter, ShotType type, bool made, bool fastBreak, bool andOne)
    {
        var three = type == ShotType.Three;
        if (three)
        {
            shooter.Line.ThreePointAttempted++;
        }
        else
        {
            shooter.Line.TwoPointAttempted++;
        }

        var what = type switch
        {
            ShotType.Rim => fastBreak ? "on the fast break" : shooter.A.Dunk > 65 && shooter.Player.HeightCm >= 200 ? "with a dunk" : "on a drive to the rim",
            ShotType.Post => "in the post",
            ShotType.MidRange => "from mid-range",
            _ => "from three",
        };
        if (!made)
        {
            var blockChance = type switch
            {
                ShotType.Rim or ShotType.Post => 0.11 + ((def.OnCourt.Max(r => r.A.Block) - 50) * 0.002),
                ShotType.MidRange => 0.025,
                _ => 0.01,
            };
            if (_random.NextDouble() < Math.Clamp(blockChance, 0, 0.3))
            {
                var blocker = Pick(def.OnCourt.ToList(), r => Math.Pow(r.A.Block, 2) * (r.Player.HeightCm / 200.0));
                blocker.Line.Blocks++;
                Emit(MatchEventKind.FieldGoalMissed, off.Side, shooter.Player.Id, blocker.Player.Id, 0, $"{blocker.Name} blocks {shooter.Name} {what}.");
            }
            else
            {
                Emit(MatchEventKind.FieldGoalMissed, off.Side, shooter.Player.Id, null, 0, $"{shooter.Name} misses {what}.");
            }

            return;
        }

        if (three)
        {
            shooter.Line.ThreePointMade++;
        }
        else
        {
            shooter.Line.TwoPointMade++;
        }

        var points = three ? 3 : 2;
        Runner? passer = null;
        if (_random.NextDouble() < AssistChance(off, shooter, type, fastBreak))
        {
            var passers = off.OnCourt.Where(r => r != shooter).ToList();
            passer = Pick(passers, r =>
                Math.Pow((r.A.Passing + r.A.CourtVision) / 2.0, 2)
                * (r.Assignment.Role switch { TacticalRole.PrimaryBallHandler => 2.2, TacticalRole.ShotCreator => 1.2, _ => 1.0 })
                * (0.5 + (r.T.PassFirst / 100.0)));
            passer.Line.Assists++;
        }

        AddPoints(off, points);
        var text = $"{shooter.Name} scores {what}" + (passer is null ? string.Empty : $" (assist {passer.Name})") + (andOne ? ", and one!" : ".");
        Emit(MatchEventKind.FieldGoalMade, off.Side, shooter.Player.Id, passer?.Player.Id, points, text);
    }

    /// <summary>Shoots free throws; returns <c>true</c> when the last one is missed (a live rebound).</summary>
    private bool FreeThrows(Team off, Runner shooter, int count)
    {
        var missedLast = false;
        for (var i = 1; i <= count; i++)
        {
            shooter.Line.FreeThrowAttempted++;
            if (_random.NextDouble() < FreeThrowChance(shooter))
            {
                shooter.Line.FreeThrowMade++;
                AddPoints(off, 1);
                Emit(MatchEventKind.FreeThrowMade, off.Side, shooter.Player.Id, null, 1, $"{shooter.Name} makes free throw {i} of {count}.");
                missedLast = false;
            }
            else
            {
                Emit(MatchEventKind.FreeThrowMissed, off.Side, shooter.Player.Id, null, 0, $"{shooter.Name} misses free throw {i} of {count}.");
                missedLast = true;
            }
        }

        return missedLast;
    }

    private ShotOutcome ResolveRebound(Team off, Team def, ShotType type, bool freeThrow)
    {
        static double Board(Runner r, bool offensive) =>
            (offensive ? r.A.OffensiveRebound : r.A.DefensiveRebound) + ((r.Player.HeightCm - 195) * 1.2);
        var offCourt = off.OnCourt.ToList();
        var defCourt = def.OnCourt.ToList();
        var chance = freeThrow ? 0.14 : 0.29 + (type == ShotType.Three ? 0.02 : 0);
        chance += (offCourt.Average(r => Board(r, true)) - defCourt.Average(r => Board(r, false))) * 0.004;
        chance += TacticalModel.OffensiveReboundEdge(off.Tactics, def.Tactics);
        chance += 0.04 * (offCourt.Count - defCourt.Count);
        chance = Math.Clamp(chance, 0.08, 0.45);

        static double RoleBoard(Runner r) => r.Assignment.Role switch
        {
            TacticalRole.Rebounder => 1.4,
            TacticalRole.RimProtector or TacticalRole.RollMan => 1.15,
            _ => 1.0,
        };

        if (_random.NextDouble() < chance)
        {
            var rebounder = Pick(offCourt, r => Math.Pow(Math.Max(5, Board(r, true) + (r.T.CrashOffensiveGlass * 0.2)), 2) * RoleBoard(r));
            rebounder.Line.OffensiveRebounds++;
            Emit(MatchEventKind.OffensiveRebound, off.Side, rebounder.Player.Id, null, 0, $"Offensive rebound {rebounder.Name}.");
            return ShotOutcome.OffensiveRebound;
        }

        var grabber = Pick(defCourt, r => Math.Pow(Math.Max(5, Board(r, false)), 2) * RoleBoard(r));
        grabber.Line.DefensiveRebounds++;
        Emit(MatchEventKind.DefensiveRebound, def.Side, grabber.Player.Id, null, 0, $"{grabber.Name} grabs the defensive rebound.");
        _possession = def.Side;
        _nextFastBreak = _random.NextDouble() < TacticalModel.FastBreakAfterRebound(def.Tactics, off.Tactics);
        return ShotOutcome.DefensiveRebound;
    }

    private void AddPoints(Team team, int points)
    {
        team.Score += points;
        team.Run += points;
        var other = Get(team.Side.Other());
        other.Run = 0;
        foreach (var r in team.OnCourt)
        {
            r.Line.PlusMinus += points;
        }

        foreach (var r in other.OnCourt)
        {
            r.Line.PlusMinus -= points;
        }
    }

    private void AddFoul(Team team, Runner fouler, Runner? fouled)
    {
        fouler.Line.PersonalFouls++;
        team.PeriodFouls++;
        Emit(MatchEventKind.Foul, team.Side, fouler.Player.Id, fouled?.Player.Id, 0,
            fouled is null ? $"Foul on {fouler.Name} ({fouler.Line.PersonalFouls})." : $"Foul on {fouler.Name} ({fouler.Line.PersonalFouls}), {fouled.Name} fouled.");
        if (fouler.Line.PersonalFouls >= _rules.FoulOutLimit)
        {
            fouler.Line.FouledOut = true;
            Emit(MatchEventKind.FouledOut, team.Side, fouler.Player.Id, null, 0, $"{fouler.Name} fouls out.");

            // He leaves at once (the ball is dead after a foul); a team left with fewer than two players loses.
            Substitutions(team, periodStart: false, forcedOnly: true);
            if (team.OnCourt.Count() < 2)
            {
                EndByDefault(team);
            }
        }
    }

    // ---------------------------------------------------------------- coaching

    private void Timeout(Team team)
    {
        team.TimeoutsLeft--;
        team.LastTimeout = _elapsed;
        Get(team.Side.Other()).Run = 0;
        foreach (var runner in team.OnCourt)
        {
            runner.Energy += 4;
        }

        Emit(MatchEventKind.Timeout, team.Side, null, null, 0, $"Timeout {team.Sheet.Name}.");
    }

    /// <summary>The engine's coach stops a run of eight unanswered points (at most one timeout every three minutes).</summary>
    private void AutoTimeout(Team team)
    {
        if (_periodRunning && team.TimeoutsLeft > 0 && Get(team.Side.Other()).Run >= 8 && _elapsed - team.LastTimeout >= 180)
        {
            Timeout(team);
        }
    }

    /// <summary>
    /// Forced substitutions (fouled-out players, empty slots) for every side; rest, foul trouble, minutes targets and
    /// starters returning for automatic sides (docs/MATCH_ENGINE.md §5).
    /// </summary>
    private void Substitutions(Team team, bool periodStart, bool forcedOnly = false)
    {
        for (var slot = 0; slot < 5; slot++)
        {
            if (team.Court[slot] is null || team.Court[slot]!.Line.FouledOut)
            {
                var replacement = Bench(team).MaxBy(r => Value(team, r, slot));
                if (replacement is not null)
                {
                    Swap(team, slot, replacement);
                }
                else if (team.Court[slot] is { } gone)
                {
                    gone.Line.IsOnCourt = false;
                    team.Court[slot] = null;
                }
            }
        }

        if (!team.Automatic || forcedOnly)
        {
            return;
        }

        for (var slot = 0; slot < 5; slot++)
        {
            if (team.Court[slot] is not { } current)
            {
                continue;
            }

            var tired = NeedsRest(team, current);
            var minimum = tired ? 70 : periodStart ? 80 : 88;
            var best = Bench(team).Where(r => r.Energy >= minimum).MaxBy(r => Value(team, r, slot));
            if (best is null)
            {
                continue;
            }

            var swap = tired
                ? Value(team, best, slot) > Value(team, current, slot) * 0.8 || current.Energy < 55
                : best.Priority < current.Priority && Value(team, best, slot) >= Value(team, current, slot) - 2;
            if (swap)
            {
                Swap(team, slot, best);
            }
        }
    }

    private static IEnumerable<Runner> Bench(Team team) => team.Runners.Where(r => !r.Line.IsOnCourt && !r.Line.FouledOut);

    private bool InFoulTrouble(Runner r) =>
        Period < _rules.Periods ? r.Line.PersonalFouls >= Period + 1 : r.Line.PersonalFouls == _rules.FoulOutLimit - 1 && _clock > 300;

    private bool NeedsRest(Team team, Runner r)
    {
        if (InFoulTrouble(r))
        {
            return true;
        }

        if (r.Assignment.MinutesTarget is { } target && r.Line.SecondsPlayed / 60 >= target)
        {
            return true;
        }

        var threshold = r.Priority == 0 ? 60.0 : 68.0;
        if (r.Assignment.MinutesTarget >= 32)
        {
            threshold -= 6;
        }

        var closing = Period >= _rules.Periods && _clock <= 240 && Math.Abs(_home.Score - _away.Score) <= 10;
        if (closing)
        {
            threshold -= 15;
        }

        return r.Energy < threshold;
    }

    /// <summary>How much the coach wants a player in a slot: rating there, energy, rotation order, fouls and minutes.</summary>
    private double Value(Team team, Runner r, int slot)
    {
        var value = r.Rating(Slots[slot]) * (0.75 + (0.25 * r.Energy / 100)) - (r.Priority * 0.8);
        if (r.Assignment.MinutesTarget is { } target && r.Line.SecondsPlayed / 60 >= target)
        {
            value -= 15;
        }

        if (InFoulTrouble(r))
        {
            value -= 10;
        }

        return value;
    }

    private void Swap(Team team, int slot, Runner entering)
    {
        var leaving = team.Court[slot];
        if (leaving is not null)
        {
            leaving.Line.IsOnCourt = false;
        }

        entering.Line.IsOnCourt = true;
        team.Court[slot] = entering;
        Emit(MatchEventKind.Substitution, team.Side, entering.Player.Id, leaving?.Player.Id, 0,
            leaving is null ? $"{entering.Name} checks in for {team.Sheet.Name}." : $"Substitution {team.Sheet.Name}: {entering.Name} in, {leaving.Name} out.");
    }

    // ---------------------------------------------------------------- play-by-play

    private void Emit(MatchEventKind kind, TeamSide? side, PlayerId? player, PlayerId? other, int points, string text) =>
        _events.Add(new MatchEvent(Period, SecondsLeft, kind, side, player, other, points, _home.Score, _away.Score, text));

    private static string Ordinal(int n) => n switch { 1 => "first", 2 => "second", 3 => "third", _ => "fourth" };
}
