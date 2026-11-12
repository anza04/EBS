using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;

namespace Ebm.Domain.Tactics;

/// <summary>A player's assignment in a tactic.</summary>
public sealed class TacticalAssignment
{
    public const int MaxMinutesTarget = 40;

    public TacticalAssignment(PlayerId playerId, TacticalRole role, int shotPriority = TacticLevel.Normal, int? minutesTarget = null)
    {
        PlayerId = Guard.Required(playerId, nameof(playerId));
        Role = Guard.Defined(role, "Tactical role");
        ShotPriority = TacticLevel.Check(shotPriority, "Shot priority");
        MinutesTarget = minutesTarget is { } minutes ? Guard.Range(minutes, 0, MaxMinutesTarget, "Minutes target") : null;
    }

    private TacticalAssignment()
    {
    }

    public PlayerId PlayerId { get; private set; }

    public TacticalRole Role { get; private set; }

    /// <summary>1 = rarely shoots, 3 = normal, 5 = first option.</summary>
    public int ShotPriority { get; private set; } = TacticLevel.Normal;

    /// <summary>Minutes the coach wants him to play; <c>null</c> = let the rotation decide.</summary>
    public int? MinutesTarget { get; private set; }
}

/// <summary>
/// A club's saved tactical configuration: offensive and defensive systems, their settings and the player
/// assignments. A club may keep several; the selected one is used in its next match (docs/TACTICS.md).
/// </summary>
public sealed class Tactic : Entity<TacticId>
{
    public const int NameMaxLength = 60;
    public const int MaxAssignments = 30;

    private List<TacticalAssignment> _assignments = [];

    public Tactic(
        TacticId id,
        ClubId clubId,
        string name,
        OffensiveSystem offensiveSystem,
        OffensiveSettings offense,
        DefensiveSystem defensiveSystem,
        DefensiveSettings defense)
        : base(id)
    {
        ClubId = Guard.Required(clubId, nameof(clubId));
        Update(name, offensiveSystem, offense, defensiveSystem, defense);
    }

    private Tactic()
    {
    }

    public ClubId ClubId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public OffensiveSystem OffensiveSystem { get; private set; }

    public OffensiveSettings Offense { get; private set; } = OffensiveSettings.Default;

    public DefensiveSystem DefensiveSystem { get; private set; }

    public DefensiveSettings Defense { get; private set; } = DefensiveSettings.Default;

    /// <summary>Whether this is the tactic the club plays its next match with. At most one per club.</summary>
    public bool IsSelected { get; private set; }

    public IReadOnlyList<TacticalAssignment> Assignments => _assignments;

    /// <summary>A balanced tactic: motion offense, man-to-man, every setting normal.</summary>
    public static Tactic CreateDefault(ClubId clubId) => new(
        TacticId.New(), clubId, "Standard", OffensiveSystem.MotionOffense, OffensiveSettings.Default, DefensiveSystem.ManToMan, DefensiveSettings.Default);

    public void Update(string name, OffensiveSystem offensiveSystem, OffensiveSettings offense, DefensiveSystem defensiveSystem, DefensiveSettings defense)
    {
        ArgumentNullException.ThrowIfNull(offense);
        ArgumentNullException.ThrowIfNull(defense);
        offense.Validate();
        defense.Validate();
        Name = Guard.Text(name, "Tactic name", NameMaxLength);
        OffensiveSystem = Guard.Defined(offensiveSystem, "Offensive system");
        DefensiveSystem = Guard.Defined(defensiveSystem, "Defensive system");
        Offense = offense;
        Defense = defense;
    }

    /// <summary>Replaces the player assignments; each player at most once.</summary>
    public void SetAssignments(IEnumerable<TacticalAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var list = assignments.ToList();
        if (list.Count > MaxAssignments)
        {
            throw new DomainValidationException($"A tactic can assign at most {MaxAssignments} players.");
        }

        if (list.Select(a => a.PlayerId).Distinct().Count() != list.Count)
        {
            throw new DomainValidationException("A player is assigned twice in the tactic.");
        }

        _assignments = list;
    }

    public void Select() => IsSelected = true;

    public void Deselect() => IsSelected = false;

    /// <summary>The configuration as the match engine reads it.</summary>
    public TeamTactics ToTeamTactics() =>
        new(OffensiveSystem, Offense, DefensiveSystem, Defense, Assignments.ToDictionary(a => a.PlayerId));
}

/// <summary>
/// The tactical configuration a team plays with: what the match engine reads (docs/TACTICS.md §5). Immutable;
/// changed during a match by replacing it.
/// </summary>
public sealed record TeamTactics(
    OffensiveSystem OffensiveSystem,
    OffensiveSettings Offense,
    DefensiveSystem DefensiveSystem,
    DefensiveSettings Defense,
    IReadOnlyDictionary<PlayerId, TacticalAssignment> Assignments)
{
    public static readonly TeamTactics Default = new(
        OffensiveSystem.MotionOffense, OffensiveSettings.Default, DefensiveSystem.ManToMan, DefensiveSettings.Default, new Dictionary<PlayerId, TacticalAssignment>());

    /// <summary>The player's assignment; players without one get the suggested role at normal priority.</summary>
    public TacticalAssignment AssignmentOf(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return Assignments.TryGetValue(player.Id, out var assignment)
            ? assignment
            : new TacticalAssignment(player.Id, TacticalRoles.Suggest(player));
    }
}
