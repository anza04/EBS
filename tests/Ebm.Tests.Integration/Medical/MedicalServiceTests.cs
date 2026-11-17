using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Medical;
using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Medical;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Staff;
using Ebm.Services.Clubs;
using Ebm.Services.Management;
using Ebm.Services.Medical;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Medical;

/// <summary>Step 22: injuries in matches and training, recovery, medical records and AI handling of injured players.</summary>
public sealed class MedicalServiceTests : IAsyncLifetime
{
    /// <summary>A Monday.</summary>
    private static readonly DateOnly Monday = new(2026, 11, 16);

    private readonly TestClock _clock = new(Monday);
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private IMedicalService Medical => _db.Services.GetRequiredService<IMedicalService>();

    private ClubId Home => _world.HomeClub.Id;

    public Task InitializeAsync() => CreateAsync(new InjuryRules { Frequency = 0 });

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Injuries_happen_in_matches_and_get_a_medical_assessment()
    {
        await CreateAsync(new InjuryRules { MatchRiskPerMinute = 1, TrainingRiskPerDay = 0 });
        _clock.Today = _world.Fixture.Date;

        var day = await Medical.ProcessDayAsync([_world.Match.Id]);

        var news = Assert.Single(day.Injuries);
        Assert.Equal((_world.Player.Id, Home, InjurySource.Match, false), (news.PlayerId, news.ClubId, news.Source, news.Aggravated));
        var injury = Assert.Single(await Medical.GetInjuriesAsync(_world.Player.Id));
        Assert.Equal((_world.Match.Id, _world.Fixture.Date, true), (injury.MatchId!.Value, injury.OccurredOn, injury.IsActive));
        Assert.True(injury.EstimatedReturn > _world.Fixture.Date);
        var player = (await _db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(_world.Player.Id)))!;
        Assert.Equal((injury.Status, injury.EstimatedReturn), (player.InjuryStatus, player.InjuryReturnDate!.Value));
        Assert.NotEqual(InjuryStatus.Healthy, player.InjuryStatus);
    }

    [Fact]
    public async Task Injuries_happen_in_training_but_not_to_free_agents_and_physios_are_hired()
    {
        await CreateAsync(new InjuryRules { MatchRiskPerMinute = 0, TrainingRiskPerDay = 5 });
        var freeAgent = await PlayerAsync(null);

        var day = await Medical.ProcessDayAsync([]);

        Assert.Equal(2, day.PhysiosHired);
        Assert.Contains(day.Injuries, i => i.PlayerId == _world.Player.Id && i.Source == InjurySource.Training);
        Assert.DoesNotContain(day.Injuries, i => i.PlayerId == freeAgent.Id);
        var medical = (await Medical.GetClubMedicalAsync(Home))!;
        Assert.Equal(StaffRole.Physio, medical.Physio!.Role);
        Assert.Equal(medical.Physio.Ability, medical.PhysioAbility);
    }

    [Fact]
    public async Task Injured_players_recover_over_time_and_return_short_of_fitness()
    {
        await InjureAsync(_world.Player.Id, InjuryType.HamstringStrain, 20);
        var assessed = (await Medical.GetInjuriesAsync(_world.Player.Id)).Single().AssessedOn;

        var days = 0;
        while ((await PlayerOf(_world.Player.Id)).InjuryStatus != InjuryStatus.Healthy && days < 60)
        {
            days++;
            _clock.Today = Monday.AddDays(days);
            await Medical.ProcessDayAsync([]);
        }

        Assert.InRange(days, 15, 30);
        var injury = (await Medical.GetInjuriesAsync(_world.Player.Id)).Single();
        Assert.Equal(Monday.AddDays(days + 1), injury.RecoveredOn);
        Assert.True(injury.AssessedOn > assessed); // Reassessed on the Mondays in between.
        Assert.Equal(InjuryRules.ReturnFitness(days + 1), (await PlayerOf(_world.Player.Id)).Fitness);

        var history = (await Medical.GetClubMedicalAsync(Home))!.History;
        Assert.Equal(injury.Id, Assert.Single(history).Injury.Id);
    }

    [Fact]
    public async Task A_condition_set_without_a_medical_record_ends_on_its_return_date()
    {
        await _db.WriteAsync(s => s.Repo<Player, PlayerId>().FindAsync(_world.Player.Id).Result!.UpdateCondition(90, 50, InjuryStatus.Injured, Monday.AddDays(2)));
        _clock.Today = Monday.AddDays(1);
        await Medical.ProcessDayAsync([]);
        Assert.Equal(InjuryStatus.Injured, (await PlayerOf(_world.Player.Id)).InjuryStatus);

        _clock.Today = Monday.AddDays(2);
        await Medical.ProcessDayAsync([]);
        Assert.Equal(InjuryStatus.Healthy, (await PlayerOf(_world.Player.Id)).InjuryStatus);
    }

    [Fact]
    public async Task Injured_players_are_unavailable_and_ai_coaches_leave_them_out()
    {
        await _db.Services.GetRequiredService<IClubManagementService>().PrepareMatchdayAsync([Home]);
        var clubs = _db.Services.GetRequiredService<IClubService>();
        Assert.Equal(RosterRole.Starter, (await clubs.GetRosterAsync(Home))!.Players.Single(p => p.Player.Id == _world.Player.Id).Role);

        await InjureAsync(_world.Player.Id, InjuryType.KneeSprain, 30);
        await _db.Services.GetRequiredService<IClubManagementService>().PrepareMatchdayAsync([Home]);

        var row = (await clubs.GetRosterAsync(Home))!.Players.Single(p => p.Player.Id == _world.Player.Id);
        Assert.Equal(PlayerAvailability.Unavailable, row.Availability);
        Assert.NotEqual(RosterRole.Starter, row.Role);
    }

    [Fact]
    public async Task Ai_clubs_sign_cover_for_long_term_injuries()
    {
        var away = _world.AwayClub.Id;
        List<Player> squad = [];
        for (var i = 0; i < SquadRules.Default.MinPlayers; i++)
        {
            squad.Add(await PlayerAsync(away));
        }

        await PlayerAsync(null);
        await PlayerAsync(null);
        await _db.WriteAsync(s =>
        {
            var club = s.Repo<Club, ClubId>().FindAsync(away).Result!;
            club.UpdateProfile(club.City, club.Reputation, 5_000_000, club.WageBudget, club.BoardExpectation);
        });
        var management = _db.Services.GetRequiredService<IClubManagementService>();
        Assert.DoesNotContain(away, await management.ClubsShortOfPlayersAsync([Home]));

        await InjureAsync(squad[0].Id, InjuryType.TornAcl, 240);
        await InjureAsync(squad[1].Id, InjuryType.AnkleSprain, 10); // Short-term: no cover.
        Assert.Contains(away, await management.ClubsShortOfPlayersAsync([Home]));

        await management.FillSquadsAsync([Home]);
        Assert.Equal(SquadRules.Default.MinPlayers + 1, (await _db.Services.GetRequiredService<IClubService>().GetRosterAsync(away))!.FirstTeam.Count());
        Assert.DoesNotContain(away, await management.ClubsShortOfPlayersAsync([Home]));
    }

    [Fact]
    public async Task The_medical_screen_shows_injuries_and_history()
    {
        await InjureAsync(_world.Player.Id, InjuryType.AnkleSprain, 5);
        var vm = new MedicalViewModel(Medical, new EditorLookups()) { ClubId = Home };
        await vm.RefreshAsync();

        var row = Assert.Single(vm.Players, p => p.Name == _world.Player.FullName);
        Assert.Equal(("Day to day", "Ankle sprain (minor)", "Light work only"), (row.Status, row.Injury, row.Training));
        Assert.Equal("Ankle sprain", Assert.Single(vm.History).Injury);
    }

    /// <summary>A fresh database with <paramref name="rules"/> (not validated: a risk of 1 makes an injury certain).</summary>
    private async Task CreateAsync(InjuryRules rules)
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _clock.Today = Monday;
        _db = await TestDatabase.CreateAsync(_clock, services => services.AddSingleton(rules));
        _world = await SampleWorld.SeedAsync(_db);
    }

    /// <summary>Stores an injury that happened on <see cref="Monday"/> and assesses it, as the medical day would.</summary>
    private async Task InjureAsync(PlayerId playerId, InjuryType type, int days)
    {
        await _db.WriteAsync(s =>
        {
            var club = s.Repo<Contract, ContractId>().ListAsync(c => c.PlayerId == playerId).Result.Single().ClubId;
            var injury = new Injury(InjuryId.New(), playerId, club, type, InjurySource.Match, Monday, days, false);
            injury.Assess(Monday, Monday.AddDays(days));
            s.Repo<Injury, InjuryId>().Add(injury);
            var player = s.Repo<Player, PlayerId>().FindAsync(playerId).Result!;
            player.UpdateCondition(player.Fitness, player.Morale, injury.Status, injury.EstimatedReturn);
        });
    }

    private async Task<Player> PlayerAsync(ClubId? clubId)
    {
        var player = new Player(PlayerId.New(), "Med", $"P{Guid.NewGuid():N}"[..12], Monday.AddYears(-25), _world.Italy.Id, 200, 95, Position.SmallForward, PlayerAttributes.Uniform(50));
        await _db.WriteAsync(s =>
        {
            s.Repo<Player, PlayerId>().Add(player);
            if (clubId is { } club)
            {
                s.Repo<Contract, ContractId>().Add(Contract.ForPlayer(ContractId.New(), club, player.Id, SampleWorld.SeasonStart, SampleWorld.SeasonStart.AddYears(2), 80_000));
                s.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>().Add(new PlayerClubHistoryEntry(PlayerClubHistoryEntryId.New(), player.Id, club, SampleWorld.SeasonStart));
            }
        });
        return player;
    }

    private async Task<Player> PlayerOf(PlayerId id) => (await _db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(id)))!;
}
