using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Finances;
using Ebm.Domain.Management;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Finances;

/// <summary>Implements <see cref="IFinanceService"/>; the daily bookings run inside the career's unit of work.</summary>
internal sealed class FinanceService(IServiceScopeFactory scopes, FinanceModel model, GameClock clock) : IFinanceService
{
    /// <summary>Categories that count against the transfer budget (sales replenish it).</summary>
    private static readonly FinanceCategory[] TransferCategories = [FinanceCategory.TransferFees, FinanceCategory.LoanFees, FinanceCategory.SigningBonuses];

    public async Task<int> PlanBudgetsAsync(SeasonId seasonId, IReadOnlyDictionary<ClubId, bool>? objectivesMet = null, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var season = await Repo<Season, SeasonId>(s).FindAsync(seasonId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainValidationException("The season no longer exists in the database.");
        var budgets = Repo<ClubBudget, ClubBudgetId>(s);
        var all = await budgets.ListAsync(null, cancellationToken).ConfigureAwait(false);
        var planned = all.Where(b => b.SeasonId == seasonId).Select(b => b.ClubId).ToHashSet();
        var allClubs = await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false);
        var clubs = allClubs.Where(c => !planned.Contains(c.Id)).ToList();
        if (clubs.Count == 0)
        {
            return 0;
        }

        var seasons = (await Repo<Season, SeasonId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(x => x.Id);
        var previous = all.Where(b => seasons.TryGetValue(b.SeasonId, out var x) && x.StartDate < season.StartDate)
            .GroupBy(b => b.ClubId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => seasons[b.SeasonId].StartDate).First());
        var capacity = (await Repo<Arena, ArenaId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id, a => a.Capacity);
        var countries = allClubs.ToDictionary(c => c.Id, c => c.CountryId);
        var fixtures = await Repo<Fixture, FixtureId>(s).ListAsync(f => f.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var today = clock.Today;
        var staffWages = (await Repo<Contract, ContractId>(s).ListAsync(c => c.StaffMemberId != null, cancellationToken).ConfigureAwait(false))
            .Where(c => c.IsRunningOn(today) || c.IsUpcomingOn(today))
            .GroupBy(c => c.ClubId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.AnnualSalary));
        foreach (var club in clubs)
        {
            var away = fixtures.Where(f => f.AwayClubId == club.Id).ToList();
            var abroad = away.Count(f => countries.GetValueOrDefault(f.HomeClubId) != club.CountryId);
            var plan = model.Plan(new BudgetInput(
                club.Reputation,
                club.WageBudget,
                club.Balance,
                club.HomeArenaId is { } arena ? capacity.GetValueOrDefault(arena) : null,
                fixtures.Count(f => f.HomeClubId == club.Id),
                away.Count - abroad,
                abroad,
                staffWages.GetValueOrDefault(club.Id),
                previous.GetValueOrDefault(club.Id),
                objectivesMet is not null && objectivesMet.TryGetValue(club.Id, out var met) ? met : null));
            budgets.Add(new ClubBudget(ClubBudgetId.New(), club.Id, seasonId, today, club.Reputation, club.Balance, plan));
            club.UpdateProfile(club.City, club.Reputation, club.Balance, plan.WageBudget, club.BoardExpectation);
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return clubs.Count;
    }

    public async Task<ClubFinanceReport?> GetReportAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        if (await Repo<Club, ClubId>(s).FindAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } club)
        {
            return null;
        }

