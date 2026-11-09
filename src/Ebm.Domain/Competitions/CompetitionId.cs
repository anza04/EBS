using Ebm.Domain.Common;

namespace Ebm.Domain.Competitions;

public readonly record struct CompetitionId(Guid Value) : IEntityId<CompetitionId>
{
    public static CompetitionId New() => new(Guid.CreateVersion7());

    public static CompetitionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
