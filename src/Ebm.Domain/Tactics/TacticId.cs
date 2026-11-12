using Ebm.Domain.Common;

namespace Ebm.Domain.Tactics;

public readonly record struct TacticId(Guid Value) : IEntityId<TacticId>
{
    public static TacticId New() => new(Guid.CreateVersion7());

    public static TacticId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
