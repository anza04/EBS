using Ebm.Domain.Chemistry;
using Ebm.Domain.Common;
using Ebm.Domain.Countries;
using Ebm.Domain.Development;
using Ebm.Domain.Management;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Staff;
using Ebm.Domain.Tactics;
using Ebm.Tests.Unit.Matches;
using Xunit.Abstractions;

namespace Ebm.Tests.Unit.Staff;

/// <summary>Step 28: staff attributes, role abilities, salaries and their influence (docs/STAFF.md).</summary>
public sealed class StaffRulesTests(ITestOutputHelper output)
{
    private static StaffMember Coach(StaffRole role, StaffProfile profile) =>
        new(StaffMemberId.New(), "Test", "Coach", new DateOnly(1975, 1, 1), CountryId.New(), role, profile);

    [Fact]
    public void Role_ability_weighs_the_attributes_of_the_job_plus_experience()
    {
        var profile = new StaffProfile(TacticalKnowledge: 80, Development: 60, Motivation: 40, Scouting: 30, MedicalExpertise: 70, Reputation: 50, Experience: 0);
        Assert.Equal(62, StaffRules.RoleAbility(StaffRole.HeadCoach, profile)); // 32 + 12 + 18
        Assert.Equal(70, StaffRules.RoleAbility(StaffRole.AssistantCoach, profile));
        Assert.Equal(60, StaffRules.RoleAbility(StaffRole.DevelopmentCoach, profile));
        Assert.Equal(65, StaffRules.RoleAbility(StaffRole.FitnessCoach, profile));
        Assert.Equal(70, StaffRules.RoleAbility(StaffRole.Physio, profile));
        Assert.Equal(30, StaffRules.RoleAbility(StaffRole.Scout, profile));

        // Experience: +1 per four years, at most +5; never above 100.
        Assert.Equal(35, StaffRules.RoleAbility(StaffRole.Scout, profile with { Experience = 30 }));
        Assert.Equal(32, StaffRules.RoleAbility(StaffRole.Scout, profile with { Experience = 9 }));
        Assert.Equal(100, StaffRules.Effective(99, 20));

        // Older callers: a uniform profile keeps the ability exactly (and the migration relies on it).
        Assert.All(Enum.GetValues<StaffRole>(), r => Assert.Equal(64, new StaffMember(StaffMemberId.New(), "A", "B", new DateOnly(1970, 1, 1), CountryId.New(), r, 64).Ability));
    }

    [Fact]
    public void Attributes_are_validated()
    {
        var good = StaffProfile.Uniform(50);
        Assert.Throws<DomainValidationException>(() => Coach(StaffRole.HeadCoach, good with { Motivation = 0 }));
        Assert.Throws<DomainValidationException>(() => Coach(StaffRole.HeadCoach, good with { Reputation = 101 }));
        Assert.Throws<DomainValidationException>(() => Coach(StaffRole.HeadCoach, good with { Experience = StaffProfile.MaxExperience + 1 }));
        Assert.Throws<DomainValidationException>(() => Coach(StaffRole.HeadCoach, good with { Style = (CoachingStyle)99 }));
        var coach = Coach(StaffRole.HeadCoach, good with { PreferredOffense = OffensiveSystem.FiveOut, Style = CoachingStyle.Attacking });
        Assert.Equal(OffensiveSystem.FiveOut, coach.Profile.PreferredOffense);
        Assert.Equal(CoachingStyle.Attacking, coach.Style);
    }

    [Fact]
    public void Demands_grow_with_ability_and_reputation_and_reputation_limits_who_joins()
    {
        var average = StaffProfile.Uniform(50);
        Assert.True(StaffRules.SalaryDemand(StaffRole.HeadCoach, average) > StaffRules.SalaryDemand(StaffRole.AssistantCoach, average));
        Assert.True(StaffRules.SalaryDemand(StaffRole.Scout, average with { Scouting = 80 }) > StaffRules.SalaryDemand(StaffRole.Scout, average));
        Assert.True(StaffRules.SalaryDemand(StaffRole.Scout, average with { Reputation = 90 }) > StaffRules.SalaryDemand(StaffRole.Scout, average));
        Assert.Equal(0, StaffRules.SalaryDemand(StaffRole.Physio, average) % 1_000);

        Assert.True(StaffRules.WillJoin(70, 50));
        Assert.False(StaffRules.WillJoin(71, 50));
        Assert.Equal(1, StaffRules.Wanted(StaffRole.HeadCoach, 90));
        Assert.Equal(0, StaffRules.Wanted(StaffRole.DevelopmentCoach, 49));
        Assert.Equal(1, StaffRules.Wanted(StaffRole.DevelopmentCoach, 50));
        Assert.Equal(3, StaffRules.Wanted(StaffRole.AssistantCoach, 100));
        Assert.All(Enum.GetValues<StaffRole>(), r => Assert.True(StaffRules.Wanted(r, 100) <= StaffRules.MaxPerRole(r)));
    }

