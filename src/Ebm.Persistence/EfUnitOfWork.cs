using Ebm.Services.Data;
using Microsoft.EntityFrameworkCore;

namespace Ebm.Persistence;

internal sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly EbmDbContext _context;

    public EfUnitOfWork(EbmDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            var reason = ex.InnerException?.Message ?? ex.Message;
            throw new DataConstraintException($"The changes were not saved because they break a database rule: {reason}", ex);
        }
    }
}
