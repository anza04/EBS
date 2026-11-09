using Ebm.Domain.Common;

namespace Ebm.Domain.Staff;

public readonly record struct StaffMemberId(Guid Value) : IEntityId<StaffMemberId>
{
    public static StaffMemberId New() => new(Guid.CreateVersion7());

    public static StaffMemberId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
