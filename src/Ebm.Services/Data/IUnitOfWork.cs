namespace Ebm.Services.Data;

/// <summary>Commits the changes made through the repositories of the current scope as one transaction.</summary>
public interface IUnitOfWork
{
    /// <returns>The number of records written.</returns>
    /// <exception cref="DataConstraintException">A uniqueness, reference or check constraint was violated.</exception>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
