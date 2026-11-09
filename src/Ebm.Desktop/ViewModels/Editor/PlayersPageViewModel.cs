using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Services.Editing;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>Adds and edits players: personal details, profile, condition, attributes and tendencies.</summary>
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
    private string _wingspanCm = string.Empty;

    [ObservableProperty]
    private Position _position;

    [ObservableProperty]
    private string _experience = string.Empty;

    [ObservableProperty]
    private string _reputation = string.Empty;

    [ObservableProperty]
    private string _potential = string.Empty;

    [ObservableProperty]
    private string _fitness = string.Empty;

    [ObservableProperty]
    private string _morale = string.Empty;

    [ObservableProperty]
    private InjuryStatus _injuryStatus;

    [ObservableProperty]
    private DateTime? _injuryReturnDate;

    public PlayersPageViewModel(IDatabaseEditor editor, EditorLookups lookups, Func<Task> reload)
        : base(editor, lookups, reload)
    {
        AttributeGroups =
        [
            .. PlayerCatalog.Categories.Select(c => new RatingGroup(
                PlayerCatalog.DisplayName(c),
                [.. PlayerCatalog.AttributesOf(c).Select(a => Ratings[(int)a])])),
        ];
    }

    /// <summary>One field per attribute, in catalogue order.</summary>
    public IReadOnlyList<RatingField> Ratings { get; } = [.. PlayerCatalog.Attributes.Select(a => new RatingField(PlayerCatalog.DisplayName(a)))];

    /// <summary>The attribute fields grouped by category, for display.</summary>
    public IReadOnlyList<RatingGroup> AttributeGroups { get; }

    /// <summary>One field per tendency, in catalogue order.</summary>
    public IReadOnlyList<RatingField> Tendencies { get; } = [.. PlayerCatalog.Tendencies.Select(t => new RatingField(PlayerCatalog.DisplayName(t)))];

    /// <summary>One check box per position.</summary>
    public IReadOnlyList<PositionChoice> SecondaryPositions { get; } = [.. Enum.GetValues<Position>().Select(p => new PositionChoice(p))];

    public IReadOnlyList<InjuryStatus> InjuryStatuses { get; } = Enum.GetValues<InjuryStatus>();

    protected override IEnumerable<Player> ItemsFrom(EditorSnapshot snapshot) =>
        snapshot.Players.OrderBy(p => p.LastName).ThenBy(p => p.FirstName);

    protected override Guid IdOf(Player item) => item.Id.Value;

    protected override void LoadForm(Player item)
    {
        FirstName = item.FirstName;
        LastName = item.LastName;
        DateOfBirth = item.DateOfBirth.ToDateTime(TimeOnly.MinValue);
        NationalityId = item.NationalityId;
        HeightCm = Text(item.HeightCm);
        WeightKg = Text(item.WeightKg);
        WingspanCm = Text(item.WingspanCm);
        Position = item.Position;
        foreach (var choice in SecondaryPositions)
        {
            choice.IsChecked = item.SecondaryPositions.Contains(choice.Position);
        }

        Experience = Text(item.Experience);
        Reputation = Text(item.Reputation);
        Potential = Text(item.Potential);
        Fitness = Text(item.Fitness);
        Morale = Text(item.Morale);
        InjuryStatus = item.InjuryStatus;
        InjuryReturnDate = item.InjuryReturnDate?.ToDateTime(TimeOnly.MinValue);
        Fill(Ratings, item.Attributes.ToDictionary().Values);
        Fill(Tendencies, item.Tendencies.ToDictionary().Values);
    }

    protected override void ClearForm()
    {
        FirstName = LastName = HeightCm = WeightKg = WingspanCm = string.Empty;
        DateOfBirth = null;
        NationalityId = null;
        Position = Position.PointGuard;
        foreach (var choice in SecondaryPositions)
        {
            choice.IsChecked = false;
        }

        Experience = Text(Player.DefaultExperience);
        Reputation = Text(Player.DefaultReputation);
        Potential = Text(Player.DefaultPotential);
        Fitness = Text(Player.DefaultFitness);
        Morale = Text(Player.DefaultMorale);
        InjuryStatus = InjuryStatus.Healthy;
        InjuryReturnDate = null;
        Fill(Ratings, Ratings.Select(_ => 50));
        Fill(Tendencies, Tendencies.Select(_ => 50));
    }

    protected override async Task<Guid> SaveFormAsync()
    {
        var height = ParseInt(HeightCm, "Height (cm)");
        var draft = new PlayerDraft(
            IsNew ? null : SelectedItem?.Id,
            FirstName,
            LastName,
            RequiredDate(DateOfBirth, "Date of birth"),
            Required(NationalityId, "nationality"),
            height,
            ParseInt(WeightKg, "Weight (kg)"),
            ParseOptionalInt(WingspanCm, "Wingspan (cm)") ?? height,
            Position,
            PositionSet.Of(SecondaryPositions.Where(c => c.IsChecked && c.Position != Position).Select(c => c.Position)),
            ParseInt(Experience, "Experience"),
            ParseInt(Reputation, "Reputation"),
            ParseInt(Potential, "Potential"),
            ParseInt(Fitness, "Fitness"),
            ParseInt(Morale, "Morale"),
            InjuryStatus,
            InjuryReturnDate is { } date ? DateOnly.FromDateTime(date) : null,
            ReadAttributes(),
            new PlayerTendencies(PlayerCatalog.Tendencies.ToDictionary(t => t, t => ParseInt(Tendencies[(int)t].Value, Tendencies[(int)t].Label))));
        return (await Editor.SavePlayerAsync(draft)).Value;
    }

    /// <summary>Replaces the tendencies with the ones suggested by the current attribute values.</summary>
    [RelayCommand]
    private void SuggestTendencies()
    {
        try
        {
            Fill(Tendencies, PlayerTendencies.Suggest(ReadAttributes()).ToDictionary().Values);
            Message = null;
        }
        catch (Ebm.Domain.Common.DomainException ex)
        {
            Message = ex.Message;
            IsError = true;
        }
    }

    private PlayerAttributes ReadAttributes() =>
        new(PlayerCatalog.Attributes.ToDictionary(a => a, a => ParseInt(Ratings[(int)a].Value, Ratings[(int)a].Label)));

    private static void Fill(IReadOnlyList<RatingField> fields, IEnumerable<int> values)
    {
        foreach (var (field, value) in fields.Zip(values))
        {
            field.Value = Text(value);
        }
    }

    private static string Text(int value) => value.ToString(CultureInfo.CurrentCulture);
}

/// <summary>One editable rating.</summary>
public sealed partial class RatingField(string label) : ObservableObject
{
    [ObservableProperty]
    private string _value = "50";

    public string Label { get; } = label;
}

/// <summary>A titled group of rating fields (one attribute category).</summary>
public sealed record RatingGroup(string Title, IReadOnlyList<RatingField> Fields);

/// <summary>A position check box.</summary>
public sealed partial class PositionChoice(Position position) : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

    public Position Position { get; } = position;
}