        var today = clock.Today;
        var (budget, season) = await CurrentBudgetAsync(s, model, clubId, today, cancellationToken).ConfigureAwait(false);
        var (from, to) = season is null ? (new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)) : model.Year(season.StartDate);
        var ledger = await Repo<FinancialTransaction, FinancialTransactionId>(s)
            .ListAsync(t => t.ClubId == clubId && t.Date >= from && t.Date <= to, cancellationToken).ConfigureAwait(false);
        var contracts = await Repo<Contract, ContractId>(s).ListAsync(c => c.ClubId == clubId, cancellationToken).ConfigureAwait(false);
        List<FinanceLine> Lines(Func<long, bool> sign) =>
            [.. ledger.GroupBy(t => t.Category).Select(g => new FinanceLine(g.Key, g.Where(t => sign(t.Amount)).Sum(t => t.Amount))).Where(l => l.Amount != 0).OrderBy(l => l.Category)];
        return new ClubFinanceReport(
            club,
            season,
            from,
            to,
            budget,
            Lines(a => a > 0),
            Lines(a => a < 0),
            SalaryCommitments.Calculate(contracts.Where(c => c.PlayerId != null), today),
            contracts.Where(c => c.StaffMemberId != null && c.IsRunningOn(today)).Sum(c => c.AnnualSalary),
            budget is null ? null : TransferBudgetLeft(budget, ledger),
            budget is null ? null : FinanceModel.MeetsTarget(budget, club.Balance),
            [.. ledger.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id.Value).Take(50)]);
    }

    public async Task<IReadOnlyList<FinancialTransaction>> GetTransactionsAsync(ClubId clubId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        return
        [
            .. (await Repo<FinancialTransaction, FinancialTransactionId>(scope.ServiceProvider)
                    .ListAsync(t => t.ClubId == clubId && t.Date >= from && t.Date <= to, cancellationToken).ConfigureAwait(false))
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id.Value),
        ];
    }

    /// <summary>
    /// Books the money of game day <paramref name="date"/> in the unit of work of <paramref name="s"/> (docs/FINANCES.md §2):
    /// salaries due, matchday income and costs of the day's matches, and at month end sponsorship and arena upkeep.
    /// </summary>
    internal static async Task<FinanceDay> BookDayAsync(IServiceProvider s, FinanceModel model, DateOnly date, CancellationToken cancellationToken)
    {
        var clubs = (await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
        long salaries = 0, income = 0, costs = 0;

        var due = new Dictionary<(ClubId, bool Staff), long>();
        foreach (var contract in await Repo<Contract, ContractId>(s).ListAsync(Contract.RunningOn(date), cancellationToken).ConfigureAwait(false))
        {
            var amount = ClubFinances.SalaryDue(contract, date);
            if (amount > 0 && clubs.ContainsKey(contract.ClubId))
            {
                var key = (contract.ClubId, contract.StaffMemberId != null);
                due[key] = due.GetValueOrDefault(key) + amount;
            }
        }

        foreach (var ((clubId, staff), amount) in due.OrderBy(d => d.Key.Item1.Value).ThenBy(d => d.Key.Staff))
        {
            Ledger.Book(s, clubs[clubId], date, staff ? FinanceCategory.StaffSalaries : FinanceCategory.PlayerSalaries, -amount);
            salaries += amount;
        }

        var arenas = (await Repo<Arena, ArenaId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id, a => a.Capacity);
        int? Capacity(Club club) => club.HomeArenaId is { } id ? arenas.GetValueOrDefault(id) : null;

        // Matchday: the home club sells tickets and runs the arena, the visitors travel. Forfeits draw nobody.
        var fixtures = (await Repo<Fixture, FixtureId>(s).ListAsync(f => f.Date == date, cancellationToken).ConfigureAwait(false)).ToDictionary(f => f.Id);
        if (fixtures.Count > 0)
        {
            var ids = fixtures.Keys.ToList();
            var played = await Repo<Match, MatchId>(s).ListAsync(m => ids.Contains(m.FixtureId) && !m.IsForfeit, cancellationToken).ConfigureAwait(false);
            var competitionIds = fixtures.Values.Select(f => f.CompetitionId).Distinct().ToList();
            var cups = (await Repo<Competition, CompetitionId>(s).ListAsync(c => competitionIds.Contains(c.Id) && c.Type != CompetitionType.League, cancellationToken).ConfigureAwait(false))
                .Select(c => c.Id).ToHashSet();
            foreach (var fixture in played.Select(m => fixtures[m.FixtureId]).OrderBy(f => f.Id.Value))
            {
                if (!clubs.TryGetValue(fixture.HomeClubId, out var home) || !clubs.TryGetValue(fixture.AwayClubId, out var away))
                {
                    continue;
                }

                var big = fixture.Stage != FixtureStage.RegularSeason || cups.Contains(fixture.CompetitionId);
                var tickets = model.TicketSales(home.Reputation, Capacity(home), big);
                var matchday = model.MatchdayCost(Capacity(home));
                var trip = model.TripCost(away.Reputation, away.CountryId != home.CountryId);
                Ledger.Book(s, home, date, FinanceCategory.TicketSales, tickets, $"Home game against {away.Name}");
                Ledger.Book(s, home, date, FinanceCategory.ArenaCosts, -matchday, $"Home game against {away.Name}");
                Ledger.Book(s, away, date, FinanceCategory.TravelCosts, -trip, $"Away game at {home.Name}");
                income += tickets;
                costs += matchday + trip;
            }
        }

        if (ClubFinances.IsIncomeDay(date))
        {
            var budgets = await CurrentBudgetsAsync(s, model, date, cancellationToken).ConfigureAwait(false);
            foreach (var club in clubs.Values.OrderBy(c => c.Id.Value))
            {
                // Without a budget (a club outside the career's seasons) the board funds the wage budget, as before Step 23.
                var sponsorship = budgets.TryGetValue(club.Id, out var budget) ? budget.Sponsorship / 12 : club.WageBudget / 12;
                var upkeep = model.MonthlyUpkeep(Capacity(club));
                Ledger.Book(s, club, date, FinanceCategory.Sponsorship, sponsorship, "Monthly sponsorship instalment");
                Ledger.Book(s, club, date, FinanceCategory.ArenaCosts, -upkeep, "Monthly arena upkeep");
                income += sponsorship;
                costs += upkeep;
            }
        }

        return new FinanceDay(salaries, income, costs);
    }

    /// <summary>
    /// Transfer budget left to each club this financial year (docs/FINANCES.md §5): the budget plus the net of fees, loan
    /// payments and signing bonuses booked so far. Clubs without a budget are missing (only their balance limits them).
    /// </summary>
    internal static async Task<long?> TransferBudgetLeftAsync(IServiceProvider s, FinanceModel model, ClubId clubId, DateOnly today, CancellationToken cancellationToken)
    {
        var (budget, season) = await CurrentBudgetAsync(s, model, clubId, today, cancellationToken).ConfigureAwait(false);
        if (budget is null || season is null)
        {
            return null;
        }

        var (from, to) = model.Year(season.StartDate);
        var ledger = await Repo<FinancialTransaction, FinancialTransactionId>(s)
            .ListAsync(t => t.ClubId == clubId && t.Date >= from && t.Date <= to, cancellationToken).ConfigureAwait(false);
        return TransferBudgetLeft(budget, ledger);
    }

    private static long TransferBudgetLeft(ClubBudget budget, IEnumerable<FinancialTransaction> ledger) =>
        budget.TransferBudget + ledger.Where(t => TransferCategories.Contains(t.Category)).Sum(t => t.Amount);

    /// <summary>The club's budget for the financial year holding <paramref name="date"/>, else its latest earlier one.</summary>
    private static async Task<(ClubBudget? Budget, Season? Season)> CurrentBudgetAsync(IServiceProvider s, FinanceModel model, ClubId clubId, DateOnly date, CancellationToken cancellationToken)
    {
        var budgets = await Repo<ClubBudget, ClubBudgetId>(s).ListAsync(b => b.ClubId == clubId, cancellationToken).ConfigureAwait(false);
        if (budgets.Count == 0)
        {
            return (null, null);
        }

        var ids = budgets.Select(b => b.SeasonId).ToList();
        var seasons = (await Repo<Season, SeasonId>(s).ListAsync(x => ids.Contains(x.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(x => x.Id);
        var best = budgets.Where(b => seasons.ContainsKey(b.SeasonId) && model.Year(seasons[b.SeasonId].StartDate).From <= date)
            .OrderByDescending(b => seasons[b.SeasonId].StartDate)
            .FirstOrDefault();
        return best is null ? (null, null) : (best, seasons[best.SeasonId]);
    }

    /// <summary>Every club's current budget on <paramref name="date"/> (see <see cref="CurrentBudgetAsync"/>).</summary>
    private static async Task<Dictionary<ClubId, ClubBudget>> CurrentBudgetsAsync(IServiceProvider s, FinanceModel model, DateOnly date, CancellationToken cancellationToken)
    {
        var seasons = (await Repo<Season, SeasonId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false))
            .Where(x => model.Year(x.StartDate).From <= date)
            .ToDictionary(x => x.Id);
        var ids = seasons.Keys.ToList();
        return (await Repo<ClubBudget, ClubBudgetId>(s).ListAsync(b => ids.Contains(b.SeasonId), cancellationToken).ConfigureAwait(false))
            .GroupBy(b => b.ClubId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => seasons[b.SeasonId].StartDate).First());
    }

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();
}
