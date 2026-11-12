namespace Ebm.Persistence;

/// <summary>Settings of the persistence layer, supplied by the composition root.</summary>
/// <param name="DatabasePath">Absolute path of the SQLite game database file.</param>
/// <param name="DatabasePath">Absolute path of the SQLite game database file (the live game).</param>
public sealed record PersistenceOptions(string DatabasePath)
{
    public PersistenceOptions(string databasePath, string savesDirectory)
        : this(databasePath)
    {
        SavesDirectory = savesDirectory;
    }

    /// <summary>Folder of the save files; by default <c>saves</c> next to the database folder.</summary>
    public string SavesDirectory { get; init; } = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(DatabasePath)) ?? ".", "saves");

    /// <summary>Connection string with foreign-key enforcement switched on.</summary>
    public string ConnectionString => $"Data Source={DatabasePath};Foreign Keys=True";
}
