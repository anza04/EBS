using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Chemistry;
using Ebm.Domain.Clubs;
using Ebm.Domain.Players;
using Ebm.Services.Chemistry;

namespace Ebm.Desktop.ViewModels.Chemistry;

/// <summary>One first-team player in the dressing room.</summary>
public sealed class DressingRoomRowViewModel(SquadMember member)
{
    public int Rank => member.Place.Rank;

    public string Name => member.Place.Player.FullName + (member.Place.IsCaptain ? " (C)" : member.Place.IsLeader ? " (L)" : string.Empty);

    public string Position => PlayerCatalog.DisplayName(member.Place.Player.Position);

    public string Role => PlayerCatalog.DisplayName(member.Place.Role);

    public string Expected => member.Place.ExpectedMinutes + (member.Promised ? " (promised)" : string.Empty);

    public string Recent => member.RecentMinutes is { } m ? m.ToString("0.0", CultureInfo.CurrentCulture) : "–";

    public string Satisfaction => member.Satisfaction == RoleSatisfaction.NotJudged ? "–" : PlayerCatalog.DisplayName(member.Satisfaction);

    public int Morale => member.Place.Player.Morale;

    public int Leadership => member.Place.LeaderScore;

    public string Conflict => member.InConflict ? "Unsettled" : string.Empty;
}

/// <summary>
/// The dressing room (Step 26, docs/CHEMISTRY.md): team chemistry of the current core and its components, the squad
/// hierarchy with expected and recent minutes, satisfaction, morale and leaders, and notable relationships. Read-only.
/// </summary>
public sealed partial class DressingRoomViewModel : ObservableObject
{
    private readonly IChemistryService _chemistry;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private string _title = "Choose a club";

    [ObservableProperty]
    private string _chemistryText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<DressingRoomRowViewModel> _players = [];

    [ObservableProperty]
    private IReadOnlyList<RelationshipLine> _relationships = [];

    public DressingRoomViewModel(IChemistryService chemistry, EditorLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(chemistry);
        _chemistry = chemistry;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    internal static string Describe(TeamChemistry c) =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"Chemistry {c.Rating}/100 · morale {Signed(c.Morale)} · relationships {Signed(c.Relationships)} · leadership {Signed(c.Leadership)} · tactical fit {Signed(c.Tactical)}{(c.Conflicts > 0 ? $" · {c.Conflicts} unhappy in the core" : string.Empty)}");

    public async Task RefreshAsync()
    {
        var data = ClubId is { } id ? await _chemistry.GetClubChemistryAsync(id) : null;
        Title = data is null ? "Choose a club" : $"{data.Club.Name} · dressing room";
        ChemistryText = data is null
            ? string.Empty
            : $"{Describe(data.Chemistry)} · captain {data.Captain?.Place.Player.FullName ?? "none"} · {data.Members.Count(m => m.InConflict)} unsettled";
        Players = [.. (data?.Members ?? []).Select(m => new DressingRoomRowViewModel(m))];
        Relationships = data?.Relationships ?? [];
    }

    async partial void OnClubIdChanged(ClubId? value) => await RefreshAsync();

    private static string Signed(double value) => value.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
}
