using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Training;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Development;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Staff;
using Ebm.Services.Contracts;
using Ebm.Services.Development;
using Ebm.Services.Management;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Development;

/// <summary>Step 21: training plans, weekly development, fitness, potential, retirement and AI training plans.</summary>
public sealed class DevelopmentServiceTests : IAsyncLifetime
{
    /// <summary>A Monday.</summary>
    private static readonly DateOnly Monday = new(2026, 11, 16);

    private readonly TestClock _clock = new(Monday);
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private IDevelopmentService Development => _db.Services.GetRequiredService<IDevelopmentService>();

    private ClubId Home => _world.HomeClub.Id;

    private ClubId Away => _world.AwayClub.Id;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(_clock);
        _world = await SampleWorld.SeedAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Training_plans_are_saved_for_squad_players_only()
    {
        var shown = (await Development.GetClubTrainingAsync(Home))!;
        Assert.False(shown.IsSaved);
        Assert.Equal((TrainingFocus.Balanced, TrainingIntensity.Normal), (shown.Plan.TeamFocus, shown.Plan.Intensity));
        Assert.Contains(shown.Players, p => p.Player.Id == _world.Player.Id);
        Assert.Contains(shown.Coaches, c => c.Role == StaffRole.HeadCoach);

        await Development.SavePlanAsync(Home, new TrainingPlanDraft(TrainingFocus.Defense, TrainingIntensity.Hard, [new IndividualTraining(_world.Player.Id, TrainingFocus.Shooting)]));
        var saved = (await Development.GetClubTrainingAsync(Home))!;
        Assert.True(saved.IsSaved);
        Assert.Equal((TrainingFocus.Defense, TrainingIntensity.Hard), (saved.Plan.TeamFocus, saved.Plan.Intensity));
        Assert.Equal(TrainingFocus.Shooting, saved.Players.Single(p => p.Player.Id == _world.Player.Id).IndividualFocus);

        var stranger = await PlayerAsync(null, 20, 50, 70);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Development.SavePlanAsync(Home, new TrainingPlanDraft(TrainingFocus.Balanced, TrainingIntensity.Normal, [new IndividualTraining(stranger.Id, TrainingFocus.Physical)])));
    }

    [Fact]
    public async Task Young_players_improve_and_old_players_decline_week_by_week()
    {
        var young = await PlayerAsync(Home, 19, 45, 85);
        var old = await PlayerAsync(Away, 37, 70, 70);
        var youngBefore = await OverallAsync(young.Id);
        var oldBefore = await OverallAsync(old.Id);

        _clock.Today = Monday.AddDays(1);
        Assert.Equal(0, (await Development.ProcessDayAsync([])).PlayersDeveloped);

        DevelopmentDay? first = null;
        for (var week = 0; week < 26; week++)
        {
            _clock.Today = Monday.AddDays(7 * week);
            var day = await Development.ProcessDayAsync([]);
            first ??= day;
        }

        Assert.True(first!.CoachesHired > 0);
        Assert.True(first.PlayersDeveloped > 0);
        Assert.True(await OverallAsync(young.Id) > youngBefore, "the young player should improve");
        Assert.True(await OverallAsync(old.Id) < oldBefore, "the old player should decline");
        var coaches = (await Development.GetClubTrainingAsync(Home))!.Coaches;
        Assert.Contains(coaches, c => c.Role == StaffRole.AssistantCoach);
        Assert.Contains(coaches, c => c.Role == StaffRole.FitnessCoach);
    }

    [Fact]
    public async Task Matches_tire_players_and_training_recovers_them()
    {
        _clock.Today = _world.Fixture.Date;
        await Development.ProcessDayAsync([_world.Match.Id]);
        var tired = await FitnessAsync(_world.Player.Id);
        Assert.InRange(tired, 90, 97); // 32 minutes: −8, then a day's recovery.

        _clock.Today = _world.Fixture.Date.AddDays(1);
        await Development.ProcessDayAsync([]);
        Assert.True(await FitnessAsync(_world.Player.Id) > tired);
    }

    [Fact]
    public async Task Players_retire_at_the_end_of_the_season_and_leave_the_game()
    {
        var veteran = await PlayerAsync(Home, 42, 60, 60);
        var end = _world.Season.EndDate;
        _clock.Today = end;
        var season = await Development.ProcessSeasonEndAsync();

        var retired = Assert.Single(season.Retired, r => r.PlayerId == veteran.Id);
        Assert.Equal((42, _world.HomeClub.Name), (retired.Age, retired.Club));
        var stored = await _db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(veteran.Id));
        Assert.Equal(end.AddDays(1), stored!.RetiredOn);
        var contract = (await _db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync(c => c.PlayerId == veteran.Id))).Single();
        Assert.Equal((ContractStatus.Terminated, end.AddDays(1)), (contract.Status, contract.TerminationDate));

        _clock.Today = end.AddDays(1);
        var contracts = _db.Services.GetRequiredService<IContractService>();
        Assert.DoesNotContain(await contracts.GetFreeAgentsAsync(), f => f.Player.Id == veteran.Id);
        await Assert.ThrowsAsync<DomainValidationException>(() => contracts.NegotiateAsync(veteran.Id, Away, new ContractTerms(end.AddDays(1), end.AddYears(1), 50_000)));
        Assert.DoesNotContain((await Development.GetClubTrainingAsync(Home))!.Players, p => p.Player.Id == veteran.Id);
        Assert.Equal(0, (await Development.ProcessDayAsync([])).FitnessChanged + (await Development.ProcessSeasonEndAsync()).Retired.Count);
    }

    [Fact]
    public async Task Potential_never_ends_below_the_overall()
    {
        var player = await PlayerAsync(Home, 30, 70, 90);
        _clock.Today = _world.Season.EndDate;
        await Development.ProcessSeasonEndAsync();
        var stored = await _db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(player.Id));
        Assert.Equal(PlayerRatingCalculator.Default.Evaluate(stored!).Overall, stored!.Potential);
    }

    [Fact]
    public async Task Ai_clubs_plan_their_own_training()
    {
        await PlayerAsync(Away, 20, 45, 80);
        var planned = await _db.Services.GetRequiredService<IClubManagementService>().PlanTrainingAsync([Home]);
        Assert.True(planned > 0);
        Assert.True((await Development.GetClubTrainingAsync(Away))!.IsSaved);
        Assert.False((await Development.GetClubTrainingAsync(Home))!.IsSaved);
    }

    [Fact]
    public async Task The_training_screen_saves_the_plan()
    {
        var vm = new TrainingViewModel(Development, new EditorLookups()) { ClubId = Home };
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Players, p => p.Player.Player.Id == _world.Player.Id);
        vm.TeamFocus = TrainingFocus.Physical;
        row.Focus = nameof(TrainingFocus.Tactical);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Training plan saved.", vm.Message);
        var saved = (await Development.GetClubTrainingAsync(Home))!;
        Assert.Equal((TrainingFocus.Physical, TrainingFocus.Tactical), (saved.Plan.TeamFocus, saved.Plan.FocusOf(_world.Player.Id)));
    }

    private async Task<Player> PlayerAsync(ClubId? clubId, int age, int quality, int potential)
    {
        var player = new Player(PlayerId.New(), "Dev", $"P{Guid.NewGuid():N}"[..12], Monday.AddYears(-age).AddDays(-30), _world.Italy.Id, 200, 95, Position.SmallForward, PlayerAttributes.Uniform(quality));
        player.UpdateProfile(200, PositionSet.Empty, 10, 10, potential);
        await _db.WriteAsync(s =>
        {
            s.Repo<Player, PlayerId>().Add(player);
            if (clubId is { } club)
            {
                s.Repo<Contract, ContractId>().Add(Contract.ForPlayer(ContractId.New(), club, player.Id, SampleWorld.SeasonStart, SampleWorld.SeasonStart.AddYears(2), 80_000));
            }
        });
        return player;
    }

    private async Task<int> OverallAsync(PlayerId id) =>
        PlayerRatingCalculator.Default.Evaluate((await _db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(id)))!).Overall;

    private async Task<int> FitnessAsync(PlayerId id) => (await _db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(id)))!.Fitness;
}
