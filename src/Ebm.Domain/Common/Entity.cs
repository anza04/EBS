namespace Ebm.Domain.Common;

/// <summary>Base type of every persistent entity.</summary>
public abstract class Entity<TId>
    where TId : struct, IEntityId<TId>
{
    protected Entity(TId id)
    {
        Id = Guard.Required(id, nameof(id));
    }

    /// <summary>Used by the persistence layer when materialising entities.</summary>
    protected Entity()
    {
    }

    public TId Id { get; private set; }
}
