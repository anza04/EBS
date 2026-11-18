using Ebm.Domain.Common;
using Ebm.Domain.Players;

namespace Ebm.Domain.Chemistry;

public readonly record struct PlayerRelationshipId(Guid Value) : IEntityId<PlayerRelationshipId>
{
    public static PlayerRelationshipId New() => new(Guid.CreateVersion7());

    public static PlayerRelationshipId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// How well two players get on, −100 (feud) … +100 (close friends); 0 = strangers (docs/CHEMISTRY.md §4). One row per
/// pair, stored in a fixed order (<see cref="PlayerAId"/> has the smaller id) and kept when they stop being teammates.
/// </summary>
public sealed class PlayerRelationship : Entity<PlayerRelationshipId>
{
    public const int Min = -100;
    public const int Max = 100;

    public PlayerRelationship(PlayerRelationshipId id, PlayerId first, PlayerId second, DateOnly today)
        : base(id)
    {
        (PlayerAId, PlayerBId) = Order(Guard.Required(first, nameof(first)), Guard.Required(second, nameof(second)));
        if (PlayerAId == PlayerBId)
        {
            throw new DomainValidationException("A player has no relationship with himself.");
        }

        UpdatedOn = today;
    }

    private PlayerRelationship()
    {
    }

    public PlayerId PlayerAId { get; private set; }

    public PlayerId PlayerBId { get; private set; }

    public int Score { get; private set; }

    public DateOnly UpdatedOn { get; private set; }

    /// <summary>The pair in stored order.</summary>
    public static (PlayerId A, PlayerId B) Order(PlayerId first, PlayerId second) =>
        first.Value.CompareTo(second.Value) <= 0 ? (first, second) : (second, first);

    public PlayerId Other(PlayerId player) => player == PlayerAId ? PlayerBId : PlayerAId;

    public void Adjust(int delta, DateOnly today)
    {
        Score = Math.Clamp(Score + delta, Min, Max);
        UpdatedOn = today;
    }
}
