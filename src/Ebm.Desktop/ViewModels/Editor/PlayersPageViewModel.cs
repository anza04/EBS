using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Services.Editing;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>Adds and edits players, including their attribute ratings.</summary>
public sealed partial class PlayersPageViewModel : EditorPageViewModel<Player>
{
    [ObservableProperty]
    private string _firstName = string.Empty;

    [ObservableProperty]
    private string _lastName = string.Empty;

    [ObservableProperty]
    private DateTime? _dateOfBirth;

    [ObservableProperty]
    private CountryId? _nationalityId;

    [ObservableProperty]
    private string _heightCm = string.Empty;

    [ObservableProperty]
    private string _weightKg = string.Empty;

    [ObservableProperty]
    private Position _position;

    public PlayersPageViewModel(IDatabaseEditor editor, EditorLookups lookups, Func<Task> reload)
        : base(editor, lookups, reload)
    {
    }

    /// <summary>The attribute ratings in display order, edited as text.</summary>
    public IReadOnlyList<RatingField> Ratings { get; } =
    [
        new("Shooting"), new("Inside scoring"), new("Passing"), new("Ball handling"),
        new("Rebounding"), new("Defense"), new("Athleticism"), new("Basketball IQ"),
    ];

    protected override IEnumerable<Player> ItemsFrom(EditorSnapshot snapshot) =>
        snapshot.Players.OrderBy(p => p.LastName).ThenBy(p => p.FirstName);

    protected override Guid IdOf(Player item) => item.Id.Value;

    protected override void LoadForm(Player item)
    {
        FirstName = item.FirstName;
        LastName = item.LastName;
        DateOfBirth = item.DateOfBirth.ToDateTime(TimeOnly.MinValue);
        NationalityId = item.NationalityId;
        HeightCm = item.HeightCm.ToString(CultureInfo.CurrentCulture);
        WeightKg = item.WeightKg.ToString(CultureInfo.CurrentCulture);
        Position = item.Position;
        var a = item.Attributes;
        int[] values = [a.Shooting, a.InsideScoring, a.Passing, a.BallHandling, a.Rebounding, a.Defense, a.Athleticism, a.BasketballIq];
        for (var i = 0; i < values.Length; i++)
        {
            Ratings[i].Value = values[i].ToString(CultureInfo.CurrentCulture);
        }
    }

    protected override void ClearForm()
    {
        FirstName = LastName = HeightCm = WeightKg = string.Empty;
        DateOfBirth = null;
        NationalityId = null;
        Position = Position.PointGuard;
        foreach (var rating in Ratings)
        {
            rating.Value = "50";
        }
    }

    protected override async Task<Guid> SaveFormAsync()
    {
        var r = Ratings.Select(f => ParseInt(f.Value, f.Label)).ToArray();
        var draft = new PlayerDraft(
            IsNew ? null : SelectedItem?.Id,
            FirstName,
            LastName,
            RequiredDate(DateOfBirth, "Date of birth"),
            Required(NationalityId, "nationality"),
            ParseInt(HeightCm, "Height (cm)"),
            ParseInt(WeightKg, "Weight (kg)"),
            Position,
            new PlayerAttributes(r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7]));
        return (await Editor.SavePlayerAsync(draft)).Value;
    }
}

/// <summary>One editable attribute rating.</summary>
public sealed partial class RatingField(string label) : ObservableObject
{
    [ObservableProperty]
    private string _value = "50";

    public string Label { get; } = label;
}
