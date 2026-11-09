using Ebm.Domain.Common;

namespace Ebm.Domain.Clubs;

public readonly record struct ClubId(Guid Value) : IEntityId<ClubId>
{
    public static ClubId New() => new(Guid.CreateVersion7());

    public static ClubId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
