using Ebm.Domain.Common;

namespace Ebm.Domain.Players;

public readonly record struct PlayerId(Guid Value) : IEntityId<PlayerId>
{
    public static PlayerId New() => new(Guid.CreateVersion7());

    public static PlayerId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
