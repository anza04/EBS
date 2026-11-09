using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Hosting;
using Microsoft.Extensions.Logging;

namespace Ebm.Desktop.ViewModels;

/// <summary>
/// View model of the main window: build information and the database editor. Navigation between
/// game screens is added once there are screens to navigate to.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ILogger<ShellViewModel> _logger;

    public ShellViewModel(ApplicationInfo info, AppPaths paths, DatabaseEditorViewModel editor, ILogger<ShellViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        Editor = editor;
        ProductName = info.ProductName;
        VersionText = $"Version {info.Version}";
        DataFolder = paths.DataRoot;
        LogsFolder = paths.LogsDirectory;
    }

    public string ProductName { get; }

    public string VersionText { get; }

    public string WindowTitle => $"{ProductName} {VersionText}";

    /// <summary>The database editor shown in the content area.</summary>
    public DatabaseEditorViewModel Editor { get; }

    public string DataFolder { get; }

    public string LogsFolder { get; }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        _logger.LogInformation("Opening logs folder {LogsFolder}", LogsFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LogsFolder}\"") { UseShellExecute = true });
    }
}
