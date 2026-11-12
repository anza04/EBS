using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Matches.Simulation;

public enum TeamSide
{
    Home,
    Away,
}

/// <summary>How a match is followed (docs/MATCH_ENGINE.md §6).</summary>
public enum SimulationMode
{
    /// <summary>Only the result: the match is simulated to the end at once.</summary>
    Instant,

    /// <summary>Possession by possession with commentary; no interventions.</summary>
    Commentary,

    /// <summary>Possession by possession; the managed side can substitute, call timeouts and change tactics.</summary>
    Live,
}

public static class TeamSides
{
    public static readonly IReadOnlyList<TeamSide> Both = [TeamSide.Home, TeamSide.Away];

    public static TeamSide Other(this TeamSide side) => side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
}

/// <summary>Match rules. <see cref="Fiba"/> is the default: four 10-minute quarters, 5-minute overtimes, 24/14-second shot clock.</summary>
public sealed record MatchRules
{
    public static readonly MatchRules Fiba = new();

    public int Periods { get; init; } = 4;

    public int PeriodSeconds { get; init; } = 600;

    public int OvertimeSeconds { get; init; } = 300;

    public int ShotClock { get; init; } = 24;

    /// <summary>Shot clock after an offensive rebound.</summary>
    public int ResetShotClock { get; init; } = 14;

    /// <summary>Team fouls in a period before the bonus: from the next foul every defensive foul gives two free throws.</summary>
    public int BonusFouls { get; init; } = 4;

    public int FoulOutLimit { get; init; } = 5;

    public int TimeoutsFirstHalf { get; init; } = 2;

    public int TimeoutsSecondHalf { get; init; } = 3;

    /// <summary>Second-half timeouts still usable in the last two minutes of the fourth period.</summary>
    public int TimeoutsLastTwoMinutes { get; init; } = 2;

    public int TimeoutsPerOvertime { get; init; } = 1;

    public int MinPlayers { get; init; } = 5;

    public int MaxPlayers { get; init; } = 12;
}

/// <summary>A player named for the match. <paramref name="StartingPosition"/> is set for the five starters only.</summary>
/// <param name="Priority">Rotation priority: 0 for starters, then the rotation order; lower comes in first.</param>
public sealed record SheetPlayer(Player Player, Position? StartingPosition, int Priority);

/// <summary>A team's matchday squad and tactics.</summary>
public sealed record TeamSheet(ClubId ClubId, string Name, IReadOnlyList<SheetPlayer> Players, TeamTactics Tactics)
{
    /// <summary>
    /// Picks the matchday squad from a club's players: unavailable and youth/inactive players are left out; starters,
    /// then the rotation in order, then the bench, up to <see cref="MatchRules.MaxPlayers"/>. Missing starting positions
    /// are filled by the best available player at that position, so every club can play.
    /// </summary>
    public static TeamSheet Pick(
        ClubId clubId,
        string name,
        IEnumerable<(Player Player, RosterRole Role, Position? LineupPosition, int? RotationOrder)> players,
        TeamTactics tactics,
        PlayerRatingCalculator calculator,
        MatchRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(calculator);
        rules ??= MatchRules.Fiba;
        var available = players
            .Where(p => RosterRoles.AvailabilityOf(p.Player) != PlayerAvailability.Unavailable)
            .ToList();
        var active = available.Where(p => p.Role.IsActive()).ToList();
        if (active.Count < rules.MinPlayers)
        {
            // Not enough named players: complete with inactive and youth players.
            active.AddRange(available.Where(p => !p.Role.IsActive()).OrderByDescending(p => calculator.Evaluate(p.Player).Overall).Take(rules.MinPlayers - active.Count));
        }

        var ordered = active
            .OrderBy(p => p.Role switch { RosterRole.Starter => 0, RosterRole.Rotation => 1, RosterRole.Bench => 2, _ => 3 })
            .ThenBy(p => p.RotationOrder ?? int.MaxValue)
            .ThenByDescending(p => calculator.Evaluate(p.Player).Overall)
            .Take(rules.MaxPlayers)
            .ToList();
        if (ordered.Count < rules.MinPlayers)
        {
            throw new DomainException($"{name} has only {ordered.Count} players available; at least {rules.MinPlayers} are needed to play.");
        }

        var starters = new Dictionary<Position, Player>();
        foreach (var p in ordered.Where(p => p.Role == RosterRole.Starter && p.LineupPosition is not null))
        {
            starters.TryAdd(p.LineupPosition!.Value, p.Player);
        }

        foreach (var position in Enum.GetValues<Position>().Where(pos => !starters.ContainsKey(pos)))
        {
            var best = ordered.Select(p => p.Player)
                .Where(p => !starters.ContainsValue(p))
                .OrderByDescending(p => calculator.Evaluate(p).At(position).EffectiveRating)
                .First();
            starters[position] = best;
        }

        var priority = 0;
        var sheet = ordered.Select(p =>
        {
            var start = starters.FirstOrDefault(s => s.Value == p.Player);
            return start.Value is null ? new SheetPlayer(p.Player, null, ++priority) : new SheetPlayer(p.Player, start.Key, 0);
        }).ToList();
        return new TeamSheet(clubId, name, sheet, tactics);
    }

    public void Validate(MatchRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (Players.Count < rules.MinPlayers || Players.Count > rules.MaxPlayers)
        {
            throw new DomainValidationException($"{Name} must name between {rules.MinPlayers} and {rules.MaxPlayers} players.");
        }

        if (Players.Select(p => p.Player.Id).Distinct().Count() != Players.Count)
        {
            throw new DomainValidationException($"A player is named twice for {Name}.");
        }

        var starts = Players.Where(p => p.StartingPosition is not null).Select(p => p.StartingPosition!.Value).ToList();
        if (starts.Count != 5 || starts.Distinct().Count() != 5)
        {
            throw new DomainValidationException($"{Name} needs five starters, one per position.");
        }
    }
}

/// <summary>Everything a simulation needs. The same setup and seed always give the same match.</summary>
public sealed record MatchSetup(TeamSheet Home, TeamSheet Away, int Seed, MatchRules? Rules = null)
{
    public MatchRules MatchRules => Rules ?? MatchRules.Fiba;

    public TeamSheet Sheet(TeamSide side) => side == TeamSide.Home ? Home : Away;
}
