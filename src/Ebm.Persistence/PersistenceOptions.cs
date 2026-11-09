namespace Ebm.Persistence;

/// <summary>Settings of the persistence layer, supplied by the composition root.</summary>
/// <param name="DatabasePath">Absolute path of the SQLite game database file.</param>
public sealed record PersistenceOptions(string DatabasePath)
{
    /// <summary>Connection string with foreign-key enforcement switched on.</summary>
    public string ConnectionString => $"Data Source={DatabasePath};Foreign Keys=True";
}