    [Fact]
    public void Generated_staff_is_strong_in_the_role_and_follows_the_club_reputation()
    {
        var random = new Random(7);
        var top = Enumerable.Range(0, 200).Select(_ => StaffRules.Generate(StaffRole.Scout, 90, random)).ToList();
        var low = Enumerable.Range(0, 200).Select(_ => StaffRules.Generate(StaffRole.Scout, 20, random)).ToList();
        Assert.True(top.Average(p => p.Scouting) > low.Average(p => p.Scouting) + 25);
        Assert.True(top.Average(p => p.Scouting) > top.Average(p => p.TacticalKnowledge) + 10);
        Assert.True(top.Average(p => p.Reputation) > low.Average(p => p.Reputation) + 50);
        Assert.All(top, p => Assert.Equal(CoachingStyle.Balanced, p.Style));
        Assert.All(top, p => Assert.Null(p.PreferredOffense));

        var heads = Enumerable.Range(0, 100).Select(_ => StaffRules.Generate(StaffRole.HeadCoach, 60, random)).ToList();
        Assert.True(heads.Select(p => p.Style).Distinct().Count() >= 4);
        Assert.Contains(heads, p => p.PreferredOffense is not null && p.PreferredDefense is not null);
        Assert.Contains(heads, p => p.PreferredOffense is null);
    }

    [Fact]
    public void Coaching_styles_change_growth_and_motivation_changes_morale()
    {
        Assert.Equal(1.10, StaffRules.StyleGrowthFactor(CoachingStyle.YouthDevelopment, 21));
        Assert.Equal(1.0, StaffRules.StyleGrowthFactor(CoachingStyle.YouthDevelopment, 24));
        Assert.Equal(1.05, StaffRules.StyleGrowthFactor(CoachingStyle.Disciplinarian, 30));
        Assert.Equal(1.0, StaffRules.StyleGrowthFactor(CoachingStyle.Attacking, 20));

        Assert.Equal(1, StaffRules.MoraleEffect(40, 75, CoachingStyle.Balanced));
        Assert.Equal(0, StaffRules.MoraleEffect(40, 60, CoachingStyle.Balanced));
        Assert.Equal(1, StaffRules.MoraleEffect(40, 60, CoachingStyle.Motivator));
        Assert.Equal(-1, StaffRules.MoraleEffect(55, 25, CoachingStyle.Balanced));
        Assert.Equal(-1, StaffRules.MoraleEffect(70, 60, CoachingStyle.Disciplinarian));

        // The engine: growth of a young player under a youth-development coach.
        var input = new DevelopmentInput(20, 60, 80, PlayerAttributes.Uniform(60), 100, InjuryStatus.Healthy, 0.3, 120,
            new TrainingConditions(TrainingFocus.Balanced, null, TrainingIntensity.Normal, 60, 60), 1, false);
        var youth = input with { Training = input.Training with { Style = CoachingStyle.YouthDevelopment } };
        Assert.Equal(PlayerDevelopment.Default.GrowthPerYear(input) * 1.10, PlayerDevelopment.Default.GrowthPerYear(youth), 6);
    }

