using Ebm.Domain.Common;

namespace Ebm.Domain.Transfers;

public readonly record struct TransferId(Guid Value) : IEntityId<TransferId>
{
    public static TransferId New() => new(Guid.CreateVersion7());

    public static TransferId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
