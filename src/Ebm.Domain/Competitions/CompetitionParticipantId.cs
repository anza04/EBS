using Ebm.Domain.Common;

namespace Ebm.Domain.Competitions;

public readonly record struct CompetitionParticipantId(Guid Value) : IEntityId<CompetitionParticipantId>
{
    public static CompetitionParticipantId New() => new(Guid.CreateVersion7());

    public static CompetitionParticipantId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
