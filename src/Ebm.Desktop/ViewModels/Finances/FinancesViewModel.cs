using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Desktop.ViewModels.Contracts;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Players;
using Ebm.Domain.Clubs;
using Ebm.Domain.Finances;
using Ebm.Services.Finances;

namespace Ebm.Desktop.ViewModels.Finances;

/// <summary>One booking in the club's ledger.</summary>
public sealed class TransactionRowViewModel(FinancialTransaction transaction)
{
    public string Date => Money.Date(transaction.Date);

    public string Category => FinancesViewModel.Name(transaction.Category);

    public string Description => transaction.Description ?? string.Empty;

    public string Amount => Money.Euros(transaction.Amount);
}

/// <summary>
/// The club's finances (Step 23, docs/FINANCES.md): balance, the board's budget and target, income and expenditure of the
/// financial year by category, salary commitments and the latest bookings. Read-only.
/// </summary>
public sealed partial class FinancesViewModel : ObservableObject
{
    private readonly IFinanceService _finances;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private string _title = "Choose a club";

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _summary = [];

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _budget = [];

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _income = [];

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _expenditure = [];

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _commitments = [];

    [ObservableProperty]
    private IReadOnlyList<TransactionRowViewModel> _transactions = [];

    public FinancesViewModel(IFinanceService finances, EditorLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(finances);
        _finances = finances;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    internal static string Name(FinanceCategory category) => category switch
    {
        FinanceCategory.PlayerSalaries => "Player salaries",
        FinanceCategory.StaffSalaries => "Staff salaries",
        FinanceCategory.SigningBonuses => "Signing bonuses",
        FinanceCategory.Settlements => "Contract settlements",
        FinanceCategory.TransferFees => "Transfer fees",
        FinanceCategory.LoanFees => "Loans",
        FinanceCategory.TicketSales => "Ticket sales",
        FinanceCategory.PrizeMoney => "Prize money",
        FinanceCategory.ArenaCosts => "Arena",
        FinanceCategory.TravelCosts => "Travel",
        _ => category.ToString(),
    };

    public async Task RefreshAsync()
    {
        var data = ClubId is { } id ? await _finances.GetReportAsync(id) : null;
        if (data is null)
        {
            Title = "Choose a club";
            Summary = Budget = Income = Expenditure = Commitments = [];
            Transactions = [];
            return;
        }

        Title = $"{data.Club.Name} · finances {Money.Date(data.From)} – {Money.Date(data.To)}";
        Summary =
        [
            new("Balance", Money.Euros(data.Club.Balance)),
            new("Income this year", Money.Euros(data.TotalIncome)),
            new("Expenditure this year", Money.Euros(data.TotalExpenditure)),
            new(data.Result >= 0 ? "Profit" : "Loss", Money.Euros(Math.Abs(data.Result))),
            new("Board's target", data.Budget is { } target
                ? $"balance of at least {Money.Euros(target.MinimumBalance)} at year end · {(data.OnTarget == true ? "on target" : "below target")}"
                : "no budget set"),
        ];
        Budget = data.Budget is not { } b
            ? [new("No budget set for this year", string.Empty)]
            :
            [
                new("Sponsorship", Money.Euros(b.Sponsorship)),
                new($"Ticket sales ({b.HomeGames} home games, {b.Attendance:N0} at {Money.Euros(b.TicketPrice)})", Money.Euros(b.TicketSales)),
                new("Arena", Money.Euros(-b.ArenaCosts)),
                new("Travel", Money.Euros(-b.TravelCosts)),
                new("Staff wages", Money.Euros(-b.StaffWages)),
                new("Wage budget (players)", Money.Euros(-b.WageBudget)),
                new("Transfer budget", $"{Money.Euros(b.TransferBudget)} ({Money.Euros(data.TransferBudgetLeft ?? 0)} left)"),
            ];
        Income = data.Income.Count == 0 ? [new("Nothing yet", string.Empty)] : [.. data.Income.Select(l => new InfoLine(Name(l.Category), Money.Euros(l.Amount)))];
        Expenditure = data.Expenditure.Count == 0 ? [new("Nothing yet", string.Empty)] : [.. data.Expenditure.Select(l => new InfoLine(Name(l.Category), Money.Euros(l.Amount)))];
        var c = data.PlayerCommitments;
        Commitments =
        [
            new("Player wage bill (a year)", Money.Euros(c.WageBill)),
            new("Staff wage bill (a year)", Money.Euros(data.StaffWageBill)),
            new("Player salary still owed", Money.Euros(c.RemainingSalary)),
            .. c.Periods.Select(p => new InfoLine($"{Money.Date(p.From)} – {Money.Date(p.To)} ({p.Contracts} contracts)", Money.Euros(p.Salary))),
        ];
        Transactions = [.. data.Recent.Select(t => new TransactionRowViewModel(t))];
    }

    async partial void OnClubIdChanged(ClubId? value) => await RefreshAsync();
}
