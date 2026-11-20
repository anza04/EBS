using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Ebm.Persistence;

/// <summary>
/// File-level operations on game databases (docs/SAVE_FILES.md §4): consistent copies through the SQLite online backup
/// API, integrity checks and the automatic backups kept in the saves folder.
/// </summary>
internal static class DatabaseFiles
{
    /// <summary>Name prefix of the automatic backups; they are ordinary saves, listed and loaded like the others.</summary>
    public const string AutoBackupPrefix = "Autobackup ";

    /// <summary>Automatic backups kept; older ones are deleted when a new one is made.</summary>
    public const int AutoBackupsKept = 5;

    /// <summary>Copies <paramref name="sourcePath"/> over <paramref name="targetPath"/> (created when missing), page by page.</summary>
    public static void Copy(string sourcePath, string targetPath)
    {
        SqliteConnection.ClearAllPools();
        using (var source = Open(sourcePath, SqliteOpenMode.ReadWriteCreate))
        using (var target = Open(targetPath, SqliteOpenMode.ReadWriteCreate))
        {
            source.BackupDatabase(target);
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// <c>true</c> when the file is a readable SQLite database that passes <c>PRAGMA quick_check</c> (page structure,
    /// records and index consistency; foreign keys are checked by the game's own validation).
    /// </summary>
    public static bool IsIntact(string path)
    {
        try
        {
            using var connection = Open(path, SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check";
            using var reader = command.ExecuteReader();
            return reader.Read() && reader.GetString(0) == "ok" && !reader.Read();
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary><c>true</c> when the database holds a career (a game worth backing up).</summary>
    public static bool HasCareer(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var connection = Open(path, SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM Careers)";
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>
    /// Copies the live game to <c>Autobackup &lt;timestamp&gt;.ebmsave</c> when it holds a career, then keeps only the
    /// newest <see cref="AutoBackupsKept"/> automatic backups. Returns the backup's path, or <c>null</c> when none was needed.
    /// </summary>
    public static string? AutoBackup(string databasePath, string savesDirectory, DateTime now)
    {
        if (!HasCareer(databasePath))
        {
            return null;
        }

        Directory.CreateDirectory(savesDirectory);
        var path = Path.Combine(savesDirectory, $"{AutoBackupPrefix}{now:yyyy-MM-dd HHmmss}{SaveGameStore.Extension}");
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(savesDirectory, $"{AutoBackupPrefix}{now:yyyy-MM-dd HHmmss} {n}{SaveGameStore.Extension}");
        }

        var temporary = path + ".tmp";
        File.Delete(temporary);
        Copy(databasePath, temporary);
        File.Move(temporary, path);

        foreach (var old in AutoBackups(savesDirectory).Skip(AutoBackupsKept))
        {
            File.Delete(old);
        }

        return path;
    }

    /// <summary>Automatic backups, newest first.</summary>
    public static IReadOnlyList<string> AutoBackups(string savesDirectory) =>
        Directory.Exists(savesDirectory)
            ? [.. Directory.EnumerateFiles(savesDirectory, AutoBackupPrefix + "*" + SaveGameStore.Extension).OrderByDescending(File.GetLastWriteTimeUtc).ThenByDescending(p => p, StringComparer.Ordinal)]
            : [];

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
