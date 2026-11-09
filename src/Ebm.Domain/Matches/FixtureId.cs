using Ebm.Domain.Common;

namespace Ebm.Domain.Matches;

public readonly record struct FixtureId(Guid Value) : IEntityId<FixtureId>
{
    public static FixtureId New() => new(Guid.CreateVersion7());

    public static FixtureId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
