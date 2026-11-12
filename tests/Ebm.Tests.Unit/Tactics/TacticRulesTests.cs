using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Tactics;

namespace Ebm.Tests.Unit.Tactics;

public sealed class TacticRulesTests
{
    private static Player Player(Position position, PlayerAttributes attributes) =>
        new(PlayerId.New(), "Test", "Player", new DateOnly(2000, 1, 1), CountryId.New(), 200, 95, position, attributes);

    [Fact]
    public void A_tactic_stores_systems_settings_and_assignments()
    {
        var player = PlayerId.New();
        var tactic = new Tactic(
            TacticId.New(), ClubId.New(), " Run and gun ", OffensiveSystem.Transition, new OffensiveSettings { Tempo = 5, Focus = OffensiveFocus.Perimeter },
            DefensiveSystem.FullCourtPress, new DefensiveSettings { Intensity = 5, PickAndRollCoverage = PickAndRollCoverage.Blitz });
        tactic.SetAssignments([new TacticalAssignment(player, TacticalRole.SpotUpShooter, 5, 30)]);

        Assert.Equal("Run and gun", tactic.Name);
        var team = tactic.ToTeamTactics();
        Assert.Equal((OffensiveSystem.Transition, 5, DefensiveSystem.FullCourtPress, PickAndRollCoverage.Blitz), (team.OffensiveSystem, team.Offense.Tempo, team.DefensiveSystem, team.Defense.PickAndRollCoverage));
        Assert.Equal((TacticalRole.SpotUpShooter, 5, 30), (team.Assignments[player].Role, team.Assignments[player].ShotPriority, team.Assignments[player].MinutesTarget));
    }

    [Fact]
    public void Invalid_settings_and_assignments_are_refused()
    {
        var tactic = Tactic.CreateDefault(ClubId.New());
        Assert.Throws<DomainValidationException>(() => tactic.Update("x", OffensiveSystem.FiveOut, new OffensiveSettings { Tempo = 6 }, DefensiveSystem.ManToMan, DefensiveSettings.Default));
        Assert.Throws<DomainValidationException>(() => tactic.Update("x", OffensiveSystem.FiveOut, OffensiveSettings.Default, DefensiveSystem.ManToMan, new DefensiveSettings { HelpDefense = 0 }));
        Assert.Throws<DomainValidationException>(() => tactic.Update(" ", OffensiveSystem.FiveOut, OffensiveSettings.Default, DefensiveSystem.ManToMan, DefensiveSettings.Default));
        Assert.Throws<DomainValidationException>(() => new TacticalAssignment(PlayerId.New(), TacticalRole.Slasher, 3, 41));
        Assert.Throws<DomainValidationException>(() => new TacticalAssignment(PlayerId.New(), TacticalRole.Slasher, 0));
        var twice = PlayerId.New();
        Assert.Throws<DomainValidationException>(() => tactic.SetAssignments([new TacticalAssignment(twice, TacticalRole.Slasher), new TacticalAssignment(twice, TacticalRole.Rebounder)]));
        Assert.Equal("Standard", tactic.Name);
    }

    [Fact]
    public void Unassigned_players_get_the_role_their_attributes_suggest()
    {
        var shooter = Player(Position.ShootingGuard, PlayerAttributes.Uniform(50).With(PlayerAttribute.ThreePoint, 90));
        var slasher = Player(Position.ShootingGuard, PlayerAttributes.Uniform(50).With(PlayerAttribute.Layup, 90).With(PlayerAttribute.Dunk, 90).With(PlayerAttribute.Speed, 90));
        var center = Player(Position.Center, PlayerAttributes.Uniform(50));
        var stretch = Player(Position.PowerForward, PlayerAttributes.Uniform(50).With(PlayerAttribute.ThreePoint, 85));

        Assert.Equal(TacticalRole.SpotUpShooter, TacticalRoles.Suggest(shooter));
        Assert.Equal(TacticalRole.Slasher, TacticalRoles.Suggest(slasher));
        Assert.Equal(TacticalRole.RimProtector, TacticalRoles.Suggest(center)); // the position's usual role wins ties
        Assert.Equal(TacticalRole.StretchBig, TacticalRoles.Suggest(stretch));
        Assert.Equal(TacticalRole.Slasher, TeamTactics.Default.AssignmentOf(slasher).Role);
        Assert.Equal(TacticLevel.Normal, TeamTactics.Default.AssignmentOf(slasher).ShotPriority);
    }

    [Fact]
    public void Levels_map_to_offsets_from_normal()
    {
        Assert.Equal([-1, -0.5, 0, 0.5, 1], TacticLevel.All.Select(TacticLevel.Offset));
    }
}
