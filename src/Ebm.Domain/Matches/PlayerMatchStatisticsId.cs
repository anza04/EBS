using Ebm.Domain.Common;

namespace Ebm.Domain.Matches;

public readonly record struct PlayerMatchStatisticsId(Guid Value) : IEntityId<PlayerMatchStatisticsId>
{
    public static PlayerMatchStatisticsId New() => new(Guid.CreateVersion7());

    public static PlayerMatchStatisticsId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
