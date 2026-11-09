using Ebm.Domain.Common;

namespace Ebm.Domain.Competitions;

public readonly record struct DivisionId(Guid Value) : IEntityId<DivisionId>
{
    public static DivisionId New() => new(Guid.CreateVersion7());

    public static DivisionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
