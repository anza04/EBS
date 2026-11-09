using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Transfers;
using Ebm.Services.Clubs;
using Ebm.Services.Contracts;
using Ebm.Services.Transfers;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Transfers;

public sealed class TransferServiceTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private readonly TestClock _clock = new(Today);
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private ITransferService Market => _db.Services.GetRequiredService<ITransferService>();

    private IContractService Contracts => _db.Services.GetRequiredService<IContractService>();

    private IClubService Clubs => _db.Services.GetRequiredService<IClubService>();

    private PlayerId Mario => _world.Player.Id;

    private ClubId Home => _world.HomeClub.Id;

    private ClubId Away => _world.AwayClub.Id;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(_clock);
        _world = await SampleWorld.SeedAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task SetFinancesAsync(ClubId clubId, long balance, long wageBudget, int reputation = 50)
    {
        var club = await ClubAsync(clubId);
        await _db.WriteAsync(s =>
        {
            var tracked = s.Repo<Club, ClubId>().FindAsync(clubId).GetAwaiter().GetResult()!;
            tracked.UpdateProfile(club.City, reputation, balance, wageBudget, club.BoardExpectation);
        });
    }

    private async Task<Player> FreeAgentAsync(Position position = Position.SmallForward, int rating = 55, int birthYear = 1999)
    {
        var player = new Player(PlayerId.New(), "Free", $"Agent{Guid.NewGuid():N}"[..14], new DateOnly(birthYear, 2, 1), _world.Italy.Id, 198, 95, position, PlayerAttributes.Uniform(rating));
        await _db.WriteAsync(s => s.Repo<Player, PlayerId>().Add(player));
        return player;
    }

    /// <summary>A player under contract with <paramref name="clubId"/> (contract only, as imported data).</summary>
    private async Task<Player> ContractedAsync(ClubId clubId, Position position, int rating, long salary = 80_000)
    {
        var player = await FreeAgentAsync(position, rating);
        await _db.WriteAsync(s => s.Repo<Contract, ContractId>().Add(Contract.ForPlayer(ContractId.New(), clubId, player.Id, SampleWorld.SeasonStart, SampleWorld.SeasonStart.AddYears(2), salary)));
        return player;
    }

    private Task<Club> ClubAsync(ClubId id) => _db.ReadAsync(async s => (await s.Repo<Club, ClubId>().FindAsync(id))!);

    private Task<Contract> ContractAsync(ContractId id) => _db.ReadAsync(async s => (await s.Repo<Contract, ContractId>().FindAsync(id))!);

    private Task<IReadOnlyList<PlayerClubHistoryEntry>> SpellsAsync(PlayerId id) =>
        _db.ReadAsync(s => s.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>().ListAsync(e => e.PlayerId == id));

    private Task<IReadOnlyList<RosterSpot>> SpotsAsync(PlayerId id) =>
        _db.ReadAsync(s => s.Repo<RosterSpot, RosterSpotId>().ListAsync(e => e.PlayerId == id));

    private async Task<ContractTerms> AcceptableTermsAsync(PlayerId player, ClubId buyer)
    {
        var listing = (await Market.GetMarketPlayerAsync(player, buyer))!;
        return new ContractTerms(Today, Today.AddYears(2).AddDays(-1), listing.AskingSalary);
    }

    [Fact]
    public async Task A_player_under_contract_is_bought_for_an_accepted_fee()
    {
        await SetFinancesAsync(Away, 5_000_000, 100_000); // the fee is affordable, the salary is over the wage budget
        var listing = (await Market.GetMarketPlayerAsync(Mario, Away))!;
        Assert.Equal((MarketStatus.UnderContract, _world.HomeClub.Name, RosterRole.Bench), (listing.Status, listing.Club!.Name, listing.Role));
        var asking = listing.Valuation.AskingFee;
        Assert.True(asking > 0);
        Assert.Equal(TransferOutcome.CounterOffer, (await Market.NegotiateFeeAsync(Mario, Away, asking * 9 / 10)).Outcome);
        var terms = await AcceptableTermsAsync(Mario, Away);
        Assert.True((await Market.NegotiateContractAsync(Mario, Away, terms)).IsAccepted);
        var old = listing.Contract!;

        var receipt = await Market.BuyAsync(Mario, Away, asking, terms, RosterRole.Inactive);

        Assert.Equal((TransferKind.Transfer, asking), (receipt.Kind, receipt.Fee));
        Assert.Contains("over its wage budget", Assert.Single(receipt.Warnings), StringComparison.Ordinal); // reported, not enforced, for a managed club
        var ended = await ContractAsync(old.Id);
        Assert.Equal((ContractStatus.Transferred, (DateOnly?)Today, 0L), (ended.Status, ended.TerminationDate, ended.TerminationCompensation));
        var signed = await ContractAsync(receipt.ContractId!.Value);
        Assert.Equal((Away, ContractStatus.Active, terms.AnnualSalary), (signed.ClubId, signed.Status, signed.AnnualSalary));
        Assert.Equal(asking, (await ClubAsync(Home)).Balance);
        Assert.Equal(5_000_000 - asking, (await ClubAsync(Away)).Balance);

        var spells = (await SpellsAsync(Mario)).OrderBy(s => s.StartDate).ToList();
        Assert.Equal((Home, (DateOnly?)Today), (spells[0].ClubId, spells[0].EndDate));
        Assert.Equal((Away, Today, (DateOnly?)null), (spells[1].ClubId, spells[1].StartDate, spells[1].EndDate));
        Assert.Equal((Away, RosterRole.Inactive), ((await SpotsAsync(Mario)).Single().ClubId, (await SpotsAsync(Mario)).Single().Role));
        Assert.Equal(_world.AwayClub.Name, (await Contracts.GetPlayerContractsAsync(Mario))!.CurrentClub);
        Assert.Equal(0, (await Contracts.GetClubContractsAsync(Home))!.Commitments.WageBill);
        Assert.Equal("Transferred", Assert.Single((await Contracts.GetClubContractsAsync(Home))!.History).StatusText);

        var home = (await Market.GetClubTransfersAsync(Home))!;
        var away = (await Market.GetClubTransfersAsync(Away))!;
        Assert.Equal(receipt.TransferId, Assert.Single(home.History).Transfer.Id);
        Assert.Equal((_world.HomeClub.Name, _world.AwayClub.Name, "Transfer"), (away.History[0].FromClub, away.History[0].ToClub, away.History[0].KindText));
        Assert.Single(await Market.GetPlayerTransfersAsync(Mario));
        Assert.Equal(MarketStatus.UnderContract, (await Market.GetMarketPlayerAsync(Mario, Home))!.Status);
        await Assert.ThrowsAsync<DomainValidationException>(() => Market.BuyAsync(Mario, Away, asking, terms)); // already plays for the buyer
    }

    [Fact]
    public async Task A_refused_fee_an_empty_wallet_or_a_full_squad_blocks_the_transfer_and_nothing_is_saved()
    {
        var listing = (await Market.GetMarketPlayerAsync(Mario, Away))!;
        var asking = listing.Valuation.AskingFee;
        var terms = await AcceptableTermsAsync(Mario, Away);

        var refused = await Assert.ThrowsAsync<DomainException>(() => Market.BuyAsync(Mario, Away, asking / 2, terms));
        Assert.Contains("refuses", refused.Message, StringComparison.Ordinal);
        var broke = await Assert.ThrowsAsync<DomainException>(() => Market.BuyAsync(Mario, Away, asking, terms));
        Assert.Contains("cannot pay", broke.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<DomainValidationException>(() => Market.BuyAsync(Mario, Away, asking, terms with { StartDate = Today.AddDays(1) }));

        await SetFinancesAsync(Away, 50_000_000, 50_000_000);
        var cheap = await Assert.ThrowsAsync<DomainException>(() => Market.BuyAsync(Mario, Away, asking, terms with { AnnualSalary = 15_000 }));
        Assert.Contains("refuses the contract", cheap.Message, StringComparison.Ordinal);

        // A full first team cannot register another player.
        for (var i = 0; i < SquadRules.Default.MaxPlayers; i++)
        {
            await ContractedAsync(Away, Position.Center, 50);
        }

        var full = await Assert.ThrowsAsync<DomainException>(() => Market.BuyAsync(Mario, Away, asking, terms));
        Assert.Contains("cannot register", full.Message, StringComparison.Ordinal);
        Assert.Contains("at most", full.Message, StringComparison.Ordinal);

        Assert.Equal(_world.HomeClub.Name, (await Contracts.GetPlayerContractsAsync(Mario))!.CurrentClub);
        Assert.Equal(ContractStatus.Active, (await ContractAsync(listing.Contract!.Id)).Status);
        Assert.Empty(await _db.ReadAsync(s => s.Repo<Transfer, TransferId>().ListAsync()));
        Assert.Equal(50_000_000, (await ClubAsync(Away)).Balance);

        // As youth he is not registered, so the squad limit does not apply.
        var receipt = await Market.BuyAsync(Mario, Away, asking, terms, RosterRole.Youth);
        Assert.Equal(TransferKind.Transfer, receipt.Kind);
    }

    [Fact]
    public async Task A_release_clause_forces_the_sale()
    {
        await SetFinancesAsync(Away, 5_000_000, 5_000_000);
        var contract = (await Market.GetMarketPlayerAsync(Mario, Away))!.Contract!;
        await _db.WriteAsync(s =>
        {
            var tracked = s.Repo<Contract, ContractId>().FindAsync(contract.Id).GetAwaiter().GetResult()!;
            tracked.UpdateTerms(tracked.Terms with { ReleaseClause = 10_000 });
        });
        await Clubs.SaveRosterAsync(Home, [new RosterAssignment(Mario, RosterRole.Starter, Position.SmallForward)]); // starters cost ×1.5

        var listing = (await Market.GetMarketPlayerAsync(Mario, Away))!;
        Assert.Equal(10_000, listing.Valuation.BuyNowFee);
        Assert.True(listing.Valuation.AskingFee > 10_000);
        Assert.Equal(TransferOutcome.Rejected, (await Market.NegotiateFeeAsync(Mario, Away, 9_999)).Outcome);
        var answer = await Market.NegotiateFeeAsync(Mario, Away, 10_000);
        Assert.True(answer.IsAccepted);
        Assert.Contains("release clause", answer.Message, StringComparison.Ordinal);

        var receipt = await Market.BuyAsync(Mario, Away, 10_000, await AcceptableTermsAsync(Mario, Away));
        Assert.Equal((TransferKind.ReleaseClause, 10_000L), (receipt.Kind, receipt.Fee));
        Assert.Equal("Release clause", (await Market.GetPlayerTransfersAsync(Mario)).Single().KindText);
        Assert.Equal(10_000, (await ClubAsync(Home)).Balance);
    }

    [Fact]
    public async Task A_loan_moves_the_player_and_he_comes_back_when_it_ends()
    {
        await SetFinancesAsync(Away, 1_000_000, 1_000_000);
        var listing = (await Market.GetMarketPlayerAsync(Mario, Away))!;
        Assert.True(listing.Valuation.AvailableForLoan);
        var until = new DateOnly(2027, 6, 30);
        var fee = (await Market.NegotiateLoanAsync(Mario, Away, until, 0)).AskingFee;
        Assert.Equal(TransferOutcome.Rejected, (await Market.NegotiateLoanAsync(Mario, Away, until, 0)).Outcome);
        await Assert.ThrowsAsync<DomainValidationException>(() => Market.NegotiateLoanAsync(Mario, Away, new DateOnly(2028, 12, 31), fee)); // past the contract
        var parentId = listing.Contract!.Id;

        var receipt = await Market.LoanAsync(Mario, Away, until, fee);

        Assert.Equal((TransferKind.Loan, fee), (receipt.Kind, receipt.Fee));
        Assert.Equal(ContractStatus.OnLoan, (await ContractAsync(parentId)).Status);
        var loanContract = await ContractAsync(receipt.ContractId!.Value);
        Assert.Equal((Away, Today, until, 300_000L), (loanContract.ClubId, loanContract.StartDate, loanContract.EndDate, loanContract.AnnualSalary));
        Assert.Equal((fee, 1_000_000 - fee), ((await ClubAsync(Home)).Balance, (await ClubAsync(Away)).Balance));
        Assert.Equal(_world.AwayClub.Name, (await Contracts.GetPlayerContractsAsync(Mario))!.CurrentClub);
        Assert.Equal(Away, Assert.Single(await SpotsAsync(Mario)).ClubId);
        Assert.Equal(0, (await Contracts.GetClubContractsAsync(Home))!.Commitments.WageBill); // the loan club pays him
        Assert.Equal(300_000, (await Contracts.GetClubContractsAsync(Away))!.Commitments.WageBill);
        Assert.Equal(0, (await Clubs.GetProfileAsync(Home))!.WageBill);
        Assert.Equal("On loan", Assert.Single((await Contracts.GetClubContractsAsync(Home))!.Current).StatusText);
        Assert.True(Assert.Single((await Clubs.GetRosterAsync(Away))!.Players).HasContract);
        Assert.Equal(MarketStatus.OnLoan, (await Market.GetMarketPlayerAsync(Mario, Home))!.Status);
        var homeTerms = new ContractTerms(Today, Today.AddYears(2).AddDays(-1), 300_000);
        var blocked = await Assert.ThrowsAsync<DomainValidationException>(() => Market.BuyAsync(Mario, Home, 1, homeTerms));
        Assert.Contains("on loan", blocked.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<DomainValidationException>(() => Contracts.TerminateAsync(parentId, TerminationKind.ClubRelease)); // out on loan
        var transfers = (await Market.GetClubTransfersAsync(Home))!;
        Assert.Single(transfers.LoansOut);
        Assert.Empty(transfers.LoansIn);
        Assert.Single((await Market.GetClubTransfersAsync(Away))!.LoansIn);
        Assert.Equal(TransferDateChanges.None, await Market.ProcessTransferDatesAsync());

        // The loan ends: the player is back home, the loan contract is history.
        _clock.Today = new DateOnly(2027, 7, 3);
        var changes = await Market.ProcessTransferDatesAsync();
        Assert.Equal((1, Mario), (changes.LoansReturned, Assert.Single(changes.ReturnedPlayers)));
        Assert.Equal(ContractStatus.Active, (await ContractAsync(parentId)).Status);
        Assert.Equal(ContractStatus.Expired, (await ContractAsync(loanContract.Id)).Status);
        Assert.Equal(_world.HomeClub.Name, (await Contracts.GetPlayerContractsAsync(Mario))!.CurrentClub);
        var spells = (await SpellsAsync(Mario)).OrderBy(s => s.StartDate).ToList();
        Assert.Equal([(Home, (DateOnly?)Today), (Away, until), (Home, null)], spells.Select(s => (s.ClubId, s.EndDate)));
        Assert.Equal(new DateOnly(2027, 7, 1), spells[2].StartDate);
        Assert.Equal((Home, RosterRole.Bench), ((await SpotsAsync(Mario)).Single().ClubId, (await SpotsAsync(Mario)).Single().Role));
        Assert.Equal(300_000, (await Contracts.GetClubContractsAsync(Home))!.Commitments.WageBill);
        var history = await Market.GetPlayerTransfersAsync(Mario);
        Assert.Equal(["Loan return", $"Loan until 2027-06-30 (back 2027-07-01)"], history.Select(l => l.KindText));
        Assert.Empty((await Market.GetClubTransfersAsync(Home))!.LoansOut);
        Assert.Equal(TransferDateChanges.None, await Market.ProcessTransferDatesAsync()); // idempotent
    }

    [Fact]
    public async Task A_starter_is_not_loaned_and_a_recall_brings_a_player_back_early()
    {
        await SetFinancesAsync(Away, 1_000_000, 1_000_000);
        await Clubs.SaveRosterAsync(Home, [new RosterAssignment(Mario, RosterRole.Starter, Position.SmallForward)]);
        var until = new DateOnly(2027, 6, 30);
        var refused = await Assert.ThrowsAsync<DomainException>(() => Market.LoanAsync(Mario, Away, until, 1_000_000));
        Assert.Contains("starting five", refused.Message, StringComparison.Ordinal);

        await Clubs.SaveRosterAsync(Home, [new RosterAssignment(Mario, RosterRole.Inactive)]);
        var fee = (await Market.NegotiateLoanAsync(Mario, Away, until, 0)).AskingFee;
        var loan = await Market.LoanAsync(Mario, Away, until, fee);
        await Assert.ThrowsAsync<DomainValidationException>(() => Market.RecallAsync(loan.TransferId)); // not on the first day

        _clock.Today = Today.AddDays(30);
        var back = await Market.RecallAsync(loan.TransferId);

        Assert.Equal(TransferKind.LoanReturn, back.Kind);
        var loanContract = await ContractAsync(loan.ContractId!.Value);
        Assert.Equal((ContractStatus.Terminated, (DateOnly?)_clock.Today, 0L), (loanContract.Status, loanContract.TerminationDate, loanContract.TerminationCompensation));
        Assert.Equal(_world.HomeClub.Name, (await Contracts.GetPlayerContractsAsync(Mario))!.CurrentClub);
        Assert.Equal(Home, Assert.Single(await SpotsAsync(Mario)).ClubId);
        Assert.Equal(_clock.Today, (await _db.ReadAsync(s => s.Repo<Transfer, TransferId>().FindAsync(loan.TransferId)))!.ReturnedOn);
        await Assert.ThrowsAsync<DomainValidationException>(() => Market.RecallAsync(loan.TransferId));
        Assert.Empty((await Clubs.GetRosterAsync(Away))!.Players);
        Assert.Equal(TransferDateChanges.None, await Market.ProcessTransferDatesAsync());
    }

    [Fact]
    public async Task The_search_filters_and_prices_players_for_the_searching_club()
    {
        await SetFinancesAsync(Away, 1_000_000, 1_000_000, reputation: 90);
        await SetFinancesAsync(Home, 1_000_000, 1_000_000, reputation: 20);
        var centre = await FreeAgentAsync(Position.SmallForward, 70, birthYear: 1990); // the best player, a 36-year-old
        var guard = await FreeAgentAsync(Position.PointGuard, 60, birthYear: 2004);
        await FreeAgentAsync(Position.PowerForward, 40);

        var everyone = await Market.SearchAsync(PlayerSearch.Everyone);
        Assert.Equal(4, everyone.Count);
        Assert.Equal(centre.Id, everyone[0].Player.Id); // best overall first
        Assert.Equal(0, everyone.Single(m => m.Player.Id == centre.Id).Valuation.AskingFee);

        var free = await Market.SearchAsync(new PlayerSearch(Filter: MarketFilter.FreeAgents, MinOverall: 50));
        Assert.Equal([centre.Id, guard.Id], free.Select(m => m.Player.Id));
        Assert.Equal([Mario], (await Market.SearchAsync(new PlayerSearch(Filter: MarketFilter.UnderContract))).Select(m => m.Player.Id));
        Assert.Equal([Mario], (await Market.SearchAsync(new PlayerSearch(Filter: MarketFilter.AvailableForLoan))).Select(m => m.Player.Id));
        Assert.Equal([guard.Id], (await Market.SearchAsync(new PlayerSearch(Position: Position.PointGuard))).Select(m => m.Player.Id));
        Assert.Equal([guard.Id], (await Market.SearchAsync(new PlayerSearch(MaxAge: 23))).Select(m => m.Player.Id));
        Assert.Equal([centre.Id], (await Market.SearchAsync(new PlayerSearch(MinAge: 35))).Select(m => m.Player.Id));
        Assert.Equal([Mario], (await Market.SearchAsync(new PlayerSearch(Name: "mock"))).Select(m => m.Player.Id));
        Assert.DoesNotContain((await Market.SearchAsync(new PlayerSearch(ForClub: Home))).Select(m => m.Player.Id), id => id == Mario); // own players excluded
        Assert.DoesNotContain((await Market.SearchAsync(new PlayerSearch(MaxFee: 1_000))).Select(m => m.Player.Id), id => id == Mario); // fee above the limit
        Assert.Single(await Market.SearchAsync(new PlayerSearch(MaxResults: 1)));

        // A prestigious club is asked for less than a small one.
        var forAway = (await Market.GetMarketPlayerAsync(centre.Id, Away))!.AskingSalary;
        var forHome = (await Market.GetMarketPlayerAsync(centre.Id, Home))!.AskingSalary;
        Assert.True(forAway < forHome);
        Assert.Equal(forAway, (await Contracts.NegotiateAsync(centre.Id, Away, new ContractTerms(Today, Today.AddYears(1), 1))).AskingSalary);
    }

    [Fact]
    public async Task Free_agent_signings_are_part_of_the_transfer_history()
    {
        var player = await FreeAgentAsync();
        var terms = await AcceptableTermsAsync(player.Id, Away);
        var id = await Contracts.SignAsync(player.Id, Away, terms);

        var line = Assert.Single(await Market.GetPlayerTransfersAsync(player.Id));
        Assert.Equal((TransferKind.FreeAgentSigning, (string?)null, _world.AwayClub.Name, 0L, (ContractId?)id), (line.Kind, line.FromClub, line.ToClub, line.Fee, line.Transfer.ContractId));
        Assert.Equal("Free agent", line.KindText);
        Assert.Equal(line.Transfer.Id, Assert.Single((await Market.GetClubTransfersAsync(Away))!.History).Transfer.Id);
    }

    [Fact]
    public async Task AI_clubs_fill_their_rosters_with_free_agents_within_their_budgets()
    {
        await SetFinancesAsync(Away, 500_000, 1_200_000);
        await SetFinancesAsync(Home, 500_000, 2_000_000);
        foreach (var position in Enum.GetValues<Position>())
        {
            for (var i = 0; i < 5; i++)
            {
                await FreeAgentAsync(position, 50 + i);
            }
        }

        var report = await Market.RunAiTransferWindowAsync([Home]);

        var away = Assert.Single(report.Clubs);
        Assert.Equal(_world.AwayClub.Name, away.Club.Name);
        Assert.NotEmpty(away.Needs);
        Assert.True(away.Transactions > 0, string.Join(" | ", away.Actions));
        var roster = (await Clubs.GetRosterAsync(Away))!;
        var contracts = (await Contracts.GetClubContractsAsync(Away))!;
        Assert.Equal(away.Transactions, roster.Players.Count);
        Assert.True(contracts.Commitments.WageBill <= 1_200_000, $"wage bill {contracts.Commitments.WageBill}");
        Assert.True(contracts.Commitments.WageBill > 600_000, $"wage bill {contracts.Commitments.WageBill}"); // the budget is used, not hoarded
        Assert.All(Enum.GetValues<Position>(), p => Assert.True(roster.Players.Count(r => r.Player.Position == p || r.Player.SecondaryPositions.Contains(p)) >= 1, $"no {p}"));
        Assert.Equal(500_000, (await ClubAsync(Away)).Balance); // free agents cost no fee
        Assert.Equal(away.Transactions, (await Market.GetClubTransfersAsync(Away))!.History.Count);

        // The managed club was left alone; the AI never buys its players.
        Assert.Single((await Clubs.GetRosterAsync(Home))!.Players);
        Assert.Equal(_world.HomeClub.Name, (await Contracts.GetPlayerContractsAsync(Mario))!.CurrentClub);

        // The same window again changes nothing more than the still-open needs allow.
        var again = await Market.RunAiTransferWindowAsync([Home]);
        Assert.True(again.Transactions <= Math.Max(0, SquadRules.Default.MatchdayPlayers + 1 - roster.Players.Count));
    }

    [Fact]
    public async Task AI_clubs_sell_their_surplus_to_each_other_and_release_what_nobody_buys()
    {
        // Home has 20 players under contract: two over the registration limit.
        for (var i = 0; i < 19; i++)
        {
            await ContractedAsync(Home, (Position)(i % 5), 45 + (i % 7), salary: 40_000);
        }

        await SetFinancesAsync(Home, 100_000, 5_000_000);
        await SetFinancesAsync(Away, 3_000_000, 3_000_000);

        var report = await Market.RunAiTransferWindowAsync([]);

        var home = report.Clubs.Single(c => c.Club.Id == Home);
        var away = report.Clubs.Single(c => c.Club.Id == Away);
        Assert.Contains(home.Actions, a => a.StartsWith("Listed", StringComparison.Ordinal));
        Assert.Contains(away.Actions, a => a.StartsWith("Bought", StringComparison.Ordinal));
        Assert.True((await Clubs.GetRosterAsync(Home))!.FirstTeam.Count() <= SquadRules.Default.MaxPlayers);
        Assert.True((await ClubAsync(Home)).Balance > 100_000); // fees received
        Assert.Contains(await _db.ReadAsync(s => s.Repo<Transfer, TransferId>().ListAsync()), t => t.Kind == TransferKind.Transfer && t.FromClubId == Home && t.ToClubId == Away);
        var awayRoster = (await Clubs.GetRosterAsync(Away))!;
        Assert.True(awayRoster.Players.Count >= 2);
        Assert.True((await Contracts.GetClubContractsAsync(Away))!.Commitments.WageBill <= 3_000_000);

        // A club with surplus nobody can afford releases it.
        await using var db = await TestDatabase.CreateAsync(_clock);
        var world = await SampleWorld.SeedAsync(db);
        var market = db.Services.GetRequiredService<ITransferService>();
        for (var i = 0; i < 19; i++)
        {
            var player = new Player(PlayerId.New(), "Extra", $"Player{i:00}", new DateOnly(1999, 2, 1), world.Italy.Id, 198, 95, (Position)(i % 5), PlayerAttributes.Uniform(45 + (i % 7)));
            await db.WriteAsync(s =>
            {
                s.Repo<Player, PlayerId>().Add(player);
                s.Repo<Contract, ContractId>().Add(Contract.ForPlayer(ContractId.New(), world.HomeClub.Id, player.Id, SampleWorld.SeasonStart, SampleWorld.SeasonStart.AddYears(2), 40_000));
            });
        }

        var lonely = await market.RunAiTransferWindowAsync([world.AwayClub.Id]); // nobody else is buying
        var only = Assert.Single(lonely.Clubs);
        Assert.Equal(2, only.Actions.Count(a => a.StartsWith("Released", StringComparison.Ordinal)));
        Assert.Equal(SquadRules.Default.MaxPlayers, (await db.Services.GetRequiredService<IClubService>().GetRosterAsync(world.HomeClub.Id))!.FirstTeam.Count());
    }

    [Fact]
    public async Task Startup_processing_returns_loans_before_applying_contract_dates()
    {
        await SetFinancesAsync(Away, 1_000_000, 1_000_000);
        var parent = (await Market.GetMarketPlayerAsync(Mario, Away))!.Contract!;
        var until = parent.EndDate; // loaned out to the very end of his contract
        var fee = (await Market.NegotiateLoanAsync(Mario, Away, until, 0)).AskingFee;
        await Market.LoanAsync(Mario, Away, until, fee);

        _clock.Today = until.AddDays(2);
        var changes = await Market.ProcessTransferDatesAsync();

        Assert.Equal(1, changes.LoansReturned);
        Assert.Equal(2, changes.Contracts.Expired); // his parent contract and the coach's
        Assert.Equal(ContractStatus.Expired, (await ContractAsync(parent.Id)).Status);
        Assert.True((await Contracts.GetPlayerContractsAsync(Mario))!.IsFreeAgent);
        Assert.Null((await Contracts.GetPlayerContractsAsync(Mario))!.CurrentClub);
        Assert.Empty(await SpotsAsync(Mario));
        Assert.All(await SpellsAsync(Mario), s => Assert.NotNull(s.EndDate));
    }
}
