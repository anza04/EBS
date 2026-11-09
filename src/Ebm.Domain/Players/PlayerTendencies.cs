namespace Ebm.Domain.Players;

/// <summary>
/// How often a player chooses each action (1–100), one value per <see cref="PlayerTendency"/>. Independent of
/// ability: a poor shooter may still like to shoot. Read by the match engine (Step 10).
/// </summary>
public sealed class PlayerTendencies : RatingSet<PlayerTendency>
{
    /// <summary>Suggested tendency = weighted mean of these attributes (docs/PLAYER_MODEL.md §5). Mirrored by migration PlayerModel.</summary>
    public static readonly IReadOnlyDictionary<PlayerTendency, (PlayerAttribute Attribute, int Weight)[]> SuggestionWeights =
        new Dictionary<PlayerTendency, (PlayerAttribute, int)[]>
        {
            [PlayerTendency.ThreePointShot] = [(PlayerAttribute.ThreePoint, 3), (PlayerAttribute.MidRange, 1)],
            [PlayerTendency.MidRangeShot] = [(PlayerAttribute.MidRange, 3), (PlayerAttribute.CloseShot, 1)],
            [PlayerTendency.DriveToBasket] = [(PlayerAttribute.Layup, 2), (PlayerAttribute.BallHandling, 1), (PlayerAttribute.Speed, 1)],
            [PlayerTendency.PostUp] = [(PlayerAttribute.PostMoves, 3), (PlayerAttribute.Strength, 1)],
            [PlayerTendency.PassFirst] = [(PlayerAttribute.Passing, 1), (PlayerAttribute.CourtVision, 1)],
            [PlayerTendency.Isolation] = [(PlayerAttribute.BallHandling, 2), (PlayerAttribute.MidRange, 1)],
            [PlayerTendency.CrashOffensiveGlass] = [(PlayerAttribute.OffensiveRebound, 3), (PlayerAttribute.WorkRate, 1)],
            [PlayerTendency.DefensiveGambling] = [(PlayerAttribute.Steal, 2), (PlayerAttribute.Block, 1)],
        };

    public PlayerTendencies(IReadOnlyDictionary<PlayerTendency, int> values)
        : base(values)
    {
    }

    private PlayerTendencies()
    {
    }

    /// <summary>Tendencies that follow the player's strengths; the default for new and imported players.</summary>
    public static PlayerTendencies Suggest(PlayerAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return new(PlayerCatalog.Tendencies.ToDictionary(t => t, t => Suggest(attributes, t)));
    }

    public static int Suggest(PlayerAttributes attributes, PlayerTendency tendency)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var weights = SuggestionWeights[tendency];
        var mean = (double)weights.Sum(w => attributes[w.Attribute] * w.Weight) / weights.Sum(w => w.Weight);
        return (int)Math.Round(mean, MidpointRounding.AwayFromZero);
    }

    /// <summary>A copy with one tendency changed.</summary>
    public PlayerTendencies With(PlayerTendency tendency, int value) =>
        new(new Dictionary<PlayerTendency, int>(ToDictionary()) { [tendency] = value });

    public int ThreePointShot { get => Get(PlayerTendency.ThreePointShot); private set => Set(PlayerTendency.ThreePointShot, value); }

    public int MidRangeShot { get => Get(PlayerTendency.MidRangeShot); private set => Set(PlayerTendency.MidRangeShot, value); }

    public int DriveToBasket { get => Get(PlayerTendency.DriveToBasket); private set => Set(PlayerTendency.DriveToBasket, value); }

    public int PostUp { get => Get(PlayerTendency.PostUp); private set => Set(PlayerTendency.PostUp, value); }

    public int PassFirst { get => Get(PlayerTendency.PassFirst); private set => Set(PlayerTendency.PassFirst, value); }

    public int Isolation { get => Get(PlayerTendency.Isolation); private set => Set(PlayerTendency.Isolation, value); }

    public int CrashOffensiveGlass { get => Get(PlayerTendency.CrashOffensiveGlass); private set => Set(PlayerTendency.CrashOffensiveGlass, value); }

    public int DefensiveGambling { get => Get(PlayerTendency.DefensiveGambling); private set => Set(PlayerTendency.DefensiveGambling, value); }
}
