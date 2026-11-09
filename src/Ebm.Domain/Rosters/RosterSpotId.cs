using Ebm.Domain.Common;

namespace Ebm.Domain.Rosters;

public readonly record struct RosterSpotId(Guid Value) : IEntityId<RosterSpotId>
{
    public static RosterSpotId New() => new(Guid.CreateVersion7());

    public static RosterSpotId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
