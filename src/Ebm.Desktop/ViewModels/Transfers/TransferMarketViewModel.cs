using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.ViewModels.Contracts;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Players;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Transfers;
using Ebm.Services.Contracts;
using Ebm.Services.Data;
using Ebm.Services.Transfers;

namespace Ebm.Desktop.ViewModels.Transfers;

/// <summary>One player in the market search results.</summary>
public sealed record MarketRow(MarketPlayer Market)
{
    public Player Player => Market.Player;

    public string Name => $"{Player.LastName}, {Player.FirstName}";

    public string Position => PlayerCatalog.DisplayName(Player.Position);

    public int Age => Market.Age;

    public int Overall => Market.Overall;

    public int Potential => Market.Evaluation.PotentialRating;

    public string Nationality => Market.Nationality;

    public string Club => Market.Status == MarketStatus.OnLoan
        ? $"{Market.Club?.Name} (on loan from {Market.ParentClub?.Name})"
        : Market.Club?.Name ?? "free agent";

    public string Status => Market.Status switch
    {
        MarketStatus.FreeAgent => "Free agent",
        MarketStatus.OnLoan => "On loan",
        _ => Market.Contract is { } c && !c.IsRunningOn(Market.Date) ? $"Signed from {Money.Date(c.StartDate)}" : "Under contract",
    };

    public string ContractEnd => Market.ContractEnd is { } end ? Money.Date(end) : "—";

    public string Salary => Market.Salary == 0 ? "—" : Money.Euros(Market.Salary);

    public string Value => Money.Euros(Market.Valuation.MarketValue);

    public string Fee => Market.IsFreeAgent ? "—" : Money.Euros(Market.Valuation.AskingFee);

    public string ReleaseClause => Market.ReleaseClause is { } clause ? Money.Euros(clause) : "—";

    public string Loan => Market.Status != MarketStatus.UnderContract ? "—" : Market.Valuation.AvailableForLoan ? $"{Money.Euros(Market.Valuation.LoanFeePerYear)}/year" : "not available";

    public string AskingSalary => Money.Euros(Market.AskingSalary);
}

/// <summary>One transfer in the history and loan grids.</summary>
public sealed record TransferRowView(TransferLine Line)
{
    public Transfer Transfer => Line.Transfer;

    public string Date => Money.Date(Line.Date);

    public string Player => Line.Player;

    public string From => Line.FromClub ?? "free agent";

    public string To => Line.ToClub;

    public string Kind => Line.KindText;

    public string Fee => Line.Fee == 0 ? "—" : Money.Euros(Line.Fee);

    public string LoanEnd => Transfer.LoanEndDate is { } end ? Money.Date(end) : "—";
}

/// <summary>
/// The transfer market screen (Step 08): player search with filters, valuations, fee and contract negotiation,
/// transfers, release clauses, loans and recalls, free-agent signings, the club's transfer history and the AI clubs'
/// transfer window. Every action goes through <see cref="ITransferService"/> or <see cref="IContractService"/>.
/// </summary>
public sealed partial class TransferMarketViewModel : ObservableObject
{
    private readonly ITransferService _transfers;
    private readonly IContractService _contracts;
    private readonly Func<Task> _reloadAll;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private string _title = "Choose your club";

    [ObservableProperty]
    private string _searchName = string.Empty;

    [ObservableProperty]
    private Position? _searchPosition;

    [ObservableProperty]
    private string _minAge = string.Empty;

    [ObservableProperty]
    private string _maxAge = string.Empty;

    [ObservableProperty]
    private string _minOverall = string.Empty;

    [ObservableProperty]
    private MarketFilter _filter = MarketFilter.Everyone;

    [ObservableProperty]
    private string _maxFee = string.Empty;

