using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Persistence;
using Ebm.Services.Clubs;
using Ebm.Services.Contracts;
using Ebm.Services.Editing;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Contracts;

public sealed class ContractServiceTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private readonly TestClock _clock = new(Today);
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private IContractService Contracts => _db.Services.GetRequiredService<IContractService>();

    private IClubService Clubs => _db.Services.GetRequiredService<IClubService>();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(_clock);
        _world = await SampleWorld.SeedAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Player> FreeAgentAsync(int rating = 60, string name = "Free")
    {
        var player = new Player(PlayerId.New(), name, $"Agent{Guid.NewGuid():N}"[..20], new DateOnly(1998, 2, 1), _world.Italy.Id, 198, 95, Position.SmallForward, PlayerAttributes.Uniform(rating));
        await _db.WriteAsync(s => s.Repo<Player, PlayerId>().Add(player));
        return player;
    }

    /// <summary>Terms the player accepts: his asking salary, from <paramref name="start"/> for two years.</summary>
    private async Task<ContractTerms> AcceptableTermsAsync(PlayerId player, ClubId club, DateOnly start, long signingBonus = 0)
    {
        var offer = new ContractTerms(start, start.AddYears(2).AddDays(-1), 1, SigningBonus: signingBonus);
        var asking = (await Contracts.NegotiateAsync(player, club, offer)).AskingSalary;
        return offer with { AnnualSalary = asking };
    }

    private Task<Club> ClubAsync(ClubId id) => _db.ReadAsync(async s => (await s.Repo<Club, ClubId>().FindAsync(id))!);

    private Task<Contract> ContractAsync(ContractId id) => _db.ReadAsync(async s => (await s.Repo<Contract, ContractId>().FindAsync(id))!);

    private Task<IReadOnlyList<PlayerClubHistoryEntry>> SpellsAsync(PlayerId id) =>
        _db.ReadAsync(s => s.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>().ListAsync(e => e.PlayerId == id));

    private Task<IReadOnlyList<RosterSpot>> SpotsAsync(PlayerId id) =>
        _db.ReadAsync(s => s.Repo<RosterSpot, RosterSpotId>().ListAsync(e => e.PlayerId == id));

    private async Task<bool> IsFreeAgentAsync(PlayerId id) => (await Contracts.GetFreeAgentsAsync()).Any(f => f.Player.Id == id);

    [Fact]
    public async Task A_free_agent_signs_joins_the_squad_and_the_club_pays_the_signing_bonus()
    {
        var player = await FreeAgentAsync();
        Assert.True(await IsFreeAgentAsync(player.Id));
        var terms = await AcceptableTermsAsync(player.Id, _world.AwayClub.Id, Today, signingBonus: 20_000);

        var id = await Contracts.SignAsync(player.Id, _world.AwayClub.Id, terms, RosterRole.Inactive);

        var contract = await ContractAsync(id);
        Assert.Equal((ContractStatus.Active, _world.AwayClub.Id, terms.AnnualSalary), (contract.Status, contract.ClubId, contract.AnnualSalary));
        Assert.Equal(-20_000, (await ClubAsync(_world.AwayClub.Id)).Balance);
        Assert.False(await IsFreeAgentAsync(player.Id));
        Assert.Equal(_world.AwayClub.Id, Assert.Single(await SpellsAsync(player.Id)).ClubId);
        Assert.Equal(RosterRole.Inactive, Assert.Single(await SpotsAsync(player.Id)).Role);
        var squad = (await Clubs.GetRosterAsync(_world.AwayClub.Id))!;
        Assert.True(Assert.Single(squad.Players).HasContract);

        var club = (await Contracts.GetClubContractsAsync(_world.AwayClub.Id))!;
        Assert.Equal(id, Assert.Single(club.Current).Contract.Id);
        Assert.Equal(terms.AnnualSalary, club.Commitments.WageBill);
        Assert.Equal(-terms.AnnualSalary, club.WageBudgetLeft);
        var profile = (await Contracts.GetPlayerContractsAsync(player.Id))!;
        Assert.False(profile.IsFreeAgent);
        Assert.Equal(_world.AwayClub.Name, profile.CurrentClub);
        Assert.Equal("Running", profile.Running!.StatusText);
    }

    [Fact]
    public async Task A_player_refuses_a_low_offer_and_nothing_is_saved()
    {
        var player = await FreeAgentAsync();
        var terms = await AcceptableTermsAsync(player.Id, _world.AwayClub.Id, Today);
        var low = terms with { AnnualSalary = terms.AnnualSalary / 2 };

        Assert.Equal(NegotiationOutcome.Rejected, (await Contracts.NegotiateAsync(player.Id, _world.AwayClub.Id, low)).Outcome);
        var counter = await Contracts.NegotiateAsync(player.Id, _world.AwayClub.Id, terms with { AnnualSalary = terms.AnnualSalary * 9 / 10 });
        Assert.Equal(NegotiationOutcome.CounterOffer, counter.Outcome);

        await Assert.ThrowsAsync<DomainException>(() => Contracts.SignAsync(player.Id, _world.AwayClub.Id, low));
        Assert.True(await IsFreeAgentAsync(player.Id));
        await Contracts.SignAsync(player.Id, _world.AwayClub.Id, counter.CounterOffer!);
        Assert.False(await IsFreeAgentAsync(player.Id));
    }

    [Fact]
    public async Task A_contracted_player_can_only_agree_a_contract_that_starts_after_his_current_one()
    {
        var mario = _world.Player.Id; // under contract with the home club until 2028-09-01
        var during = new ContractTerms(Today, Today.AddYears(1), 5_000_000);
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => Contracts.SignAsync(mario, _world.AwayClub.Id, during));
        Assert.Contains(_world.HomeClub.Name, ex.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<DomainValidationException>(() => Contracts.SignAsync(mario, _world.HomeClub.Id, during)); // renew instead

        var after = await AcceptableTermsAsync(mario, _world.AwayClub.Id, new DateOnly(2028, 9, 2));
        var id = await Contracts.SignAsync(mario, _world.AwayClub.Id, after);
        var profile = (await Contracts.GetPlayerContractsAsync(mario))!;
        Assert.Equal(_world.HomeClub.Name, profile.CurrentClub);
        Assert.Equal(id, profile.Upcoming!.Contract.Id);
        Assert.Equal(ContractDateChanges.None, await Contracts.ProcessContractDatesAsync());

        // Two years later the old contract has run out and the new one has started.
        _clock.Today = new DateOnly(2028, 9, 5);
        var changes = await Contracts.ProcessContractDatesAsync();
        Assert.Equal((2, 1), (changes.Expired, changes.Started)); // Mario's and the coach's contracts expired; Mario's new one started
        Assert.Empty(changes.NewFreeAgents);
        var spells = (await SpellsAsync(mario)).OrderBy(s => s.StartDate).ToList();
        Assert.Equal((_world.HomeClub.Id, new DateOnly(2028, 9, 1)), (spells[0].ClubId, spells[0].EndDate));
        Assert.Equal((_world.AwayClub.Id, new DateOnly(2028, 9, 2), (DateOnly?)null), (spells[1].ClubId, spells[1].StartDate, spells[1].EndDate));
        Assert.Equal(_world.AwayClub.Id, Assert.Single(await SpotsAsync(mario)).ClubId);
        Assert.Equal(ContractDateChanges.None, await Contracts.ProcessContractDatesAsync()); // idempotent
    }

    [Fact]
    public async Task An_expired_contract_frees_the_player_and_stays_in_the_history()
    {
        var mario = _world.Player.Id;
        await Clubs.SaveRosterAsync(_world.HomeClub.Id, [new RosterAssignment(mario, RosterRole.Rotation)]);

        _clock.Today = new DateOnly(2028, 9, 2);
        var changes = await Contracts.ProcessContractDatesAsync();

        Assert.Equal(mario, Assert.Single(changes.NewFreeAgents));
        Assert.True(await IsFreeAgentAsync(mario));
        Assert.Equal(new DateOnly(2028, 9, 1), Assert.Single(await SpellsAsync(mario)).EndDate);
        Assert.Empty(await SpotsAsync(mario));
        Assert.Empty((await Clubs.GetRosterAsync(_world.HomeClub.Id))!.Players);
        var club = (await Contracts.GetClubContractsAsync(_world.HomeClub.Id))!;
        Assert.Empty(club.Current);
        Assert.Equal(ContractStatus.Expired, Assert.Single(club.History).Contract.Status);
        Assert.Equal(ContractStatus.Expired, Assert.Single((await Contracts.GetPlayerContractsAsync(mario))!.Contracts).Status);

        // Staff contracts expire too (status only).
        Assert.All(await _db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync()), c => Assert.Equal(ContractStatus.Expired, c.Status));
    }

    [Fact]
    public async Task Releasing_a_player_pays_his_remaining_salary_and_makes_him_a_free_agent()
    {
        var mario = _world.Player.Id;
        var contract = (await Contracts.GetPlayerContractsAsync(mario))!.Running!;

        var paid = await Contracts.TerminateAsync(contract.Contract.Id, TerminationKind.ClubRelease);

        Assert.Equal(contract.RemainingSalary, paid);
        Assert.Equal(SalaryCalculator.RemainingSalary(contract.Contract, Today), paid);
        Assert.Equal(-paid, (await ClubAsync(_world.HomeClub.Id)).Balance);
        var stored = await ContractAsync(contract.Contract.Id);
        Assert.Equal((ContractStatus.Terminated, Today, paid), (stored.Status, stored.TerminationDate, stored.TerminationCompensation));
        Assert.True(await IsFreeAgentAsync(mario));
        Assert.Equal(Today, Assert.Single(await SpellsAsync(mario)).EndDate);
        Assert.Null((await Contracts.GetPlayerContractsAsync(mario))!.CurrentClub);
        Assert.Equal(0, (await Contracts.GetClubContractsAsync(_world.HomeClub.Id))!.Commitments.WageBill);
        await Assert.ThrowsAsync<DomainValidationException>(() => Contracts.TerminateAsync(contract.Contract.Id, TerminationKind.ClubRelease));

        // The released player can sign elsewhere at once.
        await Contracts.SignAsync(mario, _world.AwayClub.Id, await AcceptableTermsAsync(mario, _world.AwayClub.Id, Today));
        Assert.Equal(_world.AwayClub.Name, (await Contracts.GetPlayerContractsAsync(mario))!.CurrentClub);
    }

    [Fact]
    public async Task A_mutual_agreement_pays_the_agreed_settlement_within_the_remaining_salary()
    {
        var contract = (await Contracts.GetPlayerContractsAsync(_world.Player.Id))!.Running!;
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Contracts.TerminateAsync(contract.Contract.Id, TerminationKind.MutualAgreement, contract.RemainingSalary + 1));

        Assert.Equal(100_000, await Contracts.TerminateAsync(contract.Contract.Id, TerminationKind.MutualAgreement, 100_000));
        Assert.Equal(-100_000, (await ClubAsync(_world.HomeClub.Id)).Balance);
    }

    [Fact]
    public async Task A_renewal_replaces_the_contract_and_keeps_the_old_one_as_history()
    {
        var mario = _world.Player.Id;
        var old = (await Contracts.GetPlayerContractsAsync(mario))!.Running!.Contract;
        var start = new DateOnly(2027, 7, 1);
        var cut = new ContractTerms(start, new DateOnly(2030, 6, 30), old.AnnualSalary / 2);
        Assert.Equal(NegotiationOutcome.Rejected, (await Contracts.NegotiateRenewalAsync(old.Id, cut)).Outcome); // at most a 10% pay cut
        await Assert.ThrowsAsync<DomainException>(() => Contracts.RenewAsync(old.Id, cut));
        await Assert.ThrowsAsync<DomainValidationException>(() => Contracts.RenewAsync(old.Id, cut with { StartDate = new DateOnly(2028, 9, 3) })); // gap

        var renewal = cut with { AnnualSalary = old.AnnualSalary + 50_000, SigningBonus = 10_000 };
        var id = await Contracts.RenewAsync(old.Id, renewal);

        var stored = await ContractAsync(old.Id);
        Assert.Equal((ContractStatus.Renewed, (DateOnly?)start), (stored.Status, stored.TerminationDate));
        var profile = (await Contracts.GetPlayerContractsAsync(mario))!;
        Assert.Equal(old.Id, profile.Running!.Contract.Id); // still runs until the renewal starts
        Assert.Equal(id, profile.Upcoming!.Contract.Id);
        Assert.Equal(2, profile.Contracts.Count);
        Assert.Equal(-10_000, (await ClubAsync(_world.HomeClub.Id)).Balance);
        await Assert.ThrowsAsync<DomainValidationException>(() => Contracts.RenewAsync(old.Id, renewal));

        _clock.Today = start;
        Assert.Equal(ContractDateChanges.None, await Contracts.ProcessContractDatesAsync()); // same club: no move, nothing expires
        var club = (await Contracts.GetClubContractsAsync(_world.HomeClub.Id))!;
        Assert.Equal(id, Assert.Single(club.Current).Contract.Id);
        Assert.Equal(old.Id, Assert.Single(club.History).Contract.Id);
        Assert.Equal(_world.HomeClub.Name, (await Contracts.GetPlayerContractsAsync(mario))!.CurrentClub);
    }

    [Fact]
    public async Task Options_extend_the_contract_or_lapse()
    {
        var player = await FreeAgentAsync();
        var terms = await AcceptableTermsAsync(player.Id, _world.AwayClub.Id, Today);
        var withOptions = terms with
        {
            AnnualSalary = terms.AnnualSalary * 2, // generous enough for the club option's discount
            ClubOptionUntil = terms.EndDate.AddYears(1),
            PlayerOptionUntil = terms.EndDate.AddYears(2),
        };
        var id = await Contracts.SignAsync(player.Id, _world.AwayClub.Id, withOptions);

        await Contracts.DecideClubOptionAsync(id, exercise: true);
        var extended = await ContractAsync(id);
        Assert.Equal((terms.EndDate.AddYears(1), (DateOnly?)null), (extended.EndDate, extended.ClubOptionUntil));
        await Assert.ThrowsAsync<DomainValidationException>(() => Contracts.DecideClubOptionAsync(id, exercise: false));

        // Paid twice his market value, the player takes up his own option as well.
        Assert.True(await Contracts.DecidePlayerOptionAsync(id));
        Assert.Equal(terms.EndDate.AddYears(2), (await ContractAsync(id)).EndDate);
    }

    [Fact]
    public async Task A_player_paid_below_his_value_declines_his_option()
    {
        var player = await FreeAgentAsync(rating: 80);
        var terms = await AcceptableTermsAsync(player.Id, _world.AwayClub.Id, Today);
        var cheap = await _db.ReadAsync(async s =>
        {
            // Stored through the editor: a contract the player would not sign today.
            var editor = s.Services.GetRequiredService<IDatabaseEditor>();
            return await editor.SaveContractAsync(new ContractDraft(null, _world.AwayClub.Id, player.Id, Today, terms.EndDate, 50_000, PlayerOptionUntil: terms.EndDate.AddYears(1)));
        });

        Assert.False(await Contracts.DecidePlayerOptionAsync(cheap));
        var stored = await ContractAsync(cheap);
        Assert.Equal((terms.EndDate, (DateOnly?)null), (stored.EndDate, stored.PlayerOptionUntil));
    }

    [Fact]
    public async Task The_editor_and_the_database_keep_every_contract_term()
    {
        var player = await FreeAgentAsync();
        var bonuses = PerformanceBonuses.Of((BonusCondition.ReachPlayoffs, 15_000), (BonusCondition.PerGamePlayed, 250));
        var id = await _db.Services.GetRequiredService<IDatabaseEditor>().SaveContractAsync(new ContractDraft(
            null, _world.AwayClub.Id, player.Id, Today, Today.AddYears(3), 180_000,
            PaymentFrequency.Quarterly, 25_000, bonuses, 2_000_000, Today.AddYears(4), null));

        var stored = await ContractAsync(id);
        Assert.Equal((PaymentFrequency.Quarterly, 25_000L, bonuses, (long?)2_000_000), (stored.PaymentFrequency, stored.SigningBonus, stored.PerformanceBonuses, stored.ReleaseClause));
        Assert.Equal((Today.AddYears(4), (DateOnly?)null, ContractStatus.Active), (stored.ClubOptionUntil, stored.PlayerOptionUntil, stored.Status));
        Assert.Contains("PerGamePlayed:250;ReachPlayoffs:15000", await _db.QueryStringsAsync("SELECT PerformanceBonuses FROM Contracts"));
    }

    [Fact]
    public async Task Step_06_contracts_upgrade_as_active_contracts_with_default_terms()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.ReadAsync(async s =>
        {
            await s.Services.GetRequiredService<EbmDbContext>().Database.MigrateAsync("20261109171322_ClubsAndRosters");
            return 0;
        });
        await db.ExecuteSqlAsync("""
            INSERT INTO Countries (Id, Code, Name) VALUES ('33333333-3333-3333-3333-333333333333', 'ITA', 'Italy');
            INSERT INTO Clubs (Id, Name, ShortName, CountryId) VALUES ('44444444-4444-4444-4444-444444444444', 'Old Roma', 'ROM', '33333333-3333-3333-3333-333333333333');
            INSERT INTO Staff (Id, FirstName, LastName, DateOfBirth, NationalityId, Role)
            VALUES ('55555555-5555-5555-5555-555555555555', 'Old', 'Coach', '1970-01-01', '33333333-3333-3333-3333-333333333333', 'HeadCoach');
            INSERT INTO Contracts (Id, ClubId, StaffMemberId, StartDate, EndDate, AnnualSalary)
            VALUES ('66666666-6666-6666-6666-666666666666', '44444444-4444-4444-4444-444444444444', '55555555-5555-5555-5555-555555555555', '2025-07-01', '2027-06-30', 90000);
            """);

        Assert.EndsWith("_CountryEuEea", await db.Initializer.InitializeAsync(), StringComparison.Ordinal);
        var c = Assert.Single(await db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync()));
        Assert.Equal((ContractStatus.Active, PaymentFrequency.Monthly, 0L, true), (c.Status, c.PaymentFrequency, c.SigningBonus, c.PerformanceBonuses.IsEmpty));
        Assert.Equal(((long?)null, (DateOnly?)null, (DateOnly?)null, 0L), (c.ReleaseClause, c.ClubOptionUntil, c.TerminationDate, c.TerminationCompensation));
    }
}
