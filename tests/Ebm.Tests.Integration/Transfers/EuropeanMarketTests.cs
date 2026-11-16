using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Transfers;
using Ebm.Services.Contracts;
using Ebm.Services.Transfers;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Transfers;

/// <summary>Step 19: registration deadlines, loan agreements (salary share, option to buy) and contract buyouts.</summary>
public sealed class EuropeanMarketTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private readonly TestClock _clock = new(Today);
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private ITransferService Market => _db.Services.GetRequiredService<ITransferService>();

    private IContractService Contracts => _db.Services.GetRequiredService<IContractService>();

    private ClubId Home => _world.HomeClub.Id;

    private ClubId Away => _world.AwayClub.Id;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(_clock);
        _world = await SampleWorld.SeedAsync(_db);
        await SetBalanceAsync(Home, 2_000_000);
        await SetBalanceAsync(Away, 2_000_000);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task New_players_cannot_be_registered_after_the_deadline_until_the_season_ends()
    {
        await SetChampionshipRulesAsync("""{"pointsForWin":2,"pointsForLoss":0,"registration":{"deadline":"01-31","squad":{"minPlayers":5,"maxPlayers":18}}}""");
        for (var i = 0; i < 4; i++)
        {
            await ContractedAsync(Away);
        }

        _clock.Today = new DateOnly(2027, 2, 10);
        var window = await Market.GetRegistrationWindowAsync(Away);
        Assert.Equal((false, (DateOnly?)new DateOnly(2027, 1, 31), (DateOnly?)_world.Season.EndDate), (window.IsOpen, window.Deadline, window.ReopensOn));

        // Short of players: the fifth may join (an emergency signing), the sixth may not.
        await Contracts.SignAsync((await FreeAgentAsync()).Id, Away, OneYear());
        var refused = await Assert.ThrowsAsync<DomainException>(async () => await Contracts.SignAsync((await FreeAgentAsync()).Id, Away, OneYear()));
        Assert.Contains("deadline of Serie A", refused.Message, StringComparison.Ordinal);
        await Contracts.SignAsync((await FreeAgentAsync()).Id, Away, OneYear(), RosterRole.Youth); // the youth squad is not registered
        await SetBalanceAsync(Away, 100_000_000);
        var price = (await Market.GetMarketPlayerAsync(_world.Player.Id, Away))!.Valuation.BuyNowFee;
        var bought = await Assert.ThrowsAsync<DomainException>(() => Market.BuyAsync(_world.Player.Id, Away, price, new ContractTerms(_clock.Today, _clock.Today.AddYears(2), 400_000)));
        Assert.Contains("deadline", bought.Message, StringComparison.Ordinal);

        // The window opens again on the last day of the season.
        _clock.Today = _world.Season.EndDate;
        Assert.True((await Market.GetRegistrationWindowAsync(Away)).IsOpen);
        await Contracts.SignAsync((await FreeAgentAsync()).Id, Away, OneYear());
    }

    [Fact]
    public async Task A_loan_with_a_salary_share_and_an_option_to_buy_converts_into_a_transfer()
    {
        var player = await ContractedAsync(Home, salary: 80_000);
        var until = Today.AddYears(1).AddDays(-1);
        var listing = (await Market.GetMarketPlayerAsync(player.Id, Away))!;
        var option = listing.Valuation.BuyNowFee;
        var fee = (await Market.NegotiateLoanAsync(player.Id, Away, until, 0)).AskingFee;
        Assert.Contains("at most 25%", (await Market.NegotiateLoanAsync(player.Id, Away, until, fee, 30)).Message, StringComparison.Ordinal); // aged 27
        Assert.Equal(TransferOutcome.Rejected, (await Market.NegotiateLoanAsync(player.Id, Away, until, fee, 25, option - 1)).Outcome);
        Assert.True((await Market.NegotiateLoanAsync(player.Id, Away, until, fee, 25, option)).IsAccepted);

        var loan = await Market.LoanAsync(player.Id, Away, until, fee, parentSalaryShare: 25, optionFee: option);

        var row = await TransferAsync(loan.TransferId);
        Assert.Equal((20_000L, (long?)option), (row.SalaryContribution, row.OptionFee));
        Assert.Equal(2_000_000 + fee - 20_000, (await ClubAsync(Home)).Balance);
        Assert.Equal(2_000_000 - fee + 20_000, (await ClubAsync(Away)).Balance);
        Assert.Contains("contributes", loan.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<DomainValidationException>(() => Market.ExerciseOptionAsync(loan.TransferId, new ContractTerms(Today, Today.AddYears(2), 200_000))); // not on the first day

        _clock.Today = Today.AddDays(73);
        var terms = new ContractTerms(_clock.Today, _clock.Today.AddYears(2).AddDays(-1), 200_000);
        var bought = await Market.ExerciseOptionAsync(loan.TransferId, terms);

        var unused = 20_000L * (until.DayNumber - _clock.Today.DayNumber + 1) / (until.DayNumber - Today.DayNumber + 1);
        Assert.Equal((TransferKind.OptionToBuy, option), (bought.Kind, bought.Fee));
        Assert.Equal(2_000_000 + fee - 20_000 + option + unused, (await ClubAsync(Home)).Balance);
        Assert.Equal(2_000_000 - fee + 20_000 - option - unused, (await ClubAsync(Away)).Balance);
        Assert.Equal((DateOnly?)_clock.Today, (await TransferAsync(loan.TransferId)).ReturnedOn);
        var contracts = await ContractsOfAsync(player.Id);
        Assert.Equal(ContractStatus.Transferred, contracts.Single(c => c.ClubId == Home).Status);
        Assert.Equal(ContractStatus.Terminated, contracts.Single(c => c.Id == loan.ContractId).Status);
        var running = Assert.Single(contracts, c => c.IsRunningOn(_clock.Today));
        Assert.Equal((Away, 200_000L), (running.ClubId, running.AnnualSalary));
        var market = (await Market.GetMarketPlayerAsync(player.Id))!;
        Assert.Equal((MarketStatus.UnderContract, (ClubId?)Away), (market.Status, market.Club?.Id));
        Assert.Empty((await Market.GetClubTransfersAsync(Away))!.LoansIn);
        Assert.Equal(["Option to buy", $"Loan until {until:yyyy-MM-dd}, option € {option:N0} (ended {_clock.Today:yyyy-MM-dd})"], (await Market.GetPlayerTransfersAsync(player.Id)).Select(l => l.KindText));
    }

    [Fact]
    public async Task A_recall_refunds_the_unused_salary_contribution()
    {
        var player = await ContractedAsync(Home, salary: 100_000, birthYear: 2005); // 21: a development loan
        var until = Today.AddYears(1).AddDays(-1);
        var fee = (await Market.NegotiateLoanAsync(player.Id, Away, until, 0)).AskingFee;
        var loan = await Market.LoanAsync(player.Id, Away, until, fee, parentSalaryShare: 50);
        Assert.Equal(50_000, (await TransferAsync(loan.TransferId)).SalaryContribution);

        _clock.Today = Today.AddDays(100);
        var back = await Market.RecallAsync(loan.TransferId);

        var unused = 50_000L * (until.DayNumber - _clock.Today.DayNumber + 1) / (until.DayNumber - Today.DayNumber + 1);
        Assert.Contains($"{unused:N0}", back.Message, StringComparison.Ordinal);
        Assert.Equal(2_000_000 + fee - 50_000 + unused, (await ClubAsync(Home)).Balance);
        Assert.Equal(2_000_000 - fee + 50_000 - unused, (await ClubAsync(Away)).Balance);
    }

    [Fact]
    public async Task A_buyout_needs_the_players_agreement()
    {
        var contract = (await Contracts.GetPlayerContractsAsync(_world.Player.Id))!.Contracts.Single(c => c.Contract.IsRunningOn(Today)).Contract;
        var answer = await Contracts.NegotiateBuyoutAsync(contract.Id, 0);
        Assert.False(answer.IsAccepted);
        Assert.InRange(answer.Minimum, (long)(answer.Remaining * 0.3), (long)(answer.Remaining * 0.9) + 1_000);
        Assert.True((await Contracts.NegotiateBuyoutAsync(contract.Id, answer.Minimum)).IsAccepted);
        Assert.False((await Contracts.NegotiateBuyoutAsync(contract.Id, answer.Remaining + 1)).IsAccepted);

        var refused = await Assert.ThrowsAsync<DomainException>(() => Contracts.TerminateAsync(contract.Id, TerminationKind.MutualAgreement, answer.Minimum - 1_000));
        Assert.Contains("refuses the buyout", refused.Message, StringComparison.Ordinal);
        Assert.Equal(answer.Minimum, await Contracts.TerminateAsync(contract.Id, TerminationKind.MutualAgreement));
        Assert.Equal(MarketStatus.FreeAgent, (await Market.GetMarketPlayerAsync(_world.Player.Id))!.Status);
    }

    private ContractTerms OneYear() => new(_clock.Today, _clock.Today.AddYears(1), 300_000);

    private async Task SetBalanceAsync(ClubId clubId, long balance) =>
        await _db.WriteAsync(s =>
        {
            var club = s.Repo<Club, ClubId>().FindAsync(clubId).GetAwaiter().GetResult()!;
            club.UpdateProfile(club.City, club.Reputation, balance, 5_000_000, club.BoardExpectation);
        });

    private Task SetChampionshipRulesAsync(string json) =>
        _db.WriteAsync(s => s.Repo<Competition, CompetitionId>().FindAsync(_world.Championship.Id).GetAwaiter().GetResult()!.UpdateRules(json));

    private async Task<Player> FreeAgentAsync(int birthYear = 1999)
    {
        var player = new Player(PlayerId.New(), "Free", $"Agent{Guid.NewGuid():N}"[..14], new DateOnly(birthYear, 2, 1), _world.Italy.Id, 198, 95, Position.SmallForward, PlayerAttributes.Uniform(55));
        await _db.WriteAsync(s => s.Repo<Player, PlayerId>().Add(player));
        return player;
    }

    private async Task<Player> ContractedAsync(ClubId clubId, long salary = 80_000, int birthYear = 1999)
    {
        var player = await FreeAgentAsync(birthYear);
        await _db.WriteAsync(s => s.Repo<Contract, ContractId>().Add(Contract.ForPlayer(ContractId.New(), clubId, player.Id, SampleWorld.SeasonStart, SampleWorld.SeasonStart.AddYears(2), salary)));
        return player;
    }

    private Task<Club> ClubAsync(ClubId id) => _db.ReadAsync(async s => (await s.Repo<Club, ClubId>().FindAsync(id))!);

    private Task<Transfer> TransferAsync(TransferId id) => _db.ReadAsync(async s => (await s.Repo<Transfer, TransferId>().FindAsync(id))!);

    private Task<IReadOnlyList<Contract>> ContractsOfAsync(PlayerId id) => _db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync(c => c.PlayerId == id));
}
