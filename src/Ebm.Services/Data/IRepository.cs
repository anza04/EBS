using System.Linq.Expressions;
using Ebm.Domain.Common;

namespace Ebm.Services.Data;

/// <summary>
/// Data access port for one entity type. Changes are written when <see cref="IUnitOfWork.SaveChangesAsync"/> is called.
/// </summary>
/// <remarks>
/// Repositories and the unit of work are scoped: resolve them from one DI scope per unit of work
/// so they share the same change tracking.
/// </remarks>
public interface IRepository<TEntity, TId>
    where TEntity : Entity<TId>
    where TId : struct, IEntityId<TId>
{
    Task<TEntity?> FindAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>Lists entities matching <paramref name="predicate"/>, or all entities when it is <c>null</c>.</summary>
    Task<IReadOnlyList<TEntity>> ListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    void Add(TEntity entity);

    /// <summary>
    /// Marks an entity for deletion. Deleting an entity that other records still refer to
    /// (history, statistics, contracts, …) is refused with <see cref="DataConstraintException"/> on save.
    /// </summary>
    void Remove(TEntity entity);
}
