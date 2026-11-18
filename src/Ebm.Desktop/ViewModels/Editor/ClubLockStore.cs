using System.IO;
using Ebm.Domain.Clubs;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>
/// Remembers the locked club between sessions: a one-line text file holding the club's id, or no file when nothing is
/// locked. A missing, empty or unreadable file simply means "no locked club".
/// </summary>
public sealed class ClubLockStore(string filePath)
{
    public string FilePath { get; } = filePath ?? throw new ArgumentNullException(nameof(filePath));

    public ClubId? Load()
    {
        try
        {
            return File.Exists(FilePath) && Guid.TryParse(File.ReadAllText(FilePath).Trim(), out var id) ? ClubId.From(id) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(ClubId? club)
    {
        try
        {
            if (club is { } id)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, id.Value.ToString());
            }
            else if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (IOException)
        {
            // Not remembering the lock is harmless: it only affects the next session's preselection.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
