using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Staff;
using Ebm.Domain.Careers;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Finances;
using Ebm.Domain.Staff;
using Ebm.Services.Clubs;
using Ebm.Services.Development;
using Ebm.Services.Scouting;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Clubs;

/// <summary>Step 28: the staff market, hiring, dismissal, AI staff recruitment and the staff's influence (docs/STAFF.md).</summary>
public sealed class StaffServiceTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private readonly TestClock _clock = new(Today);
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private IStaffService Staff => _db.Services.GetRequiredService<IStaffService>();

    private ClubId Home => _world.HomeClub.Id;

    private ClubId Away => _world.AwayClub.Id;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(_clock);
        _world = await SampleWorld.SeedAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task The_market_is_supplied_and_staff_can_be_hired_and_dismissed()
    {
        var clubs = await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync());
        var size = IStaffService.MarketSizeFor(clubs.Count);
        Assert.Equal(size * Enum.GetValues<StaffRole>().Length, await Staff.SupplyMarketAsync());
        Assert.Equal(0, await Staff.SupplyMarketAsync());

        await SetReputationAsync(Home, 100);
        var market = await Staff.GetMarketAsync(Home, StaffRole.Physio);
        Assert.Equal(size, market.Count);
        Assert.All(market, c => Assert.True(c.WillJoin));
        Assert.Equal(market.Max(c => c.Member.Ability), market[0].Member.Ability);
        var pick = market[0];

        var low = await Assert.ThrowsAsync<DomainException>(() => Staff.HireAsync(Home, pick.Member.Id, pick.Demand - 1_000, 2));
        Assert.Contains("asks for at least", low.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<DomainValidationException>(() => Staff.HireAsync(Home, pick.Member.Id, pick.Demand, StaffRules.MaxContractYears + 1));

        var contract = await Staff.HireAsync(Home, pick.Member.Id, pick.Demand, 2);
        Assert.Equal((Today, Today.AddYears(2).AddDays(-1), pick.Demand), (contract.StartDate, contract.EndDate, contract.AnnualSalary));
        await Assert.ThrowsAsync<DomainException>(() => Staff.HireAsync(Away, pick.Member.Id, pick.Demand * 2, 2)); // no longer on the market
        Assert.DoesNotContain(await Staff.GetMarketAsync(Home, StaffRole.Physio), c => c.Member.Id == pick.Member.Id);

        var staff = (await Staff.GetClubStaffAsync(Home))!;
        Assert.Contains(staff.Members, l => l.Member.Id == pick.Member.Id);
        Assert.Equal(pick.Member.Ability, staff.Duties.Single(d => d.Role == StaffRole.Physio).Rating);
        Assert.Equal(staff.Members.Sum(l => l.Contract.AnnualSalary), staff.AnnualWages);

        // The sample's head coach is the only one allowed.
        var head = (await Staff.GetMarketAsync(Home, StaffRole.HeadCoach))[0];
        var full = await Assert.ThrowsAsync<DomainException>(() => Staff.HireAsync(Home, head.Member.Id, head.Demand, 1));
        Assert.Contains("the most allowed", full.Message, StringComparison.Ordinal);

        // Dismissal: the rest of the contract is paid as a settlement and he returns to the market.
        var balance = _world.HomeClub.Balance;
        _clock.Today = Today.AddDays(30);
        var paid = await Staff.DismissAsync(contract.Id);
        Assert.Equal(SalaryCalculator.RemainingSalary(contract, Today.AddDays(30)), paid);
        Assert.True(paid > 0);
        var club = await _db.ReadAsync(s => s.Repo<Club, ClubId>().FindAsync(Home));
        Assert.Equal(balance - paid, club!.Balance);
        var booked = await _db.ReadAsync(s => s.Repo<FinancialTransaction, FinancialTransactionId>().ListAsync(t => t.ClubId == Home && t.Category == FinanceCategory.Settlements));
        Assert.Equal(-paid, booked.Single().Amount);
        Assert.Contains(await Staff.GetMarketAsync(Home, StaffRole.Physio), c => c.Member.Id == pick.Member.Id);
        Assert.DoesNotContain((await Staff.GetClubStaffAsync(Home))!.Members, l => l.Member.Id == pick.Member.Id);
        await Assert.ThrowsAsync<DomainValidationException>(() => Staff.DismissAsync(contract.Id));
    }

    [Fact]
    public async Task Staff_refuse_clubs_far_below_their_reputation()
    {
        await Staff.SupplyMarketAsync();
        await SetReputationAsync(Home, 10);
        var star = (await Staff.GetMarketAsync(Home, StaffRole.Scout)).First(c => c.Member.Reputation > 10 + StaffRules.ReputationGap);
        Assert.False(star.WillJoin);
        var refused = await Assert.ThrowsAsync<DomainException>(() => Staff.HireAsync(Home, star.Member.Id, star.Demand * 3, 3));
        Assert.Contains("will not join", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ai_clubs_recruit_from_the_market_and_replace_failing_head_coaches()
    {
        await Staff.SupplyMarketAsync();
        var freeHeads = (await Staff.GetMarketAsync(Away, StaffRole.HeadCoach)).Select(c => c.Member.Id).ToHashSet();
        await _db.Services.GetRequiredService<IDevelopmentService>().HireMissingCoachesAsync();

        var away = (await Staff.GetClubStaffAsync(Away))!;
        var head = Assert.Single(away.Members, l => l.Member.Role == StaffRole.HeadCoach);
        Assert.Contains(head.Member.Id, freeHeads); // hired from the market, not generated
        Assert.Equal(StaffRules.SalaryDemand(StaffRole.HeadCoach, head.Member.Profile), head.Contract.AnnualSalary);
        Assert.Equal(StaffRules.Wanted(StaffRole.AssistantCoach, _world.AwayClub.Reputation), away.Members.Count(l => l.Member.Role == StaffRole.AssistantCoach));
        Assert.Equal(1, away.Members.Count(l => l.Member.Role == StaffRole.FitnessCoach));
        Assert.Equal(_world.Coach.Id, Assert.Single((await Staff.GetClubStaffAsync(Home))!.Members, l => l.Member.Role == StaffRole.HeadCoach).Member.Id);

        // Both missed their objectives; only the AI club sacks its coach (the user decides for his own club).
        Assert.Equal(1, await Staff.ReviewHeadCoachesAsync(new Dictionary<ClubId, bool> { [Home] = false, [Away] = false }, [Home]));
        Assert.DoesNotContain((await Staff.GetClubStaffAsync(Away))!.Members, l => l.Member.Role == StaffRole.HeadCoach);
        Assert.Contains((await Staff.GetClubStaffAsync(Home))!.Members, l => l.Member.Role == StaffRole.HeadCoach);
        await _db.Services.GetRequiredService<IDevelopmentService>().HireMissingCoachesAsync();
        Assert.NotEqual(head.Member.Id, Assert.Single((await Staff.GetClubStaffAsync(Away))!.Members, l => l.Member.Role == StaffRole.HeadCoach).Member.Id);
    }

    [Fact]
    public async Task The_managed_club_is_staffed_by_the_user_after_its_first_staff()
    {
        await _db.WriteAsync(s => s.Repo<Career, CareerId>().Add(new Career(CareerId.New(), "Manager", Home, _world.Season.Id, Today)));
        var scouting = _db.Services.GetRequiredService<IScoutingService>();
        Assert.True(await scouting.HireMissingScoutsAsync() > 0);
        Assert.Empty(await scouting.GetScoutsAsync(Home)); // Home already has staff (its head coach): the user hires
        Assert.NotEmpty(await scouting.GetScoutsAsync(Away));
    }

    [Fact]
    public async Task Development_coaches_raise_the_coaching_ability_of_training()
    {
        var development = _db.Services.GetRequiredService<IDevelopmentService>();
        var before = (await development.GetClubTrainingAsync(Home))!.CoachAbility;
        Assert.Equal(StaffMember.DefaultAbility, before); // the sample's head coach alone

        var expert = new StaffMember(StaffMemberId.New(), "Dev", "Expert", new DateOnly(1970, 1, 1), _world.Italy.Id, StaffRole.DevelopmentCoach, StaffProfile.Uniform(30) with { Development = 90 });
        await _db.WriteAsync(s => s.Repo<StaffMember, StaffMemberId>().Add(expert));
        await Staff.HireAsync(Home, expert.Id, StaffRules.SalaryDemand(StaffRole.DevelopmentCoach, expert.Profile), 1);

        Assert.Equal(70, (await development.GetClubTrainingAsync(Home))!.CoachAbility); // (50 + 90) / 2
        Assert.Equal(70, (await Staff.GetClubStaffAsync(Home))!.Duties.Single(d => d.Duty.StartsWith("Training", StringComparison.Ordinal)).Rating);
    }

    [Fact]
    public async Task The_staff_screen_hires_and_dismisses()
    {
        await Staff.SupplyMarketAsync();
        await SetReputationAsync(Home, 100);
        var vm = new StaffViewModel(Staff, new EditorLookups(), () => Task.CompletedTask) { ClubId = Home };
        await vm.RefreshAsync();
        Assert.Equal(_world.Coach.FullName, Assert.Single(vm.Members).Name);
        Assert.Equal(6, vm.Duties.Count);
        Assert.Equal("(no physio)", vm.Duties.Single(d => d.Name == "Medical care").Lead);

        vm.MarketRole = StaffRole.Physio;
        await vm.RefreshAsync();
        Assert.All(vm.Market, c => Assert.Equal("Physio", c.Role));
        vm.SelectedCandidate = vm.Market[0];
        Assert.Equal(vm.Market[0].Candidate.Demand.ToString(System.Globalization.CultureInfo.CurrentCulture), vm.OfferSalary);
        await vm.HireCommand.ExecuteAsync(null);
        Assert.False(vm.IsError, vm.Message);
        await vm.RefreshAsync();
        Assert.Equal(2, vm.Members.Count);

        vm.SelectedMember = vm.Members.Single(m => m.Role == "Physio");
        await vm.DismissCommand.ExecuteAsync(null);
        Assert.False(vm.IsError, vm.Message);
        Assert.Contains("dismissed", vm.Message, StringComparison.Ordinal);
        await vm.RefreshAsync();
        Assert.Single(vm.Members);
    }

    [Fact]
    public async Task The_staff_screen_renders_with_the_application_themes()
    {
        await Staff.SupplyMarketAsync();
        await _db.Services.GetRequiredService<IDevelopmentService>().HireMissingCoachesAsync();
        var vm = new StaffViewModel(Staff, new EditorLookups(), () => Task.CompletedTask) { ClubId = Away };
        await vm.RefreshAsync();
        Assert.NotEmpty(vm.Members);
        Assert.NotEmpty(vm.Market);

        var size = Players.PlayerScreensRenderTests.RunOnSta(() =>
        {
            var resources = (System.Windows.Application.Current ?? new System.Windows.Application()).Resources;
            foreach (var name in new[] { "Shell", "Editor", "Staff" })
            {
                resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/Ebm.Desktop;component/Themes/{name}.xaml") });
            }

            return Players.PlayerScreensRenderTests.Render("staff", vm);
        });
        Assert.True(size > 0, "The staff screen rendered no content.");
    }

    private async Task SetReputationAsync(ClubId clubId, int reputation) =>
        await _db.WriteAsync(s =>
        {
            var club = s.Repo<Club, ClubId>().FindAsync(clubId).GetAwaiter().GetResult()!;
            club.UpdateProfile(club.City, reputation, club.Balance, club.WageBudget, club.BoardExpectation);
        });
}
