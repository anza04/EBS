using Ebm.Domain.Common;

namespace Ebm.Domain.Countries;

public readonly record struct CountryId(Guid Value) : IEntityId<CountryId>
{
    public static CountryId New() => new(Guid.CreateVersion7());

    public static CountryId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
