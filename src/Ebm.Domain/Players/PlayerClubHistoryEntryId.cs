using Ebm.Domain.Common;

namespace Ebm.Domain.Players;

public readonly record struct PlayerClubHistoryEntryId(Guid Value) : IEntityId<PlayerClubHistoryEntryId>
{
    public static PlayerClubHistoryEntryId New() => new(Guid.CreateVersion7());

    public static PlayerClubHistoryEntryId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
