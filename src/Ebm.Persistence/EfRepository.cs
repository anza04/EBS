using System.Linq.Expressions;
using Ebm.Domain.Common;
using Ebm.Services.Data;
using Microsoft.EntityFrameworkCore;

namespace Ebm.Persistence;

internal sealed class EfRepository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : Entity<TId>
    where TId : struct, IEntityId<TId>
{
    private readonly DbSet<TEntity> _set;

    public EfRepository(EbmDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _set = context.Set<TEntity>();
    }

    public async Task<TEntity?> FindAsync(TId id, CancellationToken cancellationToken = default) =>
        await _set.FindAsync([id], cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<TEntity>> ListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> query = _set;
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Add(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _set.Add(entity);
    }

    public void Remove(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _set.Remove(entity);
    }
}