    [ObservableProperty]
    private string _maxSalary = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<MarketRow> _results = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedIsFreeAgent), nameof(SelectedUnderContract), nameof(SelectedHasClause), nameof(SelectedTitle))]
    private MarketRow? _selectedPlayer;

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _playerDetails = [];

    [ObservableProperty]
    private string _fee = string.Empty;

    [ObservableProperty]
    private string _offerSalary = string.Empty;

    [ObservableProperty]
    private DateTime? _offerEnd;

    [ObservableProperty]
    private string _offerSigningBonus = string.Empty;

    [ObservableProperty]
    private RosterRole _joinAs = RosterRole.Bench;

    [ObservableProperty]
    private DateTime? _loanUntil;

    [ObservableProperty]
    private string _loanFee = string.Empty;

    [ObservableProperty]
    private string? _negotiation;

    [ObservableProperty]
    private IReadOnlyList<TransferRowView> _history = [];

    [ObservableProperty]
    private IReadOnlyList<TransferRowView> _loansOut = [];

    [ObservableProperty]
    private IReadOnlyList<TransferRowView> _loansIn = [];

    [ObservableProperty]
    private TransferRowView? _selectedLoan;

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _aiReport = [];

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    private long? _clubCounter;
    private long? _loanCounter;
    private ContractTerms? _playerCounter;

    public TransferMarketViewModel(ITransferService transfers, IContractService contracts, EditorLookups lookups, Func<Task> reloadAll)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(lookups);
        ArgumentNullException.ThrowIfNull(reloadAll);
        _transfers = transfers;
        _contracts = contracts;
        _reloadAll = reloadAll;
        Lookups = lookups;
        PositionOptions = [new(null, "(any)"), .. Enum.GetValues<Position>().Select(p => new Option<Position>(p, PlayerCatalog.DisplayName(p)))];
    }

    public EditorLookups Lookups { get; }

    /// <summary>Date the shown data refers to (today in the game).</summary>
    public DateOnly? Date { get; private set; }

    public IReadOnlyList<Option<Position>> PositionOptions { get; }

    public IReadOnlyList<MarketFilter> Filters { get; } = Enum.GetValues<MarketFilter>();

    public IReadOnlyList<RosterRole> JoinRoles { get; } = [RosterRole.Bench, RosterRole.Inactive, RosterRole.Youth];

    public bool HasSelection => SelectedPlayer is not null;

    public bool SelectedIsFreeAgent => SelectedPlayer?.Market.Status == MarketStatus.FreeAgent;

    public bool SelectedUnderContract => SelectedPlayer?.Market.Status == MarketStatus.UnderContract && SelectedPlayer.Market.Contract?.IsRunningOn(SelectedPlayer.Market.Date) == true;

    public bool SelectedHasClause => SelectedUnderContract && SelectedPlayer?.Market.ReleaseClause is not null;

    public string SelectedTitle => SelectedPlayer is { } row ? $"{row.Player.FullName} · {row.Position} · ovr {row.Overall} · age {row.Age}" : "Select a player";

    /// <summary>Reloads the search results and the club's transfers; keeps the selection when the player is still listed.</summary>
    public async Task RefreshAsync()
    {
        var keep = SelectedPlayer?.Player.Id;
        var data = ClubId is { } id ? await _transfers.GetClubTransfersAsync(id) : null;
        if (data is null)
        {
            Title = "Choose your club";
            Date = null;
            History = LoansOut = LoansIn = [];
        }
        else
        {
            Date = data.Date;
            Title = $"{data.Club.Name} · balance {Money.Euros(data.Club.Balance)} · market on {Money.Date(data.Date)}";
            History = [.. data.History.Select(l => new TransferRowView(l))];
            LoansOut = [.. data.LoansOut.Select(l => new TransferRowView(l))];
            LoansIn = [.. data.LoansIn.Select(l => new TransferRowView(l))];
        }

        await SearchAsync();
        SelectedPlayer = keep is { } k ? Results.FirstOrDefault(r => r.Player.Id == k) : null;
        if (SelectedPlayer is not null)
        {
            await DescribeSelectionAsync();
        }
    }

    async partial void OnClubIdChanged(ClubId? value)
    {
        Message = null;
        Negotiation = null;
        await RefreshAsync();
    }

    async partial void OnSelectedPlayerChanged(MarketRow? value)
    {
        Negotiation = null;
        _clubCounter = _loanCounter = null;
        _playerCounter = null;
        NotifyCounters();
        await DescribeSelectionAsync();
    }

    partial void OnFeeChanged(string value)
    {
        _clubCounter = null;
        UseClubCounterCommand.NotifyCanExecuteChanged();
    }

    partial void OnOfferSalaryChanged(string value)
    {
        _playerCounter = null;
        UsePlayerCounterCommand.NotifyCanExecuteChanged();
    }

    partial void OnLoanFeeChanged(string value)
    {
        _loanCounter = null;
        UseLoanCounterCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        try
        {
            var search = new PlayerSearch(
                string.IsNullOrWhiteSpace(SearchName) ? null : SearchName,
                SearchPosition,
                ParseInt(MinAge, "Minimum age"),
                ParseInt(MaxAge, "Maximum age"),
                ParseInt(MinOverall, "Minimum overall"),
                null,
                null,
                Filter,
                Money.ParseOptional(MaxFee, "Maximum fee"),
                Money.ParseOptional(MaxSalary, "Maximum salary"),
                ClubId);
            Results = [.. (await _transfers.SearchAsync(search)).Select(m => new MarketRow(m))];
            if (IsError)
            {
                Message = null;
                IsError = false;
            }
        }
        catch (DomainException ex)
        {
            Report(ex.Message, error: true);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchName = MinAge = MaxAge = MinOverall = MaxFee = MaxSalary = string.Empty;
        SearchPosition = null;
        Filter = MarketFilter.Everyone;
    }

    /// <summary>Asks the selling club what it thinks of the fee; nothing is signed.</summary>
    [RelayCommand]
    private async Task AskClubAsync()
    {
        await RunAsync(async () =>
        {
            var row = RequireSelection();
            var response = await _transfers.NegotiateFeeAsync(row.Player.Id, RequireClub(), Money.Parse(Fee, "Fee"));
            Negotiation = $"{row.Market.Club?.Name}: {response.Message} (It asks about {Money.Euros(response.AskingFee)}.)";
            _clubCounter = response.CounterFee;
            UseClubCounterCommand.NotifyCanExecuteChanged();
            return null;
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseClubCounter))]
    private void UseClubCounter()
    {
        if (_clubCounter is { } fee)
        {
            Fee = fee.ToString("N0", CultureInfo.CurrentCulture);
            Negotiation = "Fee set to the club's counter-offer.";
        }
    }

    private bool CanUseClubCounter() => _clubCounter is not null;

    /// <summary>Asks the player what he thinks of the contract he would get; nothing is signed.</summary>
    [RelayCommand]
    private async Task AskPlayerAsync()
    {
        await RunAsync(async () =>
        {
            var row = RequireSelection();
            var terms = Terms();
            var response = row.Market.Status == MarketStatus.FreeAgent
                ? await _contracts.NegotiateAsync(row.Player.Id, RequireClub(), terms)
                : await _transfers.NegotiateContractAsync(row.Player.Id, RequireClub(), terms);
            Negotiation = $"{row.Player.FullName}: {response.Message} (He values the offer at {Money.Euros(response.OfferValue)} a year and asks about {Money.Euros(response.AskingSalary)}.)";
            _playerCounter = response.CounterOffer;
            UsePlayerCounterCommand.NotifyCanExecuteChanged();
            return null;
        });
    }

    [RelayCommand(CanExecute = nameof(CanUsePlayerCounter))]
    private void UsePlayerCounter()
    {
        if (_playerCounter is { } counter)
        {
            OfferSalary = counter.AnnualSalary.ToString("N0", CultureInfo.CurrentCulture);
            Negotiation = "Salary set to the player's counter-offer.";
        }
    }

    private bool CanUsePlayerCounter() => _playerCounter is not null;

    /// <summary>Buys the selected player for the fee on the form, on the contract on the form.</summary>
    [RelayCommand]
    private Task BuyAsync() => BuyAsync(null);

    /// <summary>Buys the selected player for his release clause: his club cannot refuse.</summary>
    [RelayCommand]
    private Task PayReleaseClauseAsync() => BuyAsync(SelectedPlayer?.Market.ReleaseClause ?? throw new InvalidOperationException("The selected player has no release clause."));

    private async Task BuyAsync(long? clause)
    {
        await RunAsync(async () =>
        {
            var row = RequireSelection();
            var fee = clause ?? Money.Parse(Fee, "Fee");
            var receipt = await _transfers.BuyAsync(row.Player.Id, RequireClub(), fee, Terms(), JoinAs);
            return Done(receipt);
        });
    }

    /// <summary>Signs the selected free agent on the contract on the form.</summary>
    [RelayCommand]
    private async Task SignFreeAgentAsync()
    {
        await RunAsync(async () =>
        {
            var row = RequireSelection();
            var terms = Terms();
            await _contracts.SignAsync(row.Player.Id, RequireClub(), terms, JoinAs);
            return $"{row.Player.FullName} signed as a free agent on {Money.Euros(terms.AnnualSalary)} a year until {Money.Date(terms.EndDate)}.";
        });
    }

    /// <summary>Asks the parent club about a loan on the form's dates and fee; nothing is signed.</summary>
    [RelayCommand]
    private async Task AskLoanAsync()
    {
        await RunAsync(async () =>
        {
            var row = RequireSelection();
            var response = await _transfers.NegotiateLoanAsync(row.Player.Id, RequireClub(), RequiredDate(LoanUntil, "Loan end"), Money.ParseOptional(LoanFee, "Loan fee") ?? 0);
            Negotiation = $"{row.Market.Club?.Name}: {response.Message} (It asks about {Money.Euros(response.AskingFee)}.)";
            _loanCounter = response.CounterFee;
            UseLoanCounterCommand.NotifyCanExecuteChanged();
            return null;
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseLoanCounter))]
    private void UseLoanCounter()
    {
        if (_loanCounter is { } fee)
        {
            LoanFee = fee.ToString("N0", CultureInfo.CurrentCulture);
            Negotiation = "Loan fee set to the club's counter-offer.";
        }
    }

    private bool CanUseLoanCounter() => _loanCounter is not null;

    [RelayCommand]
    private async Task LoanAsync()
    {
        await RunAsync(async () =>
        {
            var row = RequireSelection();
            var receipt = await _transfers.LoanAsync(row.Player.Id, RequireClub(), RequiredDate(LoanUntil, "Loan end"), Money.ParseOptional(LoanFee, "Loan fee") ?? 0, JoinAs);
            return Done(receipt);
        });
    }

    /// <summary>Recalls the selected loaned-out player today.</summary>
    [RelayCommand]
    private async Task RecallAsync()
    {
        await RunAsync(async () =>
        {
            var loan = SelectedLoan ?? throw new DomainValidationException("Select one of your players out on loan.");
            var receipt = await _transfers.RecallAsync(loan.Transfer.Id);
            return receipt.Message;
        });
    }

    /// <summary>Every other club runs its transfer window with the AI rules; the chosen club is left alone.</summary>
    [RelayCommand]
    private async Task RunAiWindowAsync()
    {
        await RunAsync(async () =>
        {
            var managed = ClubId is { } id ? new[] { id } : [];
            var report = await _transfers.RunAiTransferWindowAsync(managed);
            AiReport =
            [
                .. report.Clubs.SelectMany(c => new[] { new InfoLine(c.Club.Name, c.Transactions == 1 ? "1 deal" : $"{c.Transactions} deals") }
                    .Concat(c.Needs.Select(n => new InfoLine("  needs", n)))
                    .Concat(c.Actions.Select(a => new InfoLine("  ", a)))),
            ];
            return $"AI transfer window done: {report.Transactions} deals by {report.Clubs.Count} clubs.";
        });
    }

    private async Task DescribeSelectionAsync()
    {
        if (SelectedPlayer is not { } row)
        {
            PlayerDetails = [];
            return;
        }

        var m = row.Market;
        var details = new List<InfoLine>
        {
            new("Status", row.Status),
            new("Club", row.Club),
            new("Nationality", m.Nationality),
            new("Potential", Format(m.Evaluation.PotentialRating)),
            new("Market value", row.Value),
            new("Asks for a salary of", $"{row.AskingSalary} a year"),
        };
        if (m.Contract is { } c)
        {
            details.Add(new("Contract", $"{Money.Euros(c.AnnualSalary)} a year until {Money.Date(c.EffectiveEndDate)}"));
            details.Add(new("Release clause", row.ReleaseClause));
            if (m.Status == MarketStatus.UnderContract)
            {
                details.Add(new("Club asks", row.Fee));
                details.Add(new("Loan", row.Loan));
            }
        }

        PlayerDetails = details;

        // Pre-fill the forms with what would be accepted straight away.
        var today = Date ?? DateOnly.FromDateTime(DateTime.Today);
        Fee = m.IsFreeAgent ? string.Empty : m.Valuation.BuyNowFee.ToString("N0", CultureInfo.CurrentCulture);
        OfferSalary = m.AskingSalary.ToString("N0", CultureInfo.CurrentCulture);
        OfferEnd ??= today.AddYears(2).AddDays(-1).ToDateTime(TimeOnly.MinValue);
        LoanUntil = m.Status == MarketStatus.UnderContract && m.ContractEnd is { } end
            ? (end < today.AddYears(1) ? end : today.AddYears(1).AddDays(-1)).ToDateTime(TimeOnly.MinValue)
            : null;
        LoanFee = m.Status == MarketStatus.UnderContract && m.Valuation.AvailableForLoan ? m.Valuation.LoanFeePerYear.ToString("N0", CultureInfo.CurrentCulture) : string.Empty;
        await Task.CompletedTask;
    }

    private string Done(TransferReceipt receipt) =>
        receipt.Warnings.Count == 0 ? receipt.Message : $"{receipt.Message} Warning: {string.Join(" ", receipt.Warnings)}";

    /// <summary>Runs an action; a returned message means data changed, so everything is reloaded.</summary>
    private async Task RunAsync(Func<Task<string?>> action)
    {
        try
        {
            if (await action() is { } done)
            {
                await _reloadAll();
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

    private ContractTerms Terms()
    {
        var start = Date ?? DateOnly.FromDateTime(DateTime.Today);
        return new ContractTerms(
            start,
            RequiredDate(OfferEnd, "Contract end"),
            Money.Parse(OfferSalary, "Annual salary"),
            SigningBonus: Money.ParseOptional(OfferSigningBonus, "Signing bonus") ?? 0);
    }

    private MarketRow RequireSelection() => SelectedPlayer ?? throw new DomainValidationException("Select a player in the search results.");

    private ClubId RequireClub() => ClubId ?? throw new DomainValidationException("Choose your club first.");

    private void NotifyCounters()
    {
        UseClubCounterCommand.NotifyCanExecuteChanged();
        UsePlayerCounterCommand.NotifyCanExecuteChanged();
        UseLoanCounterCommand.NotifyCanExecuteChanged();
    }

    private static string Format(int value) => value.ToString(CultureInfo.CurrentCulture);

    private static int? ParseInt(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            ? value
            : throw new DomainValidationException($"{field} must be a whole number.");
    }

    private static DateOnly RequiredDate(DateTime? value, string field) =>
        value is { } date ? DateOnly.FromDateTime(date) : throw new DomainValidationException($"{field} is required.");

    private void Report(string message, bool error)
    {
        Message = message;
        IsError = error;
    }
}
