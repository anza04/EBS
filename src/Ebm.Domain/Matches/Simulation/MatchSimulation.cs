using Ebm.Domain.Common;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Matches.Simulation;

/// <summary>
/// A possession-based basketball match under FIBA rules (docs/MATCH_ENGINE.md). Pure and deterministic: the same
/// <see cref="MatchSetup"/> (including its seed) and the same interventions always give the same match.
/// </summary>
/// <remarks>
/// <see cref="Step"/> plays one possession (or a period transition) and returns its play-by-play; <see cref="RunToEnd"/>
/// is the instant mode. Between steps the ball is dead, so substitutions, timeouts and tactic changes apply there.
/// Sides with <see cref="IsAutomatic"/> make their own substitutions and timeouts; a managed side only gets forced
/// substitutions (fouled-out players).
/// </remarks>
public sealed partial class MatchSimulation
{
    private readonly Random _random;
    private readonly MatchRules _rules;
    private readonly Team _home;
    private readonly Team _away;
    private readonly List<MatchEvent> _events = [];
    private TeamSide _possession;
    private TeamSide _arrow;
    private bool _periodRunning;
    private int _possessions;
    private bool _endedByDefault;

    public MatchSimulation(MatchSetup setup, SimulationMode mode = SimulationMode.Instant, PlayerRatingCalculator? calculator = null)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _rules = setup.MatchRules;
        setup.Home.Validate(_rules);
        setup.Away.Validate(_rules);
        if (setup.Home.Players.Select(p => p.Player.Id).Intersect(setup.Away.Players.Select(p => p.Player.Id)).Any())
        {
            throw new DomainValidationException("A player cannot play for both teams.");
        }

        Setup = setup;
        Mode = Guard.Defined(mode, "Simulation mode");
        calculator ??= PlayerRatingCalculator.Default;
        _random = new Random(setup.Seed);
        _home = new Team(TeamSide.Home, setup.Home, calculator, _random);
        _away = new Team(TeamSide.Away, setup.Away, calculator, _random);
        Period = 1;
        SecondsLeft = _rules.PeriodSeconds;
    }

    public MatchSetup Setup { get; }

    public SimulationMode Mode { get; }

    /// <summary>1–4 for the quarters, 5 and more for overtimes.</summary>
    public int Period { get; private set; }

    /// <summary>Seconds left in the current period (the game clock).</summary>
    public int SecondsLeft { get; private set; }

    public bool IsFinished { get; private set; }

    public int HomeScore => _home.Score;

    public int AwayScore => _away.Score;

    public int Overtimes => Math.Max(0, Period - _rules.Periods);

    public IReadOnlyList<MatchEvent> Events => _events;

    public int Score(TeamSide side) => Get(side).Score;

    public string TeamName(TeamSide side) => Get(side).Sheet.Name;

    public IReadOnlyList<PlayerLine> Lines(TeamSide side) => [.. Get(side).Runners.Select(r => r.Line)];

    /// <summary>The players on court, in slot order (guards first).</summary>
    public IReadOnlyList<PlayerLine> OnCourt(TeamSide side) => [.. Get(side).Court.Where(r => r is not null).Select(r => r!.Line)];

    public int TimeoutsLeft(TeamSide side) => Get(side).TimeoutsLeft;

    public int TeamFouls(TeamSide side) => Get(side).PeriodFouls;

    public TeamTactics Tactics(TeamSide side) => Get(side).Tactics;

    public bool IsAutomatic(TeamSide side) => Get(side).Automatic;

    /// <summary>Hands a side to the user (live mode) or back to the engine's coach.</summary>
    public void SetAutomatic(TeamSide side, bool automatic)
    {
        RequireLive();
        Get(side).Automatic = automatic;
    }

    /// <summary>Plays the next possession (or starts/ends a period) and returns its play-by-play.</summary>
    public IReadOnlyList<MatchEvent> Step()
    {
        if (IsFinished)
        {
            return [];
        }

        var first = _events.Count;
        if (!_periodRunning)
        {
            StartPeriod();
        }
        else
        {
            PlayPossession();
            if (SecondsLeft == 0 && !IsFinished)
            {
                EndPeriod();
            }
        }

        return _events[first..];
    }

    /// <summary>Plays until the current period ends (or the match).</summary>
    public void RunToEndOfPeriod()
    {
        if (!_periodRunning)
        {
            Step();
        }

        while (!IsFinished && _periodRunning)
        {
            Step();
        }
    }

    /// <summary>Plays the rest of the match.</summary>
    public MatchResult RunToEnd()
    {
        while (!IsFinished)
        {
            Step();
        }

        return Result();
    }

    public MatchResult Result()
    {
        if (!IsFinished)
        {
            throw new DomainException("The match is not over yet.");
        }

        return new MatchResult(_home.Score, _away.Score, Overtimes, Lines(TeamSide.Home), Lines(TeamSide.Away), _possessions, _endedByDefault);
    }

    /// <summary>Live mode: swaps a player on court for a bench player (dead ball between possessions).</summary>
    public void Substitute(TeamSide side, PlayerId playerOut, PlayerId playerIn)
    {
        RequireLive();
        RequireRunning();
        var team = Get(side);
        var leaving = team.Find(playerOut) ?? throw new DomainValidationException("That player is not in the squad.");
        var entering = team.Find(playerIn) ?? throw new DomainValidationException("That player is not in the squad.");
        if (!leaving.Line.IsOnCourt)
        {
            throw new DomainValidationException($"{leaving.Name} is not on the court.");
        }

        if (entering.Line.IsOnCourt)
        {
            throw new DomainValidationException($"{entering.Name} is already on the court.");
        }

        if (entering.Line.FouledOut)
        {
            throw new DomainValidationException($"{entering.Name} has fouled out.");
        }

        Swap(team, Array.IndexOf(team.Court, leaving), entering);
    }

    /// <summary>Live mode: calls a timeout for a side (rested players, run broken).</summary>
    public void CallTimeout(TeamSide side)
    {
        RequireLive();
        RequireRunning();
        var team = Get(side);
        if (team.TimeoutsLeft == 0)
        {
            throw new DomainValidationException($"{team.Sheet.Name} has no timeouts left in this period.");
        }

        Timeout(team);
    }

    /// <summary>Live mode: replaces a side's tactics from the next possession.</summary>
    public void ChangeTactics(TeamSide side, TeamTactics tactics)
    {
        ArgumentNullException.ThrowIfNull(tactics);
        RequireLive();
        if (IsFinished)
        {
            throw new DomainValidationException("The match is over.");
        }

        var team = Get(side);
        team.SetTactics(tactics);
        Emit(MatchEventKind.TacticsChanged, side, null, null, 0,
            $"{team.Sheet.Name} switch to {PlayerCatalog.DisplayName(tactics.OffensiveSystem)} on offense and {PlayerCatalog.DisplayName(tactics.DefensiveSystem)} on defense.");
    }

    private Team Get(TeamSide side) => side == TeamSide.Home ? _home : _away;

    private void RequireLive()
    {
        if (Mode != SimulationMode.Live)
        {
            throw new DomainValidationException("Interventions are only possible in live mode.");
        }
    }

    private void RequireRunning()
    {
        if (IsFinished)
        {
            throw new DomainValidationException("The match is over.");
        }
    }
}
