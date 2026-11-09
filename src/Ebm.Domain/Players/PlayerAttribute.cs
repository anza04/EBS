using System.Text;

namespace Ebm.Domain.Players;

/// <summary>The attribute groups shown in the player profile. Each attribute belongs to exactly one group.</summary>
public enum AttributeCategory
{
    Shooting,
    Finishing,
    Playmaking,
    Defense,
    Rebounding,
    Physical,
    Mental,
}

/// <summary>
/// The player attribute catalogue. The numeric order is fixed: it is the storage order of
/// <see cref="PlayerAttributes"/>, the display order and it groups attributes by category
/// (see <see cref="PlayerCatalog.CategoryOf"/>). New attributes need a migration.
/// </summary>
public enum PlayerAttribute
{
    CloseShot,
    MidRange,
    ThreePoint,
    FreeThrow,
    Layup,
    Dunk,
    PostMoves,
    Passing,
    CourtVision,
    BallHandling,
    DecisionMaking,
    PerimeterDefense,
    InteriorDefense,
    Steal,
    Block,
    DefensivePositioning,
    OffensiveRebound,
    DefensiveRebound,
    Speed,
    Strength,
    Vertical,
    Agility,
    Stamina,
    Concentration,
    Leadership,
    Consistency,
    WorkRate,
}

/// <summary>How often a player chooses an action, independent of how well he performs it (1–100).</summary>
public enum PlayerTendency
{
    ThreePointShot,
    MidRangeShot,
    DriveToBasket,
    PostUp,
    PassFirst,
    Isolation,
    CrashOffensiveGlass,
    DefensiveGambling,
}

/// <summary>Metadata of the attribute and tendency catalogues: groups, display names and import field names.</summary>
public static class PlayerCatalog
{
    public static readonly IReadOnlyList<PlayerAttribute> Attributes = Enum.GetValues<PlayerAttribute>();

    public static readonly IReadOnlyList<PlayerTendency> Tendencies = Enum.GetValues<PlayerTendency>();

    public static readonly IReadOnlyList<AttributeCategory> Categories = Enum.GetValues<AttributeCategory>();

    public static AttributeCategory CategoryOf(PlayerAttribute attribute) => attribute switch
    {
        >= PlayerAttribute.CloseShot and <= PlayerAttribute.FreeThrow => AttributeCategory.Shooting,
        <= PlayerAttribute.PostMoves => AttributeCategory.Finishing,
        <= PlayerAttribute.DecisionMaking => AttributeCategory.Playmaking,
        <= PlayerAttribute.DefensivePositioning => AttributeCategory.Defense,
        <= PlayerAttribute.DefensiveRebound => AttributeCategory.Rebounding,
        <= PlayerAttribute.Stamina => AttributeCategory.Physical,
        <= PlayerAttribute.WorkRate => AttributeCategory.Mental,
        _ => throw new ArgumentOutOfRangeException(nameof(attribute), attribute, "Unknown attribute."),
    };

    public static IEnumerable<PlayerAttribute> AttributesOf(AttributeCategory category) =>
        Attributes.Where(a => CategoryOf(a) == category);

    /// <summary>Field name in import files: the camel-case enum name (<c>threePoint</c>).</summary>
    public static string FieldName<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>Display name: the enum name split into words (<c>Three point</c>).</summary>
    public static string DisplayName<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        var text = new StringBuilder(name.Length + 4).Append(name[0]);
        foreach (var c in name.AsSpan(1))
        {
            if (char.IsUpper(c))
            {
                text.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }
}
