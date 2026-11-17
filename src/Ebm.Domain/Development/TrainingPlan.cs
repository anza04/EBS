using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;

namespace Ebm.Domain.Development;

/// <summary>What a training programme works on (docs/DEVELOPMENT.md §2).</summary>
public enum TrainingFocus
{
    /// <summary>Every trainable attribute equally.</summary>
    Balanced,

    /// <summary>Shooting and finishing.</summary>
    Shooting,

    /// <summary>Playmaking, concentration and consistency: reading the game and running the system.</summary>
    Tactical,

    /// <summary>Defense and rebounding.</summary>
    Defense,

    /// <summary>Speed, strength, vertical, agility and stamina; also slows their decline.</summary>
    Physical,

    /// <summary>Rest: faster fitness recovery, less development.</summary>
    Recovery,
}

/// <summary>How hard a club trains: harder training develops more but recovers less fitness.</summary>
public enum TrainingIntensity
{
    Light,
    Normal,
    Hard,
}

public readonly record struct TrainingPlanId(Guid Value) : IEntityId<TrainingPlanId>
{
    public static TrainingPlanId New() => new(Guid.CreateVersion7());

    public static TrainingPlanId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>One player's individual training focus, on top of the team focus.</summary>
public sealed class IndividualTraining
{
    public IndividualTraining(PlayerId playerId, TrainingFocus focus)
    {
        PlayerId = Guard.Required(playerId, nameof(playerId));
        Focus = Guard.Defined(focus, "Individual focus");
    }

    private IndividualTraining()
    {
    }

    public PlayerId PlayerId { get; private set; }

    public TrainingFocus Focus { get; private set; }
}

/// <summary>
/// A club's training programme: team focus, intensity and individual focuses. One per club, current state (like a
/// tactic's assignments, individual entries are not foreign keys: a player who leaves is simply ignored).
/// </summary>
public sealed class TrainingPlan : Entity<TrainingPlanId>
{
    public const int MaxIndividuals = 30;

    private List<IndividualTraining> _individuals = [];

    public TrainingPlan(TrainingPlanId id, ClubId clubId, TrainingFocus teamFocus = TrainingFocus.Balanced, TrainingIntensity intensity = TrainingIntensity.Normal)
        : base(id)
    {
        ClubId = Guard.Required(clubId, nameof(clubId));
        Update(teamFocus, intensity, []);
    }

    private TrainingPlan()
    {
    }

    public ClubId ClubId { get; private set; }

    public TrainingFocus TeamFocus { get; private set; }

    public TrainingIntensity Intensity { get; private set; }

    public IReadOnlyList<IndividualTraining> Individuals => _individuals;

    /// <summary>The individual focus of <paramref name="playerId"/>, if any.</summary>
    public TrainingFocus? FocusOf(PlayerId playerId) => _individuals.FirstOrDefault(i => i.PlayerId == playerId)?.Focus;

    /// <summary>Replaces the programme; each player at most once.</summary>
    public void Update(TrainingFocus teamFocus, TrainingIntensity intensity, IEnumerable<IndividualTraining> individuals)
    {
        ArgumentNullException.ThrowIfNull(individuals);
        var list = individuals.ToList();
        if (list.Count > MaxIndividuals)
        {
            throw new DomainValidationException($"A training plan can hold at most {MaxIndividuals} individual programmes.");
        }

        if (list.GroupBy(i => i.PlayerId).Any(g => g.Count() > 1))
        {
            throw new DomainValidationException("A player can have only one individual programme.");
        }

        TeamFocus = Guard.Defined(teamFocus, "Team focus");
        Intensity = Guard.Defined(intensity, "Training intensity");
        _individuals = list;
    }
}

/// <summary>Which attributes each focus trains.</summary>
public static class TrainingFocuses
{
    public static readonly IReadOnlyList<TrainingFocus> All = Enum.GetValues<TrainingFocus>();

    /// <summary>Personality traits training cannot change (leadership grows with experience instead).</summary>
    public static bool IsTrainable(PlayerAttribute attribute) => attribute is not (PlayerAttribute.Leadership or PlayerAttribute.WorkRate);

    /// <summary>Whether <paramref name="focus"/> works on <paramref name="attribute"/>. Balanced and Recovery work on none in particular.</summary>
    public static bool Covers(TrainingFocus focus, PlayerAttribute attribute)
    {
        var category = PlayerCatalog.CategoryOf(attribute);
        return focus switch
        {
            TrainingFocus.Shooting => category is AttributeCategory.Shooting or AttributeCategory.Finishing,
            TrainingFocus.Tactical => category == AttributeCategory.Playmaking || attribute is PlayerAttribute.Concentration or PlayerAttribute.Consistency,
            TrainingFocus.Defense => category is AttributeCategory.Defense or AttributeCategory.Rebounding,
            TrainingFocus.Physical => category == AttributeCategory.Physical,
            _ => false,
        };
    }

    /// <summary>The attributes <paramref name="focus"/> works on.</summary>
    public static IEnumerable<PlayerAttribute> AttributesOf(TrainingFocus focus) => PlayerCatalog.Attributes.Where(a => Covers(focus, a));
}
