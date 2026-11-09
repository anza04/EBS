using Ebm.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ebm.Persistence;

/// <summary>Stores a strongly typed ID as its underlying <see cref="Guid"/>.</summary>
internal sealed class EntityIdConverter<TId> : ValueConverter<TId, Guid>
    where TId : struct, IEntityId<TId>
{
    // Expression trees cannot call static abstract members directly; a delegate field can be invoked.
    private static readonly Func<Guid, TId> Create = TId.From;

    public EntityIdConverter()
        : base(id => id.Value, value => Create(value))
    {
    }
}
