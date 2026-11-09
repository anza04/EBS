using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Players;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Services.Contracts;
using Ebm.Services.Data;

namespace Ebm.Desktop.ViewModels.Contracts;

internal static class Money
{
    public static string Euros(long value) => $"€ {value.ToString("N0", CultureInfo.CurrentCulture)}";

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static long Parse(string? text, string field) =>
        ParseOptional(text, field) ?? throw new DomainValidationException($"{field} is required.");

    /// <summary>Whole euros; thousands separators and a leading € are allowed.</summary>
    public static long? ParseOptional(string? text, string field)
    {
        var value = text?.Replace("€", string.Empty, StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return long.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var result)
            ? result
            : throw new DomainValidationException($"{field} must be a whole number of euros.");
    }

    public static string Options(Contract c) => (c.ClubOptionUntil, c.PlayerOptionUntil) switch
    {
        (null, null) => "—",
        ({ } club, null) => $"Club → {Date(club)}",
        (null, { } player) => $"Player → {Date(player)}",
        ({ } club, { } player) => $"Club → {Date(club)}, player → {Date(player)}",
    };

    public static string Bonuses(PerformanceBonuses bonuses) =>
        bonuses.IsEmpty ? "—" : string.Join(", ", bonuses.Amounts.Select(b => $"{PlayerCatalog.DisplayName(b.Key)} {Euros(b.Value)}"));
}

/// <summary>One contract in the club contracts grids.</summary>
public sealed record ContractRowView(ContractLine Line)
{
    public Contract Contract => Line.Contract;

    public string Player => Line.Player;

    public string Club => Line.Club;

    public string Status => Line.StatusText;

    public string Start => Money.Date(Contract.StartDate);

    /// <summary>Agreed end, or the early end for terminated and renewed contracts.</summary>
    public string End => Contract.TerminationDate is { } ended ? $"{Money.Date(Contract.EffectiveEndDate)} (ended early)" : Money.Date(Contract.EndDate);

    public string Salary => Money.Euros(Contract.AnnualSalary);

    public string Frequency => PlayerCatalog.DisplayName(Contract.PaymentFrequency);

    public string Remaining => Line.RemainingSalary == 0 ? "—" : Money.Euros(Line.RemainingSalary);

    public string NextPayment => Line.NextPayment is { } p ? $"{Money.Euros(p.Amount)} on {Money.Date(p.DueDate)}" : "—";

    public string Options => Money.Options(Contract);

    public string ReleaseClause => Contract.ReleaseClause is { } clause ? Money.Euros(clause) : "—";

    public string Bonuses => Money.Bonuses(Contract.PerformanceBonuses);

    public string SigningBonus => Contract.SigningBonus == 0 ? "—" : Money.Euros(Contract.SigningBonus);

    public string Settlement => Contract.Status == ContractStatus.Terminated ? Money.Euros(Contract.TerminationCompensation) : "—";
}

/// <summary>Whether the offer form signs a free agent or renews the selected contract.</summary>
public enum OfferMode
{
    NewSigning,
    Renewal,
}

/// <summary>
/// The club contracts screen (Step 07): running, upcoming and past contracts, salary commitments, and the contract
/// actions: sign a free agent, renew, terminate and decide options. Every action goes through <see cref="IContractService"/>.
/// </summary>
public sealed partial class ClubContractsViewModel : ObservableObject
{
    private readonly IContractService _contracts;
    private readonly Func<Task> _reloadAll;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private string _title = "Choose a club";

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _finances = [];

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _commitments = [];

    [ObservableProperty]
    private IReadOnlyList<ContractRowView> _current = [];

