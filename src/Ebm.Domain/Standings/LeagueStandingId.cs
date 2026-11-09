using Ebm.Domain.Common;

namespace Ebm.Domain.Standings;

public readonly record struct LeagueStandingId(Guid Value) : IEntityId<LeagueStandingId>
{
    public static LeagueStandingId New() => new(Guid.CreateVersion7());

    public static LeagueStandingId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
