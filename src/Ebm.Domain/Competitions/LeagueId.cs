using Ebm.Domain.Common;

namespace Ebm.Domain.Competitions;

public readonly record struct LeagueId(Guid Value) : IEntityId<LeagueId>
{
    public static LeagueId New() => new(Guid.CreateVersion7());

    public static LeagueId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