    [Fact]
    public void The_head_coach_shapes_the_ai_tactic()
    {
        var balanced = Enumerable.Range(0, 9)
            .Select(i => new Player(PlayerId.New(), "Test", $"P{i}", new DateOnly(2000, 1, 1), CountryId.New(), 200, 95, (Position)(i % 5), PlayerAttributes.Uniform(60)))
            .Select(p => (p, PlayerRatingCalculator.Default.Evaluate(p))).ToList();
        var preferring = Coach(StaffRole.HeadCoach, StaffProfile.Uniform(60) with { PreferredOffense = OffensiveSystem.PickAndRoll, PreferredDefense = DefensiveSystem.Zone23 });
        var plan = TacticPlanner.Plan(balanced, preferring);
        Assert.Equal(OffensiveSystem.PickAndRoll, plan.OffensiveSystem);
        Assert.Equal(DefensiveSystem.Zone23, plan.DefensiveSystem);
        Assert.Contains("preference", plan.Reason, StringComparison.Ordinal);

        var attacking = TacticPlanner.Plan(balanced, Coach(StaffRole.HeadCoach, StaffProfile.Uniform(60) with { Style = CoachingStyle.Attacking }));
        Assert.Equal(4, attacking.Offense.Tempo);
        var defensive = TacticPlanner.Plan(balanced, Coach(StaffRole.HeadCoach, StaffProfile.Uniform(60) with { Style = CoachingStyle.Defensive }));
        Assert.Equal(4, defensive.Defense.BallPressure);
        var strict = TacticPlanner.Plan(balanced, Coach(StaffRole.HeadCoach, StaffProfile.Uniform(60) with { Style = CoachingStyle.Disciplinarian }));
        Assert.Equal(RotationStrategy.Tight, strict.Situations.Rotation);
        Assert.Equal(FoulManagement.Protect, strict.Situations.FoulManagement);
        Assert.Equal(TacticPlanner.Plan(balanced).OffensiveSystem, TacticPlanner.Plan(balanced, null).OffensiveSystem);
    }

    [Fact]
    public void Tactical_knowledge_wins_games_and_weak_coaches_prepare_no_game_plan()
    {
        var situations = SituationSettings.Default with { CoachAdjusts = true };
        TeamTactics Tactics() => new(OffensiveSystem.MotionOffense, OffensiveSettings.Default, DefensiveSystem.ManToMan, DefensiveSettings.Default, new Dictionary<PlayerId, TacticalAssignment>())
        {
            Situations = situations,
        };

        List<MatchSimulation> Play(int knowledge) =>
        [
            .. Enumerable.Range(0, 300).Select(i =>
            {
                var home = SimulationTeams.Team("Home", 60, i * 2, Tactics()) with { Coach = new CoachProfile(knowledge, CoachingStyle.Balanced) };
                var away = SimulationTeams.Team("Away", 60, (i * 2) + 1, Tactics());
                var sim = new MatchSimulation(new MatchSetup(home, away, i));
                sim.RunToEnd();
                return sim;
            }),
        ];

        var expert = Play(95);
        var weak = Play(10);
        double Margin(List<MatchSimulation> games) => games.Average(g => g.HomeScore - g.AwayScore);
        output.WriteLine($"Home margin: expert {Margin(expert):F2}, weak {Margin(weak):F2}");
        Assert.True(Margin(expert) > Margin(weak) + 1.5);
        // Against poor shooters who score inside a coach packs a zone (Step 27), unless he is too weak to prepare a plan.
        DefensiveSystem Planned(int knowledge)
        {
            var home = SimulationTeams.Team("Home", 60, 1, Tactics()) with { Coach = new CoachProfile(knowledge, CoachingStyle.Balanced) };
            var away = SimulationTeams.Team("Away", 60, 2, Tactics());
            foreach (var p in away.Players)
            {
                p.Player.UpdateAttributes(p.Player.Attributes.With(PlayerAttribute.ThreePoint, 25).With(PlayerAttribute.PostMoves, 70).With(PlayerAttribute.CloseShot, 70));
            }

            var sim = new MatchSimulation(new MatchSetup(home, away, 1));
            sim.Step();
            return sim.Tactics(TeamSide.Home).DefensiveSystem;
        }

        Assert.Equal(DefensiveSystem.Zone23, Planned(StaffRules.PlanningKnowledge));
        Assert.Equal(DefensiveSystem.ManToMan, Planned(StaffRules.PlanningKnowledge - 1));
        Assert.Equal(0.0, StaffRules.CoachEdge(CoachProfile.Neutral.TacticalKnowledge));
        Assert.Equal(6, StaffRules.TimeoutRun(80));
        Assert.Equal(120, StaffRules.ClosingReviewSeconds(30));
    }
}
