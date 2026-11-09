using System.IO;
using Microsoft.Win32;

namespace Ebm.Desktop.Startup;

/// <summary>Asks the user for files. An interface so view models can be tested without dialogs.</summary>
public interface IFilePicker
{
    /// <returns>The chosen files, or <c>null</c> if the user cancelled.</returns>
    IReadOnlyList<string>? PickImportFiles();
}

internal sealed class FilePicker : IFilePicker
{
    public IReadOnlyList<string>? PickImportFiles()
    {
        var dataFolder = Path.Combine(AppContext.BaseDirectory, "data");
        var dialog = new OpenFileDialog
        {
            Title = "Choose files to import",
            Filter = "Import files (*.csv;*.json)|*.csv;*.json|All files (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = Directory.Exists(dataFolder) ? dataFolder : string.Empty,
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }
}
