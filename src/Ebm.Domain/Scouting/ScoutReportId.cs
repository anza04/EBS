using Ebm.Domain.Common;

namespace Ebm.Domain.Scouting;

public readonly record struct ScoutReportId(Guid Value) : IEntityId<ScoutReportId>
{
    public static ScoutReportId New() => new(Guid.CreateVersion7());

    public static ScoutReportId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
