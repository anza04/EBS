using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Transfers;
using Ebm.Services.Careers;
using Ebm.Services.Management;
using Ebm.Services.Transfers;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Transfers;

/// <summary>
/// Step 19 acceptance on the real-leagues dataset (data/real-leagues): AI clubs recruit across countries, within their means, and every move
/// leaves the contracts consistent; international clubs attract players for less.
/// </summary>
[Trait("Category", "Slow")] // full datasets or multi-season simulation: run before releases, not on every change
public sealed class EuropeanTransferWindowTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Ai_clubs_recruit_internationally_within_their_means_and_contracts_stay_consistent()
    {
        await RealLeagues.ImportAsync(_db);
        var careers = _db.Services.GetRequiredService<ICareerService>();
        var managed = (await careers.GetClubOptionsAsync()).Single(o => o.Club.Name == "Olimpia Milano").Club;
        await careers.StartAsync(managed.Id, "Europe");
        var market = _db.Services.GetRequiredService<ITransferService>();
        var before = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync())).ToDictionary(c => c.Id, c => c.Balance);

        // As on a window day (CareerService.AdvanceDayAsync): the free-agent pool is replenished first.
        await _db.Services.GetRequiredService<IClubManagementService>().ReplenishFreeAgentsAsync();
        var report = await market.RunAiTransferWindowAsync([managed.Id]);

        var clubs = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync())).ToDictionary(c => c.Id);
        var players = (await _db.ReadAsync(s => s.Repo<Player, PlayerId>().ListAsync())).ToDictionary(p => p.Id);
        var contracts = await _db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync(c => c.PlayerId != null));
        var moves = (await _db.ReadAsync(s => s.Repo<Transfer, TransferId>().ListAsync(t => t.Date == report.Date))).ToList();
        var today = report.Date;

        // Activity, across borders: bought from another country's club, and free agents of other nationalities.
        var bought = moves.Where(t => t.Kind is TransferKind.Transfer or TransferKind.ReleaseClause).ToList();
        Assert.True(report.Transactions >= 20, $"{report.Transactions} deals"); // 138 clubs
        var actions = report.Clubs.SelectMany(c => c.Actions).ToList();
        var signings = moves.Where(t => t.Kind == TransferKind.FreeAgentSigning).ToList();
        var summary = $"bought={bought.Count} free agents={signings.Count} upgrades={actions.Count(a => a.Contains("upgrade", StringComparison.Ordinal))} " +
            $"failed={string.Join(" | ", actions.Where(a => a.StartsWith("Could not", StringComparison.Ordinal)).Take(10))}";
        Assert.True(bought.Any(t => clubs[t.FromClubId!.Value].CountryId != clubs[t.ToClubId].CountryId), $"No cross-border purchase: {summary}");
        Assert.True(signings.Any(t => players[t.PlayerId].NationalityId != clubs[t.ToClubId].CountryId), $"No foreign free agent signed: {summary}");
        Assert.True(actions.Any(a => a.Contains("upgrade", StringComparison.Ordinal)), $"No upgrade signing: {summary}");
        Assert.DoesNotContain(moves, t => t.ToClubId == managed.Id || t.FromClubId == managed.Id); // the user's club is left alone

        // Economics: nobody spends money it does not have; fees are paid at market prices.
        Assert.All(clubs.Values, c => Assert.True(c.Balance >= 0 || before[c.Id] < 0, $"{c.Name}: {c.Balance:N0}"));
        // Fees received in the window (sales) may fund later purchases.
        var received = bought.GroupBy(t => t.FromClubId!.Value).ToDictionary(g => g.Key, g => g.Sum(t => t.Fee));
        Assert.All(bought.GroupBy(t => t.ToClubId), g => Assert.True(
            g.Sum(t => t.Fee) <= before[g.Key] + received.GetValueOrDefault(g.Key),
            $"{clubs[g.Key].Name} paid {g.Sum(t => t.Fee):N0} from {before[g.Key]:N0} + {received.GetValueOrDefault(g.Key):N0} sales"));
        Assert.All(bought, t => Assert.InRange(t.Fee, TransferNegotiator.MinimumValue, 20_000_000));

        // Contracts: one running contract per player (two only while on loan), each move backed by its contract.
        Assert.All(contracts.Where(c => c.IsRunningOn(today)).GroupBy(c => c.PlayerId!.Value), g => Assert.True(g.Count(c => c.Status != ContractStatus.OnLoan) == 1, players[g.Key].FullName));
        var byId = contracts.ToDictionary(c => c.Id);
        Assert.All(moves.Where(t => t.ContractId is not null), t => Assert.Equal((t.ToClubId, t.PlayerId), (byId[t.ContractId!.Value].ClubId, byId[t.ContractId.Value].PlayerId!.Value)));
        Assert.All(bought, t => Assert.Single(contracts, c => c.PlayerId == t.PlayerId && c.Status == ContractStatus.Transferred && c.TerminationDate == today && c.ClubId == t.FromClubId));
    }

    [Fact]
    public async Task Clubs_in_international_competitions_attract_players_for_less()
    {
        await RealLeagues.ImportAsync(_db);
        var careers = _db.Services.GetRequiredService<ICareerService>();
        var managed = (await careers.GetClubOptionsAsync()).Single(o => o.Club.Name == "Olimpia Milano").Club;
        await careers.StartAsync(managed.Id, "Europe"); // qualifies the international competitions
        var market = _db.Services.GetRequiredService<ITransferService>();
        // Price one of another club's players for Milano.
        var agent = (await market.SearchAsync(new PlayerSearch(Filter: MarketFilter.UnderContract, ForClub: managed.Id, MaxResults: 1))).Single();

        var priced = (await market.GetMarketPlayerAsync(agent.Player.Id, managed.Id))!;

        var context = new NegotiationContext(agent.Overall, agent.Evaluation.PotentialRating, agent.Player.Reputation, agent.Age, ClubAttractiveness.Of(managed.Reputation, 1), agent.Contract?.AnnualSalary);
        Assert.Equal(ContractNegotiator.Default.AskingSalary(context), priced.AskingSalary); // Milano plays the EuroLeague
        Assert.True(priced.AskingSalary < ContractNegotiator.Default.AskingSalary(context with { ClubReputation = managed.Reputation }));
    }
}
