using Ebm.Domain.Common;

namespace Ebm.Domain.Clubs;

public readonly record struct ArenaId(Guid Value) : IEntityId<ArenaId>
{
    public static ArenaId New() => new(Guid.CreateVersion7());

    public static ArenaId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
