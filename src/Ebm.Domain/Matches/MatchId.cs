using Ebm.Domain.Common;

namespace Ebm.Domain.Matches;

public readonly record struct MatchId(Guid Value) : IEntityId<MatchId>
{
    public static MatchId New() => new(Guid.CreateVersion7());

    public static MatchId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
