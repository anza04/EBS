using Ebm.Domain.Common;

namespace Ebm.Domain.Scouting;

public readonly record struct ScoutingAssignmentId(Guid Value) : IEntityId<ScoutingAssignmentId>
{
    public static ScoutingAssignmentId New() => new(Guid.CreateVersion7());

    public static ScoutingAssignmentId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