    [ObservableProperty]
    private IReadOnlyList<ContractRowView> _history = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedHasClubOption), nameof(SelectedHasPlayerOption), nameof(SelectedSummary))]
    private ContractRowView? _selectedContract;

    [ObservableProperty]
    private IReadOnlyList<Option<PlayerId>> _freeAgents = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OfferTitle), nameof(IsNewSigning))]
    private OfferMode _mode = OfferMode.NewSigning;

    [ObservableProperty]
    private PlayerId? _offerPlayerId;

    [ObservableProperty]
    private DateTime? _offerStart;

    [ObservableProperty]
    private DateTime? _offerEnd;

    [ObservableProperty]
    private string _offerSalary = string.Empty;

    [ObservableProperty]
    private PaymentFrequency _offerFrequency = PaymentFrequency.Monthly;

    [ObservableProperty]
    private string _offerSigningBonus = string.Empty;

    [ObservableProperty]
    private string _offerBonuses = string.Empty;

    [ObservableProperty]
    private string _offerReleaseClause = string.Empty;

    [ObservableProperty]
    private DateTime? _offerClubOption;

    [ObservableProperty]
    private DateTime? _offerPlayerOption;

    [ObservableProperty]
    private RosterRole _joinAs = RosterRole.Bench;

    [ObservableProperty]
    private string? _negotiation;

    [ObservableProperty]
    private TerminationKind _terminationKind = TerminationKind.ClubRelease;

    [ObservableProperty]
    private string _settlement = string.Empty;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    private ContractTerms? _counterOffer;

    public ClubContractsViewModel(IContractService contracts, EditorLookups lookups, Func<Task> reloadAll)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(reloadAll);
        _contracts = contracts;
        _reloadAll = reloadAll;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    /// <summary>Date the shown data refers to (today in the game).</summary>
    public DateOnly? Date { get; private set; }

    public IReadOnlyList<PaymentFrequency> Frequencies { get; } = Enum.GetValues<PaymentFrequency>();

    public IReadOnlyList<RosterRole> JoinRoles { get; } = [RosterRole.Bench, RosterRole.Inactive, RosterRole.Youth];

    public IReadOnlyList<TerminationKind> TerminationKinds { get; } = Enum.GetValues<TerminationKind>();

    public bool HasSelection => SelectedContract is not null;

    public bool IsNewSigning => Mode == OfferMode.NewSigning;

    public bool SelectedHasClubOption => SelectedContract?.Contract.ClubOptionUntil is not null;

    public bool SelectedHasPlayerOption => SelectedContract?.Contract.PlayerOptionUntil is not null;

    public string OfferTitle => Mode == OfferMode.NewSigning
        ? "Sign a free agent"
        : $"Renew {_renewing?.Player ?? "the selected contract"}";

    public string SelectedSummary => SelectedContract is { } row
        ? $"{row.Player}: {row.Salary} a year until {row.End}. Remaining salary {row.Remaining}."
        : "Select a running contract.";

    private ContractRowView? _renewing;

    public async Task RefreshAsync()
    {
        var data = ClubId is { } id ? await _contracts.GetClubContractsAsync(id) : null;
        var keep = SelectedContract?.Contract.Id;
        var agents = await _contracts.GetFreeAgentsAsync();
        FreeAgents = [.. agents.Select(a => new Option<PlayerId>(a.Player.Id, $"{a.Player.LastName}, {a.Player.FirstName} · {PlayerCatalog.DisplayName(a.Player.Position)} · ovr {a.Overall} · age {a.Age}"))];
        if (data is null)
        {
            Title = "Choose a club";
            Date = null;
            Finances = Commitments = [];
            Current = History = [];
            SelectedContract = null;
            return;
        }

        var c = data.Commitments;
        Date = data.Date;
        Title = $"{data.Club.Name} · contracts on {Money.Date(data.Date)}";
        Finances =
        [
            new("Balance", Money.Euros(data.Club.Balance)),
            new("Wage budget (a year)", Money.Euros(data.Club.WageBudget)),
            new("Wage bill (a year)", Money.Euros(c.WageBill)),
            new(data.WageBudgetLeft >= 0 ? "Budget left" : "Over budget by", Money.Euros(Math.Abs(data.WageBudgetLeft))),
            new("Contracts", $"{c.RunningContracts} running, {c.UpcomingContracts} upcoming"),
            new("Salary still owed", Money.Euros(c.RemainingSalary)),
            new("Season bonuses (if all earned)", Money.Euros(c.MaxSeasonBonuses)),
        ];
        Commitments = c.Periods.Count == 0
            ? [new("No salary commitments", string.Empty)]
            : [.. c.Periods.Select(p => new InfoLine($"{Money.Date(p.From)} – {Money.Date(p.To)} ({p.Contracts})", Money.Euros(p.Salary)))];
        Current = [.. data.Current.Select(l => new ContractRowView(l))];
        History = [.. data.History.Select(l => new ContractRowView(l))];
        SelectedContract = keep is { } k ? Current.FirstOrDefault(r => r.Contract.Id == k) : null;
        if (OfferStart is null)
        {
            StartSigning();
        }
    }

    async partial void OnClubIdChanged(ClubId? value)
    {
        Message = null;
        Negotiation = null;
        await RefreshAsync();
        StartSigning();
    }

    partial void OnOfferSalaryChanged(string value)
    {
        _counterOffer = null;
        AcceptCounterCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Clears the offer form for a new signing starting today, for two years.</summary>
    [RelayCommand]
    private void StartSigning()
    {
        Mode = OfferMode.NewSigning;
        _renewing = null;
        var start = Date ?? DateOnly.FromDateTime(DateTime.Today);
        FillForm(new ContractTerms(start, start.AddYears(2).AddDays(-1), 0));
        OfferSalary = string.Empty;
        OfferPlayerId = null;
        Negotiation = null;
        OnPropertyChanged(nameof(OfferTitle));
    }

    /// <summary>Fills the offer form with a renewal of the selected contract: same terms, starting the day after it ends.</summary>
    [RelayCommand]
    private void StartRenewal()
    {
        if (SelectedContract is not { } row)
        {
            Report("Select a running contract to renew.", error: true);
            return;
        }

        Mode = OfferMode.Renewal;
        _renewing = row;
        var c = row.Contract;
        var today = Date ?? DateOnly.FromDateTime(DateTime.Today);
        var start = c.EndDate.AddDays(1) < today ? today : c.EndDate.AddDays(1);
        FillForm(c.Terms with { StartDate = start, EndDate = start.AddYears(2).AddDays(-1), SigningBonus = 0, ClubOptionUntil = null, PlayerOptionUntil = null });
        OfferPlayerId = c.PlayerId;
        Negotiation = null;
        OnPropertyChanged(nameof(OfferTitle));
    }

    /// <summary>Asks the player what he thinks of the offer, without signing anything.</summary>
    [RelayCommand]
    private async Task AskPlayerAsync()
    {
        await RunAsync(async () =>
        {
            var terms = Terms();
            var response = Mode == OfferMode.Renewal
                ? await _contracts.NegotiateRenewalAsync(RenewingId(), terms)
                : await _contracts.NegotiateAsync(Required(OfferPlayerId, "player"), Required(ClubId, "club"), terms);
            Negotiation = $"{response.Message} (He values the offer at {Money.Euros(response.OfferValue)} a year and asks about {Money.Euros(response.AskingSalary)}.)";
            _counterOffer = response.CounterOffer;
            AcceptCounterCommand.NotifyCanExecuteChanged();
            return null;
        });
    }

    [RelayCommand(CanExecute = nameof(CanAcceptCounter))]
    private void AcceptCounter()
    {
        if (_counterOffer is { } counter)
        {
            OfferSalary = counter.AnnualSalary.ToString("N0", CultureInfo.CurrentCulture);
            Negotiation = "Salary set to the player's counter-offer. Submit to sign.";
        }
    }

    private bool CanAcceptCounter() => _counterOffer is not null;

    /// <summary>Signs the free agent or renews the contract on the form's terms; the player must accept them.</summary>
    [RelayCommand]
    private async Task SubmitOfferAsync()
    {
        await RunAsync(async () =>
        {
            var terms = Terms();
            if (Mode == OfferMode.Renewal)
            {
                await _contracts.RenewAsync(RenewingId(), terms);
                return "Contract renewed. The old contract is kept in the history.";
            }

            await _contracts.SignAsync(Required(OfferPlayerId, "player"), Required(ClubId, "club"), terms, JoinAs);
            return terms.StartDate > (Date ?? terms.StartDate)
                ? $"Contract signed. The player joins on {Money.Date(terms.StartDate)}."
                : "Contract signed. The player joins the squad today.";
        });
    }

    [RelayCommand]
    private async Task TerminateAsync()
    {
        await RunAsync(async () =>
        {
            var row = SelectedContract ?? throw new DomainValidationException("Select a running contract to terminate.");
            var settlement = TerminationKind == TerminationKind.MutualAgreement ? Money.ParseOptional(Settlement, "Settlement") : null;
            var paid = await _contracts.TerminateAsync(row.Contract.Id, TerminationKind, settlement);
            return $"Contract terminated. {row.Player} received {Money.Euros(paid)} and is now a free agent.";
        });
    }

    [RelayCommand]
    private Task ExerciseClubOptionAsync() => DecideClubOptionAsync(true);

    [RelayCommand]
    private Task DeclineClubOptionAsync() => DecideClubOptionAsync(false);

    [RelayCommand]
    private async Task PlayerOptionAsync()
    {
        await RunAsync(async () =>
        {
            var row = SelectedContract ?? throw new DomainValidationException("Select a contract with a player option.");
            return await _contracts.DecidePlayerOptionAsync(row.Contract.Id)
                ? $"{row.Player} took up his option: the contract now runs until {Money.Date(row.Contract.PlayerOptionUntil!.Value)}."
                : $"{row.Player} declined his option: the contract ends on {Money.Date(row.Contract.EndDate)}.";
        });
    }

    private async Task DecideClubOptionAsync(bool exercise)
    {
        await RunAsync(async () =>
        {
            var row = SelectedContract ?? throw new DomainValidationException("Select a contract with a club option.");
            await _contracts.DecideClubOptionAsync(row.Contract.Id, exercise);
            return exercise
                ? $"Option taken up: the contract now runs until {Money.Date(row.Contract.ClubOptionUntil!.Value)}."
                : "Option declined.";
        });
    }

    /// <summary>Runs an action; a returned message means data changed, so everything is reloaded.</summary>
    private async Task RunAsync(Func<Task<string?>> action)
    {
        try
        {
            if (await action() is { } done)
            {
                await _reloadAll();
                if (done.StartsWith("Contract signed", StringComparison.Ordinal) || done.StartsWith("Contract renewed", StringComparison.Ordinal))
                {
                    StartSigning();
                }

                Report(done, error: false);
            }
            else
            {
                Message = null;
            }
        }
        catch (DomainException ex)
        {
            Report(ex.Message, error: true);
        }
        catch (DataConstraintException ex)
        {
            Report(ex.Message, error: true);
        }
    }

    private ContractId RenewingId() =>
        _renewing?.Contract.Id ?? throw new DomainValidationException("Select a running contract and choose Renew.");

    private ContractTerms Terms()
    {
        var start = RequiredDate(OfferStart, "Start date");
        var end = RequiredDate(OfferEnd, "End date");
        return new ContractTerms(
            start,
            end,
            Money.Parse(OfferSalary, "Annual salary"),
            OfferFrequency,
            Money.ParseOptional(OfferSigningBonus, "Signing bonus") ?? 0,
            PerformanceBonuses.Parse(OfferBonuses),
            Money.ParseOptional(OfferReleaseClause, "Release clause"),
            OfferClubOption is { } club ? DateOnly.FromDateTime(club) : null,
            OfferPlayerOption is { } player ? DateOnly.FromDateTime(player) : null);
    }

    private void FillForm(ContractTerms terms)
    {
        OfferStart = terms.StartDate.ToDateTime(TimeOnly.MinValue);
        OfferEnd = terms.EndDate.ToDateTime(TimeOnly.MinValue);
        OfferSalary = terms.AnnualSalary == 0 ? string.Empty : terms.AnnualSalary.ToString("N0", CultureInfo.CurrentCulture);
        OfferFrequency = terms.PaymentFrequency;
        OfferSigningBonus = terms.SigningBonus == 0 ? string.Empty : terms.SigningBonus.ToString("N0", CultureInfo.CurrentCulture);
        OfferBonuses = terms.PerformanceBonuses.ToString();
        OfferReleaseClause = terms.ReleaseClause?.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;
        OfferClubOption = terms.ClubOptionUntil?.ToDateTime(TimeOnly.MinValue);
        OfferPlayerOption = terms.PlayerOptionUntil?.ToDateTime(TimeOnly.MinValue);
        _counterOffer = null;
        AcceptCounterCommand.NotifyCanExecuteChanged();
    }

    private static DateOnly RequiredDate(DateTime? value, string field) =>
        value is { } date ? DateOnly.FromDateTime(date) : throw new DomainValidationException($"{field} is required.");

    private static TId Required<TId>(TId? value, string field)
        where TId : struct =>
        value ?? throw new DomainValidationException($"Choose a {field}.");

    private void Report(string message, bool error)
    {
        Message = message;
        IsError = error;
    }
}
