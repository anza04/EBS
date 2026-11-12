using Ebm.Domain.Common;

namespace Ebm.Domain.Careers;

public readonly record struct CareerId(Guid Value) : IEntityId<CareerId>
{
    public static CareerId New() => new(Guid.CreateVersion7());

    public static CareerId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
