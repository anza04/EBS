using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Domain.Common;
using Ebm.Services.Saves;

namespace Ebm.Desktop.ViewModels.Saves;

/// <summary>The saved-games screen (Step 14): save the game, load, delete and start a new game.</summary>
public sealed partial class SaveGamesViewModel : ObservableObject
{
    private readonly ISaveGameService _saves;
    private readonly Func<Task> _reloadAll;

    [ObservableProperty]
    private IReadOnlyList<SaveGameInfo> _saveGames = [];

    [ObservableProperty]
    private SaveGameInfo? _selectedSave;

    [ObservableProperty]
    private string _saveName = "Career";

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    public SaveGamesViewModel(ISaveGameService saves, Func<Task> reloadAll)
    {
        ArgumentNullException.ThrowIfNull(saves);
        ArgumentNullException.ThrowIfNull(reloadAll);
        _saves = saves;
        _reloadAll = reloadAll;
    }

    public string Folder => _saves.SavesDirectory;

    public async Task RefreshAsync()
    {
        var keep = SelectedSave?.Name;
        SaveGames = await _saves.ListAsync();
        SelectedSave = SaveGames.FirstOrDefault(s => s.Name == keep);
    }

    partial void OnSelectedSaveChanged(SaveGameInfo? value)
    {
        if (value is not null)
        {
            SaveName = value.Name;
        }
    }

    /// <summary>Saves under <see cref="SaveName"/>, replacing a save of that name.</summary>
    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var info = await _saves.SaveAsync(SaveName, overwrite: true);
        await RefreshAsync();
        return $"Game saved as '{info.Name}'.";
    });

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        if (SelectedSave is not { } save)
        {
            return "Choose a save to load.";
        }

        await _saves.LoadAsync(save.Name);
        await _reloadAll();
        return $"'{save.Name}' loaded.";
    });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (SelectedSave is not { } save)
        {
            return "Choose a save to delete.";
        }

        await _saves.DeleteAsync(save.Name);
        await RefreshAsync();
        return $"'{save.Name}' deleted.";
    });

    /// <summary>Empties the game database; saves are kept.</summary>
    [RelayCommand]
    private Task NewGameAsync() => RunAsync(async () =>
    {
        await _saves.NewGameAsync();
        await _reloadAll();
        return "New game: import a dataset (Import tab), then start a career (Career tab).";
    });

    private async Task RunAsync(Func<Task<string>> action)
    {
        try
        {
            Message = await action();
            IsError = false;
        }
        catch (DomainException ex)
        {
            Message = ex.Message;
            IsError = true;
        }
        catch (System.IO.IOException ex)
        {
            Message = $"The save file could not be written or read: {ex.Message}";
            IsError = true;
        }
    }
}
