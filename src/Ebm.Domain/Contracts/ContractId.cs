using Ebm.Domain.Common;

namespace Ebm.Domain.Contracts;

public readonly record struct ContractId(Guid Value) : IEntityId<ContractId>
{
    public static ContractId New() => new(Guid.CreateVersion7());

    public static ContractId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
