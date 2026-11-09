using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Domain.Common;
using Ebm.Services.Data;
using Ebm.Services.Editing;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>
/// One editor tab: a list of records and a form for the selected (or a new) record.
/// Saving goes through <see cref="IDatabaseEditor"/>; rule violations are shown on the page instead of closing the app.
/// </summary>
public abstract partial class EditorPageViewModel<TItem> : ObservableObject
    where TItem : class
{
    [ObservableProperty]
    private TItem? _selectedItem;

    [ObservableProperty]
    private bool _isNew = true;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    protected EditorPageViewModel(IDatabaseEditor editor, EditorLookups lookups, Func<Task> reload)
    {
        Editor = editor;
        Lookups = lookups;
        Reload = reload;
    }

    public EditorLookups Lookups { get; }

    public ObservableCollection<TItem> Items { get; } = [];

    protected IDatabaseEditor Editor { get; }

    private Func<Task> Reload { get; }

    /// <summary>Replaces the list with fresh data and keeps the selection when the record still exists.</summary>
    public void Refresh(EditorSnapshot snapshot, Guid? select = null)
    {
        var keep = select ?? (SelectedItem is { } current ? IdOf(current) : null);
        Items.Clear();
        foreach (var item in ItemsFrom(snapshot))
        {
            Items.Add(item);
        }

        SelectedItem = keep is { } id ? Items.FirstOrDefault(i => IdOf(i) == id) : null;
        if (SelectedItem is null)
        {
            StartNew();
        }
    }

    protected abstract IEnumerable<TItem> ItemsFrom(EditorSnapshot snapshot);

    protected abstract Guid IdOf(TItem item);

    protected abstract void LoadForm(TItem item);

    protected abstract void ClearForm();

    /// <summary>Saves the form and returns the record ID.</summary>
    protected abstract Task<Guid> SaveFormAsync();

    protected static int ParseInt(string? text, string field) =>
        ParseOptionalInt(text, field) ?? throw new DomainValidationException($"{field} is required.");

    protected static int? ParseOptionalInt(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            ? value
            : throw new DomainValidationException($"{field} must be a whole number.");
    }

    protected static long ParseLong(string? text, string field) =>
        long.TryParse(text?.Trim(), NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var value)
            ? value
            : throw new DomainValidationException($"{field} must be a whole number of euros.");

    protected static DateOnly RequiredDate(DateTime? value, string field) =>
        value is { } date ? DateOnly.FromDateTime(date) : throw new DomainValidationException($"{field} is required.");

    protected static TId Required<TId>(TId? value, string field)
        where TId : struct =>
        value ?? throw new DomainValidationException($"Choose a {field}.");

    partial void OnSelectedItemChanged(TItem? value)
    {
        if (value is not null)
        {
            LoadForm(value);
            IsNew = false;
            Message = null;
        }
    }

    [RelayCommand]
    private void StartNew()
    {
        SelectedItem = null;
        ClearForm();
        IsNew = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var id = await SaveFormAsync();
            await Reload();
            SelectedItem = Items.FirstOrDefault(i => IdOf(i) == id);
            Show("Saved.", error: false);
        }
        catch (DomainException ex)
        {
            Show(ex.Message, error: true);
        }
        catch (DataConstraintException ex)
        {
            Show(ex.Message, error: true);
        }
    }

    private void Show(string message, bool error)
    {
        Message = message;
        IsError = error;
    }
}
