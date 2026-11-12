namespace Ebm.Services.Saves;

/// <summary>
/// Save files (docs/SAVE_FILES.md). The game database in use is the live game; a save is a copy of it in the saves
/// folder. Loading copies a save over the live game, upgrades it to the current schema and reloads the career date.
/// Problems the player can fix throw <see cref="Domain.Common.DomainException"/>.
/// </summary>
public interface ISaveGameService
{
    /// <summary>The saves folder.</summary>
    string SavesDirectory { get; }

    /// <summary>Every save, newest first.</summary>
    Task<IReadOnlyList<SaveGameInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Copies the live game to a save called <paramref name="name"/>. An existing save is replaced only with <paramref name="overwrite"/>.</summary>
    Task<SaveGameInfo> SaveAsync(string name, bool overwrite = false, CancellationToken cancellationToken = default);

    /// <summary>Replaces the live game with the save, upgrades it and loads its career. Unsaved progress is lost.</summary>
    Task LoadAsync(string name, CancellationToken cancellationToken = default);

    Task DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Starts over with an empty game database (import a dataset, then start a career). Unsaved progress is lost.</summary>
    Task NewGameAsync(CancellationToken cancellationToken = default);
}

/// <param name="Career">"Manager · Club · date" of the save's career, or <c>null</c> when it has none.</param>
/// <param name="SchemaVersion">Latest migration of the save; older saves are upgraded when loaded.</param>
public sealed record SaveGameInfo(string Name, string FilePath, DateTime SavedAt, long SizeBytes, string? Career, string SchemaVersion);
