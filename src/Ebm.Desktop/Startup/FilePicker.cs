using System.IO;
using Microsoft.Win32;

namespace Ebm.Desktop.Startup;

/// <summary>Asks the user for files. An interface so view models can be tested without dialogs.</summary>
public interface IFilePicker
{
    /// <returns>The chosen files, or <c>null</c> if the user cancelled.</returns>
    IReadOnlyList<string>? PickImportFiles();

    /// <returns>Where to write an exported dataset, or <c>null</c> if the user cancelled.</returns>
    string? PickExportFile(string suggestedName) => null;

    /// <returns>Community packages (<c>.ebmpack</c>) to add to the mods folder, or <c>null</c> if the user cancelled.</returns>
    IReadOnlyList<string>? PickPackageFiles() => null;
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

    public string? PickExportFile(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export the database",
            Filter = "Dataset (*.json)|*.json",
            FileName = suggestedName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string>? PickPackageFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose database packages",
            Filter = "Database packages (*.ebmpack)|*.ebmpack",
            Multiselect = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }
}
