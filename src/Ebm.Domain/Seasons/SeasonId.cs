using Ebm.Domain.Common;

namespace Ebm.Domain.Seasons;

public readonly record struct SeasonId(Guid Value) : IEntityId<SeasonId>
{
    public static SeasonId New() => new(Guid.CreateVersion7());

    public static SeasonId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
