using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Domain.Transfers;
using Ebm.Services.Clubs;
using Ebm.Services.Contracts;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Transfers;

/// <summary>
/// Implements <see cref="ITransferService"/> over the repositories, the <see cref="TransferNegotiator"/> (fees), the
/// <see cref="ContractNegotiator"/> (the player's contract) and the <see cref="ClubRecruiter"/> (AI clubs).
/// </summary>
internal sealed class TransferService : ITransferService
{
    private static readonly RosterIssueCode[] BlockingIssues = [RosterIssueCode.TooManyPlayers, RosterIssueCode.TooManyForeignPlayers, RosterIssueCode.TooManyNonEuPlayers, RosterIssueCode.UnderMinimumAge];

    private readonly IServiceScopeFactory _scopes;
    private readonly PlayerRatingCalculator _calculator;
    private readonly ContractNegotiator _salaries;
    private readonly TransferNegotiator _fees;
    private readonly ClubRecruiter _recruiter;
    private readonly IContractService _contracts;
    private readonly IClubService _clubs;
    private readonly GameClock _time;

    public TransferService(
        IServiceScopeFactory scopes,
        PlayerRatingCalculator calculator,
        ContractNegotiator salaries,
        TransferNegotiator fees,
        ClubRecruiter recruiter,
        IContractService contracts,
        IClubService clubs,
        GameClock time)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(salaries);
        ArgumentNullException.ThrowIfNull(fees);
        ArgumentNullException.ThrowIfNull(recruiter);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(clubs);
        ArgumentNullException.ThrowIfNull(time);
        _scopes = scopes;
        _calculator = calculator;
        _salaries = salaries;
        _fees = fees;
        _recruiter = recruiter;
        _contracts = contracts;
        _clubs = clubs;
        _time = time;
    }

    private DateOnly Today => _time.Today;

    public async Task<IReadOnlyList<MarketPlayer>> SearchAsync(PlayerSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        using var scope = _scopes.CreateScope();
        var market = await Market.LoadAsync(this, scope.ServiceProvider, Today, cancellationToken).ConfigureAwait(false);
        var forClub = search.ForClub is { } id ? market.Clubs.GetValueOrDefault(id) : null;
        var name = search.Name?.Trim();
        return
        [
            .. market.Players
                .Where(p => string.IsNullOrEmpty(name) || p.FullName.Contains(name, StringComparison.CurrentCultureIgnoreCase))
                .Where(p => search.Position is not { } position || p.Position == position || p.SecondaryPositions.Contains(position))
                .Where(p => search.NationalityId is not { } nationality || p.NationalityId == nationality)
                .Select(p => market.Describe(p, forClub))
                .Where(m => forClub is null || m.Club?.Id != forClub.Id)
                .Where(m => (search.MinAge is not { } minAge || m.Age >= minAge) && (search.MaxAge is not { } maxAge || m.Age <= maxAge))
                .Where(m => (search.MinOverall is not { } minOverall || m.Overall >= minOverall) && (search.MaxOverall is not { } maxOverall || m.Overall <= maxOverall))
                .Where(m => search.Filter switch
                {
                    MarketFilter.FreeAgents => m.Status == MarketStatus.FreeAgent,
                    MarketFilter.UnderContract => m.Status == MarketStatus.UnderContract,
                    MarketFilter.AvailableForLoan => m.Status == MarketStatus.UnderContract && m.Valuation.AvailableForLoan && m.Contract!.IsRunningOn(market.Today),
                    _ => true,
                })
                .Where(m => search.MaxFee is not { } maxFee || m.IsFreeAgent || m.Valuation.BuyNowFee <= maxFee)
                .Where(m => search.MaxSalary is not { } maxSalary || m.AskingSalary <= maxSalary)
                .OrderByDescending(m => m.Overall)
                .ThenBy(m => m.Player.LastName)
                .ThenBy(m => m.Player.FirstName)
                .Take(Math.Max(1, search.MaxResults)),
        ];
    }

    public async Task<MarketPlayer?> GetMarketPlayerAsync(PlayerId playerId, ClubId? forClub = null, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var market = await Market.LoadAsync(this, scope.ServiceProvider, Today, cancellationToken).ConfigureAwait(false);
        var player = market.Players.FirstOrDefault(p => p.Id == playerId);
        return player is null ? null : market.Describe(player, forClub is { } id ? market.Clubs.GetValueOrDefault(id) : null);
    }

    public async Task<TransferResponse> NegotiateFeeAsync(PlayerId playerId, ClubId buyerId, long fee, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var market = await Market.LoadAsync(this, scope.ServiceProvider, Today, cancellationToken).ConfigureAwait(false);
        var (_, buyer, listing) = await market.ForSaleAsync(playerId, buyerId, cancellationToken).ConfigureAwait(false);
        CheckFee(fee);
        _ = buyer;
        return _fees.EvaluateFee(listing.Valuation, fee);
    }

    public async Task<TransferResponse> NegotiateLoanAsync(PlayerId playerId, ClubId borrowerId, DateOnly until, long fee, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var market = await Market.LoadAsync(this, scope.ServiceProvider, Today, cancellationToken).ConfigureAwait(false);
        var (_, _, listing) = await market.ForSaleAsync(playerId, borrowerId, cancellationToken).ConfigureAwait(false);
        CheckFee(fee);
        CheckLoanDates(market, listing, until);
        return _fees.EvaluateLoan(listing.Valuation, fee, TransferNegotiator.LoanYears(market.Today, until));
    }

    public async Task<NegotiationResponse> NegotiateContractAsync(PlayerId playerId, ClubId buyerId, ContractTerms terms, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);
        using var scope = _scopes.CreateScope();
        var market = await Market.LoadAsync(this, scope.ServiceProvider, Today, cancellationToken).ConfigureAwait(false);
        var (player, buyer, listing) = await market.ForSaleAsync(playerId, buyerId, cancellationToken).ConfigureAwait(false);
        return _salaries.Evaluate(Context(player, buyer, market.Today, listing.Contract!.AnnualSalary), terms);
    }

    public async Task<TransferReceipt> BuyAsync(PlayerId playerId, ClubId buyerId, long fee, ContractTerms terms, RosterRole joinAs = RosterRole.Bench, bool enforceWageBudget = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);
        CheckJoinRole(joinAs);
        CheckFee(fee);
        using var scope = _scopes.CreateScope();
        var market = await Market.LoadAsync(this, scope.ServiceProvider, Today, cancellationToken).ConfigureAwait(false);
        var today = market.Today;
        var (player, buyer, listing) = await market.ForSaleAsync(playerId, buyerId, cancellationToken).ConfigureAwait(false);
        var contract = listing.Contract!;
        var seller = market.Clubs[contract.ClubId];
        if (terms.StartDate != today)
        {
            throw new DomainValidationException($"A transfer takes effect today: the new contract must start on {today:yyyy-MM-dd}.");
        }

        CheckUpcoming(market, player, terms.EndDate, "the new contract must end before that");
        var feeAnswer = _fees.EvaluateFee(listing.Valuation, fee);
        if (!feeAnswer.IsAccepted)
        {
            throw new DomainException($"{seller.Name} refuses: {feeAnswer.Message}");
        }

        var playerAnswer = _salaries.Evaluate(Context(player, buyer, today, contract.AnnualSalary), terms);
        if (!playerAnswer.IsAccepted)
        {
            throw new DomainException($"{player.FullName} refuses the contract: {playerAnswer.Message}");
        }

        var warnings = await CheckLimitsAsync(market, buyer, player, joinAs, fee + terms.SigningBonus, terms.AnnualSalary, enforceWageBudget, cancellationToken).ConfigureAwait(false);

        contract.Transfer(today);
        var signed = Contract.ForPlayer(ContractId.New(), buyerId, playerId, terms);
        market.Data.Contracts.Add(signed);
        seller.ChangeBalance(fee);
        buyer.ChangeBalance(-(fee + terms.SigningBonus));
        await ClubMembership.JoinAsync(scope.ServiceProvider, playerId, buyerId, today, joinAs, cancellationToken).ConfigureAwait(false);
        var viaClause = contract.ReleaseClause is { } clause && fee >= clause && fee < listing.Valuation.AskingFee;
        var transfer = Transfer.Permanent(TransferId.New(), playerId, seller.Id, buyerId, today, fee, signed.Id, viaClause);
        market.Data.Transfers.Add(transfer);
        await market.Data.SaveAsync(cancellationToken).ConfigureAwait(false);
        return new TransferReceipt(
            transfer.Id,
            transfer.Kind,
            fee,
            signed.Id,
            warnings,
            $"{player.FullName} joins {buyer.Name} from {seller.Name} for € {fee:N0}" + (viaClause ? " (release clause)" : string.Empty) +
            $" on € {terms.AnnualSalary:N0} a year until {terms.EndDate:yyyy-MM-dd}.");
    }

    public async Task<TransferReceipt> LoanAsync(PlayerId playerId, ClubId borrowerId, DateOnly until, long fee, RosterRole joinAs = RosterRole.Bench, bool enforceWageBudget = false, CancellationToken cancellationToken = default)
    {
        CheckJoinRole(joinAs);
        CheckFee(fee);
        using var scope = _scopes.CreateScope();
        var market = await Market.LoadAsync(this, scope.ServiceProvider, Today, cancellationToken).ConfigureAwait(false);
        var today = market.Today;
        var (player, borrower, listing) = await market.ForSaleAsync(playerId, borrowerId, cancellationToken).ConfigureAwait(false);
        var contract = listing.Contract!;
        var parent = market.Clubs[contract.ClubId];
        CheckLoanDates(market, listing, until);
        var answer = _fees.EvaluateLoan(listing.Valuation, fee, TransferNegotiator.LoanYears(today, until));
        if (!answer.IsAccepted)
        {
            throw new DomainException($"{parent.Name} refuses: {answer.Message}");
        }

        var warnings = await CheckLimitsAsync(market, borrower, player, joinAs, fee, contract.AnnualSalary, enforceWageBudget, cancellationToken).ConfigureAwait(false);

        contract.SendOnLoan(today, until);
        var loanContract = Contract.ForPlayer(ContractId.New(), borrowerId, playerId, new ContractTerms(today, until, contract.AnnualSalary, contract.PaymentFrequency));
        market.Data.Contracts.Add(loanContract);
        parent.ChangeBalance(fee);
        borrower.ChangeBalance(-fee);
        await ClubMembership.JoinAsync(scope.ServiceProvider, playerId, borrowerId, today, joinAs, cancellationToken).ConfigureAwait(false);
        var loan = Transfer.OnLoan(TransferId.New(), playerId, parent.Id, borrowerId, today, until, fee, loanContract.Id, contract.Id);
        market.Data.Transfers.Add(loan);
        await market.Data.SaveAsync(cancellationToken).ConfigureAwait(false);
        return new TransferReceipt(
            loan.Id,
            TransferKind.Loan,
            fee,
            loanContract.Id,
            warnings,
            $"{player.FullName} joins {borrower.Name} on loan from {parent.Name} until {until:yyyy-MM-dd} for € {fee:N0}; {borrower.Name} pays his salary of € {contract.AnnualSalary:N0} a year meanwhile.");
    }

    public async Task<TransferReceipt> RecallAsync(TransferId loanId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var loan = await data.Transfers.FindAsync(loanId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The loan no longer exists in the database.");
        if (!loan.IsOpenLoan)
        {
            throw new DomainValidationException("This is not an open loan.");
        }

        if (data.Today <= loan.Date)
        {
            throw new DomainValidationException("A loan can be recalled from the day after it started.");
        }

        var returnDay = data.Today <= loan.LoanEndDate!.Value ? data.Today : loan.LoanEndDate.Value.AddDays(1);
        var (back, player, parentClub) = await ReturnLoanAsync(scope.ServiceProvider, data, loan, returnDay, cancellationToken).ConfigureAwait(false);
        await data.SaveAsync(cancellationToken).ConfigureAwait(false);
        return new TransferReceipt(back.Id, TransferKind.LoanReturn, 0, null, [], $"{player.FullName} is back at {parentClub.Name} from {returnDay:yyyy-MM-dd}.");
    }

    public async Task<ClubTransfers?> GetClubTransfersAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        if (await data.Clubs.FindAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } club)
        {
            return null;
        }

        var transfers = await data.Transfers.ListAsync(t => t.FromClubId == clubId || t.ToClubId == clubId, cancellationToken).ConfigureAwait(false);
        var lines = await data.LinesAsync(transfers, cancellationToken).ConfigureAwait(false);
        return new ClubTransfers(
            club,
            data.Today,
            lines,
            [.. lines.Where(l => l.Transfer.IsOpenLoan && l.Transfer.FromClubId == clubId)],
            [.. lines.Where(l => l.Transfer.IsOpenLoan && l.Transfer.ToClubId == clubId)]);
    }

    public async Task<IReadOnlyList<TransferLine>> GetPlayerTransfersAsync(PlayerId playerId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var transfers = await data.Transfers.ListAsync(t => t.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        return await data.LinesAsync(transfers, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TransferDateChanges> ProcessTransferDatesAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;
        var returned = new List<PlayerId>();
        using (var scope = _scopes.CreateScope())
        {
            var data = new Data(scope.ServiceProvider, today);
            var due = await data.Transfers.ListAsync(t => t.Kind == TransferKind.Loan && t.ReturnedOn == null && t.LoanEndDate < today, cancellationToken).ConfigureAwait(false);
            foreach (var loan in due.OrderBy(l => l.LoanEndDate))
            {
                var (_, player, _) = await ReturnLoanAsync(scope.ServiceProvider, data, loan, loan.LoanEndDate!.Value.AddDays(1), cancellationToken).ConfigureAwait(false);
                returned.Add(player.Id);
            }

            await data.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        var contracts = await _contracts.ProcessContractDatesAsync(cancellationToken).ConfigureAwait(false);
        return returned.Count == 0 && contracts == ContractDateChanges.None ? TransferDateChanges.None : new TransferDateChanges(returned.Count, returned, contracts);
    }

    public async Task<TransferWindowReport> RunAiTransferWindowAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(managedClubs);
        var today = Today;
        var managed = managedClubs.ToHashSet();
        var reports = new Dictionary<ClubId, (Club Club, List<string> Needs, List<string> Actions, int Transactions)>();
        var taken = new HashSet<PlayerId>();

        // The surplus every AI club puts up for sale, priced by its own club.
        var forSale = new Dictionary<PlayerId, (ClubId From, long Fee)>();
        List<Club> aiClubs;
        using (var scope = _scopes.CreateScope())
        {
            var market = await Market.LoadAsync(this, scope.ServiceProvider, today, cancellationToken).ConfigureAwait(false);
            aiClubs = [.. market.Clubs.Values.Where(c => !managed.Contains(c.Id)).OrderByDescending(c => c.Reputation).ThenBy(c => c.Name)];
            foreach (var club in aiClubs)
            {
                var report = (club, new List<string>(), new List<string>(), 0);
                reports[club.Id] = report;
                if (await _clubs.GetRosterAsync(club.Id, cancellationToken).ConfigureAwait(false) is not { } roster)
                {
                    continue;
                }

                foreach (var sale in _recruiter.Surplus(Situation(market, club, roster)))
                {
                    var listing = market.Describe(market.PlayerById[sale.Player.PlayerId], null);
                    if (listing.Status != MarketStatus.UnderContract || listing.Contract?.ClubId != club.Id || !listing.Contract.IsRunningOn(today))
                    {
                        continue; // loaned-in players and players without a contract cannot be sold
                    }

                    forSale[sale.Player.PlayerId] = (club.Id, listing.Valuation.AskingFee);
                    report.Item3.Add($"Listed {sale.Player.Name} for sale at € {listing.Valuation.AskingFee:N0} ({sale.Reason}).");
                }
            }
        }

        // Each club shops in turn, richest reputation first; the market is reloaded after every club's deals.
        foreach (var club in aiClubs)
        {
            var report = reports[club.Id];
            if (await _clubs.GetRosterAsync(club.Id, cancellationToken).ConfigureAwait(false) is not { } roster)
            {
                continue;
            }

            Market market;
            using (var scope = _scopes.CreateScope())
            {
                market = await Market.LoadAsync(this, scope.ServiceProvider, today, cancellationToken).ConfigureAwait(false);
            }

            var current = market.Clubs[club.Id];
            var situation = Situation(market, current, roster);
            var candidates = market.Players
                .Where(p => !taken.Contains(p.Id))
                .Select(p => market.Describe(p, current))
                .Where(m => m.Status == MarketStatus.FreeAgent || (forSale.TryGetValue(m.Player.Id, out var sale) && sale.From == m.Contract?.ClubId && m.Contract.IsRunningOn(today)))
                .Where(m => m.Club?.Id != club.Id)
                .Select(m => new Candidate(
                    m.Player.Id,
                    m.Player.FullName,
                    m.Player.Position,
                    m.Player.SecondaryPositions,
                    m.Overall,
                    m.Age,
                    m.AskingSalary,
                    m.IsFreeAgent ? 0 : forSale[m.Player.Id].Fee,
                    m.IsFreeAgent ? null : m.Contract!.ClubId,
                    m.Player.NationalityId != current.CountryId,
                    market.IsNonEu(m.Player)))
                .ToList();
            var plan = _recruiter.Plan(situation, candidates);
            report.Needs.AddRange(plan.Needs.Select(n => n.Reason));
            var active = roster.Players.Count(p => p.Role.IsActive());
            foreach (var signing in plan.Signings)
            {
                var candidate = signing.Candidate;
                var joinAs = active >= situation.Rules.MatchdayPlayers ? RosterRole.Inactive : RosterRole.Bench;
                var terms = new ContractTerms(today, today.AddYears(signing.Years).AddDays(-1), signing.Salary);
                try
                {
                    if (candidate.IsFreeAgent)
                    {
                        await _contracts.SignAsync(candidate.PlayerId, club.Id, terms, joinAs, cancellationToken).ConfigureAwait(false);
                        report.Actions.Add($"Signed {candidate.Name} as a free agent on € {signing.Salary:N0} a year until {terms.EndDate:yyyy-MM-dd} ({signing.Reason}).");
                    }
                    else
                    {
                        var receipt = await BuyAsync(candidate.PlayerId, club.Id, candidate.Fee, terms, joinAs, enforceWageBudget: true, cancellationToken).ConfigureAwait(false);
                        var seller = reports.GetValueOrDefault(candidate.FromClubId!.Value);
                        seller.Actions?.Add($"Sold {candidate.Name} to {club.Name} for € {receipt.Fee:N0}.");
                        report.Actions.Add($"Bought {candidate.Name} from {market.Clubs[candidate.FromClubId!.Value].Name} for € {receipt.Fee:N0}, on € {signing.Salary:N0} a year until {terms.EndDate:yyyy-MM-dd} ({signing.Reason}).");
                    }

                    taken.Add(candidate.PlayerId);
                    forSale.Remove(candidate.PlayerId);
                    report.Transactions++;
                    active += joinAs == RosterRole.Bench ? 1 : 0;
                }
                catch (DomainException ex)
                {
                    report.Actions.Add($"Could not sign {candidate.Name}: {ex.Message}");
                }
                catch (DataConstraintException ex)
                {
                    report.Actions.Add($"Could not sign {candidate.Name}: {ex.Message}");
                }
            }

            foreach (var need in plan.Unmet)
            {
                report.Actions.Add($"Found nobody affordable: {need.Reason}.");
            }

            reports[club.Id] = report;
        }

        // Surplus nobody bought: squads over the registration limit release their weakest players.
        foreach (var club in aiClubs)
        {
            var report = reports[club.Id];
            if (await _clubs.GetRosterAsync(club.Id, cancellationToken).ConfigureAwait(false) is not { } roster)
            {
                continue;
            }

            Market market;
            using (var scope = _scopes.CreateScope())
            {
                market = await Market.LoadAsync(this, scope.ServiceProvider, today, cancellationToken).ConfigureAwait(false);
            }

            var situation = Situation(market, market.Clubs[club.Id], roster);
            var excess = situation.FirstTeam.Count - situation.Rules.MaxPlayers;
            foreach (var weakest in situation.FirstTeam.OrderBy(m => m.Overall).ThenByDescending(m => m.Salary).Take(Math.Max(0, excess)))
            {
                var contract = market.RunningContractAt(weakest.PlayerId, club.Id);
                if (contract is null || contract.Status != ContractStatus.Active)
                {
                    report.Actions.Add($"Cannot release {weakest.Name}: no contract of his own with the club.");
                    continue;
                }

                try
                {
                    var paid = await _contracts.TerminateAsync(contract.Id, TerminationKind.MutualAgreement, null, cancellationToken).ConfigureAwait(false);
                    report.Actions.Add($"Released {weakest.Name} by mutual agreement for € {paid:N0} (squad over the limit of {situation.Rules.MaxPlayers}).");
                    report.Transactions++;
                }
                catch (DomainException ex)
                {
                    report.Actions.Add($"Could not release {weakest.Name}: {ex.Message}");
                }
            }

            if (situation.WageBill > situation.WageBudget)
            {
                report.Actions.Add($"Still over the wage budget by € {situation.WageBill - situation.WageBudget:N0}.");
            }

            reports[club.Id] = report;
        }

        return new TransferWindowReport(today, [.. aiClubs.Select(c => reports[c.Id]).Select(r => new ClubWindowReport(r.Club, r.Needs, r.Actions, r.Transactions))]);
    }

    private ClubSituation Situation(Market market, Club club, ClubRoster roster)
    {
        var members = roster.Players
            .Where(p => p.Role.IsFirstTeam())
            .Select(p => new SquadMember(
                p.Player.Id,
                p.Player.FullName,
                p.Player.Position,
                p.Player.SecondaryPositions,
                p.Evaluation.Overall,
                p.Age,
                market.RunningContractAt(p.Player.Id, club.Id)?.AnnualSalary ?? 0,
                p.Role,
                p.IsForeign,
                p.IsNonEu))
            .ToList();
        var rules = SquadRules.Strictest([SquadRules.Default, .. roster.Registrations.Select(r => r.Rules)]);
        return new ClubSituation(club.Id, club.Balance, club.WageBudget, market.WageBills.GetValueOrDefault(club.Id), members, rules);
    }

    /// <summary>Refuses a move the club cannot afford or register; returns the limits it strains (docs/TRANSFERS.md §5).</summary>
    private async Task<IReadOnlyList<string>> CheckLimitsAsync(Market market, Club club, Player player, RosterRole joinAs, long cash, long salary, bool enforceWageBudget, CancellationToken cancellationToken)
    {
        if (cash > club.Balance)
        {
            throw new DomainException($"{club.Name} cannot pay € {cash:N0}: its balance is € {club.Balance:N0}.");
        }

        // Only the problems the newcomer causes count: a squad that was already short or had no lineup stays the club's business.
        var roster = await _clubs.GetRosterAsync(club.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The club no longer exists in the database.");
        var competitions = roster.Registrations.Select(r => r.Competition).ToList();
        var current = roster.Players.Where(p => p.Player.Id != player.Id).Select(p => p.ToMember()).ToList();
        var before = Errors(ClubRoster.Check(current, competitions));
        var after = Errors(ClubRoster.Check(
            [.. current, new RosterMember(player.Id, player.FullName, joinAs, null, RosterRoles.AvailabilityOf(player), player.NationalityId != club.CountryId, player.AgeOn(market.Today), null, true, market.IsNonEu(player))],
            competitions));
        if (after.FirstOrDefault(e => BlockingIssues.Contains(e.Code)) is { } blocking)
        {
            throw new DomainException($"{club.Name} cannot register {player.FullName}: {blocking.Message}");
        }

        var warnings = after.Where(e => before.All(b => b.Code != e.Code)).Select(e => e.Message).ToList();
        var bill = market.WageBills.GetValueOrDefault(club.Id) + salary;
        if (bill > club.WageBudget)
        {
            var message = $"The wage bill of {club.Name} becomes € {bill:N0}, € {bill - club.WageBudget:N0} over its wage budget.";
            if (enforceWageBudget)
            {
                throw new DomainException(message);
            }

            warnings.Add(message);
        }

        return warnings;
    }

    private static List<RosterIssue> Errors((RosterValidation Club, IReadOnlyList<CompetitionRegistrationCheck> Registrations) check) =>
        [.. check.Club.Errors.Concat(check.Registrations.SelectMany(r => r.Validation.Errors)).DistinctBy(e => e.Message)];

    /// <summary>Brings a loaned player back on <paramref name="returnDay"/>: ends the loan contract, reactivates the parent contract, moves him home.</summary>
    private static async Task<(Transfer Return, Player Player, Club ParentClub)> ReturnLoanAsync(IServiceProvider services, Data data, Transfer loan, DateOnly returnDay, CancellationToken cancellationToken)
    {
        var player = await data.Players.FindAsync(loan.PlayerId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The player no longer exists in the database.");
        var parentClub = await data.Clubs.FindAsync(loan.FromClubId!.Value, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The parent club no longer exists in the database.");
        var parent = await data.Contracts.FindAsync(loan.ParentContractId!.Value, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The parent contract no longer exists in the database.");
        var loanContract = await data.Contracts.FindAsync(loan.ContractId!.Value, cancellationToken).ConfigureAwait(false);

        if (loanContract is { Status: ContractStatus.Active })
        {
            if (returnDay <= loanContract.EndDate)
            {
                loanContract.Terminate(returnDay, 0);
            }
            else if (data.Today > loanContract.EndDate)
            {
                loanContract.Expire(data.Today);
            }
        }

        if (parent.Status == ContractStatus.OnLoan)
        {
            parent.ReturnFromLoan();
        }

        // His spell at the loan club ends the day before he is back. If the parent contract ran out with the loan he
        // leaves the loan club as a free agent; otherwise he rejoins his parent club's bench.
        if (parent.IsRunningOn(returnDay))
        {
            foreach (var open in await ClubMembership.OpenSpellsAsync(services, player.Id, cancellationToken).ConfigureAwait(false))
            {
                open.Close(open.StartDate > returnDay.AddDays(-1) ? open.StartDate : returnDay.AddDays(-1));
            }

            await ClubMembership.JoinAsync(services, player.Id, parentClub.Id, returnDay, RosterRole.Bench, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ClubMembership.LeaveAsync(services, player.Id, loan.ToClubId, returnDay.AddDays(-1), cancellationToken).ConfigureAwait(false);
        }

        loan.Return(returnDay);
        var back = Transfer.LoanReturn(TransferId.New(), player.Id, loan.ToClubId, parentClub.Id, returnDay, parent.Id);
        data.Transfers.Add(back);
        return (back, player, parentClub);
    }

    private NegotiationContext Context(Player player, Club club, DateOnly today, long? currentSalary)
    {
        var evaluation = _calculator.Evaluate(player);
        return new NegotiationContext(evaluation.Overall, evaluation.PotentialRating, player.Reputation, player.AgeOn(today), club.Reputation, currentSalary);
    }

    private static void CheckLoanDates(Market market, MarketPlayer listing, DateOnly until)
    {
        var contract = listing.Contract!;
        if (until <= market.Today || until > contract.EndDate)
        {
            throw new DomainValidationException($"A loan must end after today and no later than the contract's end ({contract.EndDate:yyyy-MM-dd}).");
        }

        if (until > market.Today.AddYears(Loan.MaxYears))
        {
            throw new DomainValidationException($"A loan lasts at most {Loan.MaxYears} years.");
        }

        CheckUpcoming(market, listing.Player, until, "the loan must end before that");
    }

    /// <summary>Refuses a move that would overlap a contract the player has already signed in advance.</summary>
    private static void CheckUpcoming(Market market, Player player, DateOnly lastDay, string advice)
    {
        var upcoming = market.ContractsOf(player.Id).Where(c => c.IsUpcomingOn(market.Today) && c.StartDate <= lastDay).OrderBy(c => c.StartDate).FirstOrDefault();
        if (upcoming is not null)
        {
            throw new DomainValidationException(
                $"{player.FullName} has already signed with {market.Clubs.GetValueOrDefault(upcoming.ClubId)?.Name ?? "another club"} from {upcoming.StartDate:yyyy-MM-dd}; {advice}.");
        }
    }

    private static void CheckFee(long fee)
    {
        if (fee < 0 || fee > Transfer.MaxFee)
        {
            throw new DomainValidationException($"A fee must be between 0 and {Transfer.MaxFee:N0} euros.");
        }
    }

    private static void CheckJoinRole(RosterRole joinAs)
    {
        if (joinAs is not (RosterRole.Bench or RosterRole.Inactive or RosterRole.Youth))
        {
            throw new DomainValidationException("A new player joins the bench, the inactive list or the youth squad; pick his starting or rotation role in the squad.");
        }
    }

    /// <summary>Repositories shared by the use cases, within one scope.</summary>
    private sealed class Data(IServiceProvider services, DateOnly today)
    {
        public DateOnly Today { get; } = today;

        public IRepository<Transfer, TransferId> Transfers => Repo<Transfer, TransferId>();

        public IRepository<Contract, ContractId> Contracts => Repo<Contract, ContractId>();

        public IRepository<Player, PlayerId> Players => Repo<Player, PlayerId>();

        public IRepository<Club, ClubId> Clubs => Repo<Club, ClubId>();

        public IRepository<TEntity, TId> Repo<TEntity, TId>()
            where TEntity : Entity<TId>
            where TId : struct, IEntityId<TId> =>
            services.GetRequiredService<IRepository<TEntity, TId>>();

        public Task SaveAsync(CancellationToken cancellationToken) => services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);

        /// <summary>Transfer lines with player and club names, newest first.</summary>
        public async Task<IReadOnlyList<TransferLine>> LinesAsync(IReadOnlyList<Transfer> transfers, CancellationToken cancellationToken)
        {
            var playerIds = transfers.Select(t => t.PlayerId).Distinct().ToList();
            var players = (await Players.ListAsync(p => playerIds.Contains(p.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id, p => p.FullName);
            var clubs = (await Clubs.ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);
            return
            [
                .. transfers
                    .OrderByDescending(t => t.Date)
                    .ThenByDescending(t => t.Kind == TransferKind.LoanReturn)
                    .ThenBy(t => players.GetValueOrDefault(t.PlayerId))
                    .Select(t => new TransferLine(
                        t,
                        players.GetValueOrDefault(t.PlayerId) ?? string.Empty,
                        t.FromClubId is { } from ? clubs.GetValueOrDefault(from) : null,
                        clubs.GetValueOrDefault(t.ToClubId) ?? string.Empty)),
            ];
        }
    }

    /// <summary>Everything the market needs about every player, loaded once per call within one scope.</summary>
    private sealed class Market
    {
        private readonly TransferService _service;
        private readonly Dictionary<PlayerId, List<Contract>> _contracts;
        private readonly Dictionary<PlayerId, ClubId> _members;
        private readonly Dictionary<PlayerId, RosterSpot> _spots;
        private readonly Dictionary<CountryId, Country> _countries;

        private Market(
            TransferService service,
            Data data,
            IReadOnlyList<Player> players,
            Dictionary<ClubId, Club> clubs,
            Dictionary<PlayerId, List<Contract>> contracts,
            Dictionary<PlayerId, ClubId> members,
            Dictionary<PlayerId, RosterSpot> spots,
            Dictionary<CountryId, Country> countries)
        {
            _service = service;
            Data = data;
            Players = players;
            PlayerById = players.ToDictionary(p => p.Id);
            Clubs = clubs;
            _contracts = contracts;
            _members = members;
            _spots = spots;
            _countries = countries;
            WageBills = contracts.Values
                .SelectMany(c => c)
                .Where(c => c.IsRunningOn(data.Today) && c.Status != ContractStatus.OnLoan)
                .GroupBy(c => c.ClubId)
                .ToDictionary(g => g.Key, g => g.Sum(c => c.AnnualSalary));
        }

        /// <summary>None of the player's nationalities is in the EU/EEA (non-EU player limits).</summary>
        public bool IsNonEu(Player player) => !player.HasNationality(c => _countries.GetValueOrDefault(c)?.IsEuEea == true);

        public Data Data { get; }

        public DateOnly Today => Data.Today;

        public IReadOnlyList<Player> Players { get; }

        public Dictionary<PlayerId, Player> PlayerById { get; }

        public Dictionary<ClubId, Club> Clubs { get; }

        /// <summary>Annual salaries of the running contracts per club, players out on loan excluded.</summary>
        public Dictionary<ClubId, long> WageBills { get; }

        public static async Task<Market> LoadAsync(TransferService service, IServiceProvider services, DateOnly today, CancellationToken cancellationToken)
        {
            var data = new Data(services, today);
            var players = await data.Players.ListAsync(null, cancellationToken).ConfigureAwait(false);
            var clubs = (await data.Clubs.ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
            var contracts = (await data.Contracts.ListAsync(c => c.PlayerId != null, cancellationToken).ConfigureAwait(false))
                .GroupBy(c => c.PlayerId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());
            var members = await ClubMembership.CurrentClubsAsync(services, today, cancellationToken).ConfigureAwait(false);
            var spots = (await data.Repo<RosterSpot, RosterSpotId>().ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.PlayerId);
            var countries = (await data.Repo<Country, CountryId>().ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
            return new Market(service, data, players, clubs, contracts, members, spots, countries);
        }

        public IReadOnlyList<Contract> ContractsOf(PlayerId playerId) => _contracts.GetValueOrDefault(playerId) ?? [];

        public Contract? RunningContractAt(PlayerId playerId, ClubId clubId) =>
            ContractsOf(playerId).Where(c => c.ClubId == clubId && c.IsRunningOn(Today)).OrderByDescending(c => c.StartDate).FirstOrDefault();

        /// <summary>The player, the interested club and the player's listing; refuses players who cannot be bought or loaned.</summary>
        public async Task<(Player Player, Club Club, MarketPlayer Listing)> ForSaleAsync(PlayerId playerId, ClubId clubId, CancellationToken cancellationToken)
        {
            var player = PlayerById.GetValueOrDefault(playerId) ?? throw new DomainException("The player no longer exists in the database.");
            var club = Clubs.GetValueOrDefault(clubId) ?? throw new DomainException("The club no longer exists in the database.");
            var listing = Describe(player, club);
            switch (listing.Status)
            {
                case MarketStatus.FreeAgent:
                    throw new DomainValidationException($"{player.FullName} is a free agent: offer him a contract without a fee (Club contracts).");
                case MarketStatus.OnLoan:
                    var loan = (await Data.Transfers.ListAsync(t => t.PlayerId == playerId && t.Kind == TransferKind.Loan && t.ReturnedOn == null, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
                    throw new DomainValidationException(
                        $"{player.FullName} is on loan at {listing.Club?.Name ?? "another club"} until {loan?.LoanEndDate:yyyy-MM-dd}; {listing.ParentClub?.Name ?? "his club"} must recall him first.");
                default:
                    break;
            }

            var contract = listing.Contract!;
            if (!contract.IsRunningOn(Today))
            {
                throw new DomainValidationException(
                    $"{player.FullName} has no running contract to buy out: he has signed with {Clubs.GetValueOrDefault(contract.ClubId)?.Name ?? "another club"} from {contract.StartDate:yyyy-MM-dd}. Offer him a contract after that one instead.");
            }

            if (contract.ClubId == clubId)
            {
                throw new DomainValidationException($"{player.FullName} already plays for {club.Name}; renew his contract instead.");
            }

            return (player, club, listing);
        }

        /// <summary>The player as the market sees him, with salaries priced for <paramref name="forClub"/> (an average club when <c>null</c>).</summary>
        public MarketPlayer Describe(Player player, Club? forClub)
        {
            var contracts = ContractsOf(player.Id);
            var running = contracts.Where(c => c.IsRunningOn(Today)).OrderByDescending(c => c.StartDate).ToList();
            var upcoming = contracts.Where(c => c.IsUpcomingOn(Today)).OrderBy(c => c.StartDate).FirstOrDefault();
            var memberClub = _members.TryGetValue(player.Id, out var memberId) ? Clubs.GetValueOrDefault(memberId) : null;
            var evaluation = _service._calculator.Evaluate(player);
            var age = player.AgeOn(Today);

            MarketStatus status;
            Contract? contract;
            Club? club = memberClub;
            Club? parentClub = null;
            if (running.FirstOrDefault(c => c.Status == ContractStatus.OnLoan) is { } loaned)
            {
                status = MarketStatus.OnLoan;
                contract = loaned;
                parentClub = Clubs.GetValueOrDefault(loaned.ClubId);
                club ??= running.Where(c => c.Id != loaned.Id).Select(c => Clubs.GetValueOrDefault(c.ClubId)).FirstOrDefault();
            }
            else if (running.Count > 0)
            {
                status = MarketStatus.UnderContract;
                contract = running[0];
                club ??= Clubs.GetValueOrDefault(contract.ClubId);
            }
            else if (upcoming is not null)
            {
                status = MarketStatus.UnderContract;
                contract = upcoming;
            }
            else
            {
                status = MarketStatus.FreeAgent;
                contract = null;
            }

            var role = _spots.TryGetValue(player.Id, out var spot) ? spot.Role : club is null ? null : (RosterRole?)RosterRole.Bench;
            var yearsLeft = contract is null ? 0 : Math.Max(0, (contract.EffectiveEndDate.DayNumber - Today.DayNumber + 1) / 365.25);
            var seller = contract is null ? null : Clubs.GetValueOrDefault(contract.ClubId);
            var context = new ValuationContext(
                evaluation.Overall,
                evaluation.PotentialRating,
                player.Reputation,
                age,
                yearsLeft,
                status == MarketStatus.UnderContract && contract!.IsRunningOn(Today) ? role : null,
                contract?.ReleaseClause,
                seller is { Balance: < 0 },
                seller is not null && WageBills.GetValueOrDefault(seller.Id) > seller.WageBudget);
            var valuation = status == MarketStatus.FreeAgent
                ? new PlayerValuation(_service._fees.MarketValue(context), 0, null, false, 0)
                : _service._fees.Value(context);
            var askingSalary = _service._salaries.AskingSalary(new NegotiationContext(
                evaluation.Overall, evaluation.PotentialRating, player.Reputation, age, forClub?.Reputation ?? TransferNegotiator.NeutralClubReputation, contract?.AnnualSalary));
            return new MarketPlayer(player, Today, age, evaluation, _countries.GetValueOrDefault(player.NationalityId)?.Name ?? string.Empty, club, contract, parentClub, role, status, valuation, askingSalary);
        }
    }
}
