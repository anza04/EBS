using System.Reflection;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Finances;
using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Finances;
using Ebm.Domain.Rosters;
using Ebm.Services.Careers;
using Ebm.Services.Finances;
using Ebm.Services.Import;
using Ebm.Services.Transfers;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Finances;

/// <summary>Step 23: budgets, the ledger of every income and expense, financial reports and transfer budgets.</summary>
public sealed class FinanceServiceTests : IAsyncLifetime
{
    private static readonly string SampleFolder = Path.Combine(
        typeof(FinanceServiceTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "sample");

    private static readonly DateOnly Today = new(2026, 11, 9);

    private readonly TestClock _clock = new(Today);
    private TestDatabase _db = null!;

    private IFinanceService Finances => _db.Services.GetRequiredService<IFinanceService>();

    private ICareerService Careers => _db.Services.GetRequiredService<ICareerService>();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private Task<IReadOnlyList<FinancialTransaction>> LedgerAsync() => _db.ReadAsync(s => s.Repo<FinancialTransaction, FinancialTransactionId>().ListAsync());

    private Task<IReadOnlyList<Club>> ClubsAsync() => _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync());

    [Fact]
    public async Task A_career_plans_budgets_and_books_every_income_and_expense()
    {
        _db = await TestDatabase.CreateAsync();
        var report = await _db.Services.GetRequiredService<IDataImportService>()
            .ImportAsync([Path.Combine(SampleFolder, "italy-sample.json"), Path.Combine(SampleFolder, "italy-squads.json")], new ImportOptions());
        Assert.True(report.Committed);
        var before = (await ClubsAsync()).ToDictionary(c => c.Id);
        var aurora = before.Values.Single(c => c.Name == "Aurora Milano");

        var state = await Careers.StartAsync(aurora.Id, "Test Manager");

        // Every club has a budget; the first one keeps the dataset's wage budget and funds it with sponsorship.
        var budgets = await _db.ReadAsync(s => s.Repo<ClubBudget, ClubBudgetId>().ListAsync());
        Assert.Equal(before.Count, budgets.Count);
        Assert.All(budgets, b =>
        {
            Assert.Equal(state.Season.Id, b.SeasonId);
            Assert.Equal(before[b.ClubId].WageBudget, b.WageBudget);
            Assert.True(b.ExpectedIncome >= b.ExpectedCosts);
        });
        Assert.All(budgets, b => Assert.True(b.HomeGames > 0 && b.StaffWages > 0));
        Assert.Equal(0, await Finances.PlanBudgetsAsync(state.Season.Id)); // idempotent
        var start = (await ClubsAsync()).ToDictionary(c => c.Id, c => c.Balance);

        // Play into November: matchdays and one month end.
        while ((await Careers.LoadAsync())!.Today <= new DateOnly(2026, 11, 1))
        {
            var days = await Careers.ContinueAsync(60);
            if (days.StoppedBecause == CalendarStop.UserMatch)
            {
                await Careers.PlayUserMatchAsync();
            }
        }

        // Every balance change is in the ledger.
        var ledger = await LedgerAsync();
        foreach (var club in await ClubsAsync())
        {
            Assert.Equal(club.Balance - start[club.Id], ledger.Where(t => t.ClubId == club.Id).Sum(t => t.Amount));
        }

        var categories = ledger.Select(t => t.Category).ToHashSet();
        Assert.Superset(
            new HashSet<FinanceCategory> { FinanceCategory.PlayerSalaries, FinanceCategory.StaffSalaries, FinanceCategory.Sponsorship, FinanceCategory.TicketSales, FinanceCategory.ArenaCosts, FinanceCategory.TravelCosts },
            categories);
        Assert.All(ledger.Where(t => t.Category == FinanceCategory.TicketSales), t => Assert.True(t.Amount > 0));
        Assert.All(ledger.Where(t => t.Category is FinanceCategory.TravelCosts or FinanceCategory.PlayerSalaries), t => Assert.True(t.Amount < 0));

        // The report adds up and shows the budget, the commitments and the board's verdict.
        var finances = (await Finances.GetReportAsync(aurora.Id))!;
        Assert.Equal(state.Season.Id, finances.Season!.Id);
        Assert.NotNull(finances.Budget);
        Assert.NotNull(finances.OnTarget);
        Assert.Contains(finances.Income, l => l.Category == FinanceCategory.Sponsorship);
        Assert.Contains(finances.Expenditure, l => l.Category == FinanceCategory.PlayerSalaries);
        Assert.Equal(ledger.Where(t => t.ClubId == aurora.Id).Sum(t => t.Amount), finances.Result);
        Assert.True(finances.PlayerCommitments.WageBill > 0 && finances.StaffWageBill > 0);
        Assert.NotEmpty(finances.Recent);
        Assert.NotEmpty(await Finances.GetTransactionsAsync(aurora.Id, finances.From, finances.To));

        // The finances screen shows it.
        var screen = new FinancesViewModel(Finances, new EditorLookups());
        screen.ClubId = aurora.Id;
        await screen.RefreshAsync();
        Assert.Contains("Aurora Milano", screen.Title, StringComparison.Ordinal);
        Assert.Contains(screen.Income, l => l.Label == "Sponsorship");
        Assert.NotEmpty(screen.Transactions);
    }

    [Fact]
    public async Task Transfers_and_bonuses_are_booked_and_use_up_the_transfer_budget()
    {
        _db = await TestDatabase.CreateAsync(_clock);
        var world = await SampleWorld.SeedAsync(_db);
        await _db.WriteAsync(s =>
        {
            var away = s.Repo<Club, ClubId>().FindAsync(world.AwayClub.Id).GetAwaiter().GetResult()!;
            away.UpdateProfile(away.City, 50, 4_000_000, 500_000, away.BoardExpectation);
        });
        Assert.Equal(2, await Finances.PlanBudgetsAsync(world.Season.Id));
        var budget = (await Finances.GetReportAsync(world.AwayClub.Id))!.Budget!;
        Assert.Equal(2_000_000, budget.TransferBudget);

        var market = _db.Services.GetRequiredService<ITransferService>();
        var listing = (await market.GetMarketPlayerAsync(world.Player.Id, world.AwayClub.Id))!;
        var terms = new ContractTerms(Today, Today.AddYears(2).AddDays(-1), listing.AskingSalary, SigningBonus: 25_000);
        await market.BuyAsync(world.Player.Id, world.AwayClub.Id, listing.Valuation.AskingFee, terms, RosterRole.Inactive);

        var fee = listing.Valuation.AskingFee;
        var ledger = await LedgerAsync();
        Assert.Contains(ledger, t => (t.ClubId, t.Category, t.Amount) == (world.HomeClub.Id, FinanceCategory.TransferFees, fee));
        Assert.Contains(ledger, t => (t.ClubId, t.Category, t.Amount) == (world.AwayClub.Id, FinanceCategory.TransferFees, -fee));
        Assert.Contains(ledger, t => (t.ClubId, t.Category, t.Amount) == (world.AwayClub.Id, FinanceCategory.SigningBonuses, -25_000L));
        var report = (await Finances.GetReportAsync(world.AwayClub.Id))!;
        Assert.Equal(2_000_000 - fee - 25_000, report.TransferBudgetLeft);
        Assert.Equal(-(fee + 25_000), report.Result);
        Assert.Equal(4_000_000 - fee - 25_000, report.Club.Balance);

        // The seller's sale adds to its transfer budget.
        var seller = (await Finances.GetReportAsync(world.HomeClub.Id))!;
        Assert.Equal(seller.Budget!.TransferBudget + fee, seller.TransferBudgetLeft);
    }
}
