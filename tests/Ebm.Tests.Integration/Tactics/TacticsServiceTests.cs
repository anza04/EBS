using Ebm.Domain.Common;
using Ebm.Domain.Players;
using Ebm.Domain.Tactics;
using Ebm.Services.Clubs;
using Ebm.Services.Tactics;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Tactics;

public sealed class TacticsServiceTests : IAsyncLifetime
{
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private ITacticsService Tactics => _db.Services.GetRequiredService<ITacticsService>();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(new FixedTimeProvider(new DateOnly(2026, 11, 9)));
        _world = await SampleWorld.SeedAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private TacticDraft Draft(string name, OffensiveSystem offense, DefensiveSystem defense, params TacticalAssignment[] assignments) =>
        new(null, name, offense, new OffensiveSettings { Tempo = 4, Spacing = 5 }, defense, new DefensiveSettings { BallPressure = 2, Rebounding = ReboundingPriority.CrashBoards }, assignments);

    [Fact]
    public async Task Tactics_are_saved_selected_changed_and_handed_to_the_match_engine()
    {
        var club = _world.HomeClub.Id;
        Assert.Same(TeamTactics.Default, await Tactics.GetMatchTacticsAsync(club)); // nothing saved yet

        var player = _world.Player.Id;
        var first = await Tactics.SaveAsync(club, Draft("Five out", OffensiveSystem.FiveOut, DefensiveSystem.ManToMan, new TacticalAssignment(player, TacticalRole.StretchBig, 5, 28)));
        var second = await Tactics.SaveAsync(club, Draft("Zone", OffensiveSystem.PostOriented, DefensiveSystem.Zone32));

        var data = (await Tactics.GetClubTacticsAsync(club))!;
        Assert.Equal(["Five out", "Zone"], data.Tactics.Select(t => t.Name));
        Assert.Equal(first.Id, data.Selected!.Id); // the first tactic is selected automatically
        Assert.Contains(data.Squad, p => p.Player.Id == player);
        var stored = Assert.Single(data.Tactics[0].Assignments);
        Assert.Equal((TacticalRole.StretchBig, 5, 28), (stored.Role, stored.ShotPriority, stored.MinutesTarget));
        Assert.Equal((4, 5, 2, ReboundingPriority.CrashBoards), (data.Tactics[0].Offense.Tempo, data.Tactics[0].Offense.Spacing, data.Tactics[0].Defense.BallPressure, data.Tactics[0].Defense.Rebounding));

        var match = await Tactics.GetMatchTacticsAsync(club);
        Assert.Equal((OffensiveSystem.FiveOut, DefensiveSystem.ManToMan), (match.OffensiveSystem, match.DefensiveSystem));
        Assert.Equal(TacticalRole.StretchBig, match.Assignments[player].Role);

        // Change before the match: pick the zone tactic.
        await Tactics.SelectAsync(second.Id);
        match = await Tactics.GetMatchTacticsAsync(club);
        Assert.Equal((OffensiveSystem.PostOriented, DefensiveSystem.Zone32), (match.OffensiveSystem, match.DefensiveSystem));
        Assert.Single((await Tactics.GetClubTacticsAsync(club))!.Tactics, t => t.IsSelected);

        // Edit the selected tactic and delete it: the remaining one is selected.
        await Tactics.SaveAsync(club, Draft("Zone press", OffensiveSystem.Transition, DefensiveSystem.FullCourtPress) with { Id = second.Id });
        Assert.Equal(OffensiveSystem.Transition, (await Tactics.GetMatchTacticsAsync(club)).OffensiveSystem);
        await Tactics.DeleteAsync(second.Id);
        data = (await Tactics.GetClubTacticsAsync(club))!;
        Assert.Equal(first.Id, Assert.Single(data.Tactics).Id);
        Assert.True(data.Tactics[0].IsSelected);
    }

    [Fact]
    public async Task Duplicate_names_and_players_of_other_clubs_are_refused()
    {
        var club = _world.HomeClub.Id;
        await Tactics.SaveAsync(club, Draft("Base", OffensiveSystem.MotionOffense, DefensiveSystem.ManToMan));
        await Assert.ThrowsAsync<DomainValidationException>(() => Tactics.SaveAsync(club, Draft("base", OffensiveSystem.FiveOut, DefensiveSystem.Zone23)));
        await Assert.ThrowsAsync<DomainException>(() => Tactics.SaveAsync(club, Draft("Other", OffensiveSystem.FiveOut, DefensiveSystem.Zone23, new TacticalAssignment(PlayerId.New(), TacticalRole.Slasher))));
        await Assert.ThrowsAsync<DomainException>(() => Tactics.SaveAsync(_world.AwayClub.Id, Draft("Away", OffensiveSystem.FiveOut, DefensiveSystem.Zone23, new TacticalAssignment(_world.Player.Id, TacticalRole.Slasher))));
        Assert.Single((await Tactics.GetClubTacticsAsync(club))!.Tactics);
    }

    [Fact]
    public async Task Assignments_of_players_who_left_are_ignored_by_the_match_engine()
    {
        var club = _world.HomeClub.Id;
        await Tactics.SaveAsync(club, Draft("Base", OffensiveSystem.MotionOffense, DefensiveSystem.ManToMan, new TacticalAssignment(_world.Player.Id, TacticalRole.Rebounder)));
        await _db.Services.GetRequiredService<IClubService>().AssignPlayerAsync(_world.Player.Id, _world.AwayClub.Id);

        Assert.Empty((await Tactics.GetMatchTacticsAsync(club)).Assignments);
    }
}
