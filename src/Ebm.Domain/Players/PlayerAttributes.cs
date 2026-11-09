namespace Ebm.Domain.Players;

/// <summary>
/// A player's ability ratings (1–100), one per <see cref="PlayerAttribute"/>. Immutable: edits create a new set.
/// Definitions and ranges: docs/PLAYER_MODEL.md.
/// </summary>
public sealed class PlayerAttributes : RatingSet<PlayerAttribute>
{
    public PlayerAttributes(IReadOnlyDictionary<PlayerAttribute, int> values)
        : base(values)
    {
    }

    private PlayerAttributes()
    {
    }

    /// <summary>Every attribute at the same value (mock data and tests).</summary>
    public static PlayerAttributes Uniform(int value) => new(PlayerCatalog.Attributes.ToDictionary(a => a, _ => value));

    /// <summary>A copy with one attribute changed.</summary>
    public PlayerAttributes With(PlayerAttribute attribute, int value) =>
        new(new Dictionary<PlayerAttribute, int>(ToDictionary()) { [attribute] = value });

    public int CloseShot { get => Get(PlayerAttribute.CloseShot); private set => Set(PlayerAttribute.CloseShot, value); }

    public int MidRange { get => Get(PlayerAttribute.MidRange); private set => Set(PlayerAttribute.MidRange, value); }

    public int ThreePoint { get => Get(PlayerAttribute.ThreePoint); private set => Set(PlayerAttribute.ThreePoint, value); }

    public int FreeThrow { get => Get(PlayerAttribute.FreeThrow); private set => Set(PlayerAttribute.FreeThrow, value); }

    public int Layup { get => Get(PlayerAttribute.Layup); private set => Set(PlayerAttribute.Layup, value); }

    public int Dunk { get => Get(PlayerAttribute.Dunk); private set => Set(PlayerAttribute.Dunk, value); }

    public int PostMoves { get => Get(PlayerAttribute.PostMoves); private set => Set(PlayerAttribute.PostMoves, value); }

    public int Passing { get => Get(PlayerAttribute.Passing); private set => Set(PlayerAttribute.Passing, value); }

    public int CourtVision { get => Get(PlayerAttribute.CourtVision); private set => Set(PlayerAttribute.CourtVision, value); }

    public int BallHandling { get => Get(PlayerAttribute.BallHandling); private set => Set(PlayerAttribute.BallHandling, value); }

    public int DecisionMaking { get => Get(PlayerAttribute.DecisionMaking); private set => Set(PlayerAttribute.DecisionMaking, value); }

    public int PerimeterDefense { get => Get(PlayerAttribute.PerimeterDefense); private set => Set(PlayerAttribute.PerimeterDefense, value); }

    public int InteriorDefense { get => Get(PlayerAttribute.InteriorDefense); private set => Set(PlayerAttribute.InteriorDefense, value); }

    public int Steal { get => Get(PlayerAttribute.Steal); private set => Set(PlayerAttribute.Steal, value); }

    public int Block { get => Get(PlayerAttribute.Block); private set => Set(PlayerAttribute.Block, value); }

    public int DefensivePositioning { get => Get(PlayerAttribute.DefensivePositioning); private set => Set(PlayerAttribute.DefensivePositioning, value); }

    public int OffensiveRebound { get => Get(PlayerAttribute.OffensiveRebound); private set => Set(PlayerAttribute.OffensiveRebound, value); }

    public int DefensiveRebound { get => Get(PlayerAttribute.DefensiveRebound); private set => Set(PlayerAttribute.DefensiveRebound, value); }

    public int Speed { get => Get(PlayerAttribute.Speed); private set => Set(PlayerAttribute.Speed, value); }

    public int Strength { get => Get(PlayerAttribute.Strength); private set => Set(PlayerAttribute.Strength, value); }

    public int Vertical { get => Get(PlayerAttribute.Vertical); private set => Set(PlayerAttribute.Vertical, value); }

    public int Agility { get => Get(PlayerAttribute.Agility); private set => Set(PlayerAttribute.Agility, value); }

    public int Stamina { get => Get(PlayerAttribute.Stamina); private set => Set(PlayerAttribute.Stamina, value); }

    public int Concentration { get => Get(PlayerAttribute.Concentration); private set => Set(PlayerAttribute.Concentration, value); }

    public int Leadership { get => Get(PlayerAttribute.Leadership); private set => Set(PlayerAttribute.Leadership, value); }

    public int Consistency { get => Get(PlayerAttribute.Consistency); private set => Set(PlayerAttribute.Consistency, value); }

    public int WorkRate { get => Get(PlayerAttribute.WorkRate); private set => Set(PlayerAttribute.WorkRate, value); }
}
