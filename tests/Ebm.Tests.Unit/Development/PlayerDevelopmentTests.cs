using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Countries;
using Ebm.Domain.Development;
using Ebm.Domain.Management;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Hosting.Configuration;
using Microsoft.Extensions.Configuration;

namespace Ebm.Tests.Unit.Development;

/// <summary>Step 21: development formulas, age curves, potential, fitness, retirement rules and the AI training planner.</summary>
public sealed class PlayerDevelopmentTests
{
    private static readonly PlayerDevelopment Engine = PlayerDevelopment.Default;

    private static readonly TrainingConditions Normal = new(TrainingFocus.Balanced, null, TrainingIntensity.Normal, 60, 60);

    private static DevelopmentInput Input(
        int age,
        int overall = 50,
        int potential = 75,
        int workRate = 50,
        TrainingConditions? training = null,
        double? share = 0.5,
        int? level = 1,
        InjuryStatus injury = InjuryStatus.Healthy,
        int quality = 50) =>
        new(age, overall, potential, PlayerAttributes.Uniform(quality).With(PlayerAttribute.WorkRate, workRate), 100, injury, share, 60, training ?? Normal, level, false);

    /// <summary>Average attribute change over a simulated year (52 weekly steps, attributes carried over).</summary>
    private static double YearChange(DevelopmentInput input, int seed, double luck = 1.0)
    {
        var random = new Random(seed);
        var attributes = input.Attributes;
        for (var week = 0; week < PlayerDevelopment.WeeksPerYear; week++)
        {
            attributes = Engine.Week(input with { Attributes = attributes }, random, luck).Attributes;
        }

        return PlayerCatalog.Attributes.Average(a => attributes[a] - input.Attributes[a]);
    }

    [Fact]
    public void Young_players_with_potential_improve()
    {
        var change = Enumerable.Range(1, 20).Average(seed => YearChange(Input(19), seed));
        Assert.InRange(change, 4, 14);
        Assert.Equal(0, Engine.GrowthPerYear(Input(19, overall: 75, potential: 75)));
    }

    [Fact]
    public void Old_players_decline_and_athleticism_goes_first()
    {
        Assert.True(Enumerable.Range(1, 20).Average(seed => YearChange(Input(34), seed)) < -1.5);
        Assert.Equal(0, PlayerDevelopment.DeclinePerYear(29));
        Assert.True(PlayerDevelopment.DeclinePerYear(36) > PlayerDevelopment.DeclinePerYear(32));
        Assert.True(PlayerDevelopment.DeclineWeight(PlayerAttribute.Speed) > PlayerDevelopment.DeclineWeight(PlayerAttribute.ThreePoint));
        Assert.Equal(0, PlayerDevelopment.DeclineWeight(PlayerAttribute.Concentration));
    }

    [Fact]
    public void Development_is_not_deterministic_but_reproducible_for_a_seed()
    {
        var outcomes = Enumerable.Range(1, 10).Select(seed => YearChange(Input(19), seed)).ToList();
        Assert.True(outcomes.Distinct().Count() > 1);
        Assert.Equal(YearChange(Input(19), 7), YearChange(Input(19), 7));
        Assert.NotEqual(PlayerDevelopment.SeasonLuck(PlayerId.New(), 2026), PlayerDevelopment.SeasonLuck(PlayerId.New(), 2026));
        var id = PlayerId.New();
        Assert.InRange(PlayerDevelopment.SeasonLuck(id, 2026), 0.6, 1.4);
        Assert.Equal(PlayerDevelopment.SeasonLuck(id, 2026), PlayerDevelopment.SeasonLuck(id, 2026));
    }

    [Fact]
    public void Growth_depends_on_age_playing_time_coaching_level_professionalism_and_condition()
    {
        var baseline = Engine.GrowthPerYear(Input(19));
        Assert.True(Engine.GrowthPerYear(Input(24)) < baseline);
        Assert.True(Engine.GrowthPerYear(Input(19, share: 0)) < baseline);
        Assert.True(Engine.GrowthPerYear(Input(19, training: Normal with { CoachAbility = 90 })) > baseline);
        Assert.True(Engine.GrowthPerYear(Input(19, training: Normal with { Intensity = TrainingIntensity.Hard })) > baseline);
        Assert.True(Engine.GrowthPerYear(Input(19, level: 2)) < baseline);
        Assert.True(Engine.GrowthPerYear(Input(19, level: null)) < Engine.GrowthPerYear(Input(19, level: 3)));
        Assert.True(Engine.GrowthPerYear(Input(19, workRate: 90)) > baseline);
        Assert.Equal(0, Engine.GrowthPerYear(Input(19, injury: InjuryStatus.Injured)));
        Assert.Equal(baseline / 2, Engine.GrowthPerYear(Input(19, injury: InjuryStatus.DayToDay)), 6);
        Assert.True(PlayerDevelopment.DeclineFor(Input(34, workRate: 90)) < PlayerDevelopment.DeclineFor(Input(34, workRate: 10)));
    }

    [Fact]
    public void A_focus_moves_development_to_its_attributes()
    {
        var input = Input(19, training: Normal with { TeamFocus = TrainingFocus.Shooting, IndividualFocus = TrainingFocus.Shooting });
        var random = new Random(3);
        var attributes = input.Attributes;
        for (var week = 0; week < PlayerDevelopment.WeeksPerYear; week++)
        {
            attributes = Engine.Week(input with { Attributes = attributes }, random, 1).Attributes;
        }

        var shooting = TrainingFocuses.AttributesOf(TrainingFocus.Shooting).Average(a => attributes[a] - 50.0);
        var defense = TrainingFocuses.AttributesOf(TrainingFocus.Defense).Average(a => attributes[a] - 50.0);
        Assert.True(shooting > defense + 2, $"shooting {shooting}, defense {defense}");
        Assert.Equal(50, attributes.WorkRate);
        Assert.Equal(50, attributes.Leadership);
    }

    [Fact]
    public void Experience_grows_with_minutes()
    {
        var random = new Random(1);
        var gained = Enumerable.Range(0, 52).Sum(_ => Engine.Week(Input(25) with { MinutesThisWeek = 60 }, random, 1).ExperienceGain);
        Assert.InRange(gained, 2, 10);
        Assert.Equal(0, Engine.Week(Input(25) with { MinutesThisWeek = 0 }, random, 1).ExperienceGain);
    }

    [Fact]
    public void Fitness_drops_with_minutes_and_recovers_with_rest()
    {
        Assert.Equal(96, Engine.DailyFitness(100, 32, Normal with { FitnessCoachAbility = 50 }, InjuryStatus.Healthy));
        Assert.Equal(76, Engine.DailyFitness(70, 0, Normal with { FitnessCoachAbility = 50, Intensity = TrainingIntensity.Light }, InjuryStatus.Healthy));
        Assert.True(PlayerDevelopment.DailyRecovery(Normal with { TeamFocus = TrainingFocus.Recovery }, InjuryStatus.Healthy) > PlayerDevelopment.DailyRecovery(Normal, InjuryStatus.Healthy));
        Assert.True(PlayerDevelopment.DailyRecovery(Normal with { FitnessCoachAbility = 90 }, InjuryStatus.Healthy) > PlayerDevelopment.DailyRecovery(Normal, InjuryStatus.Healthy));
        Assert.Equal(100, Engine.DailyFitness(99, 0, Normal, InjuryStatus.Healthy));
    }

    [Fact]
    public void Potential_is_realised_or_lost_with_age_and_never_below_the_overall()
    {
        var random = new Random(5);
        Assert.InRange(PlayerDevelopment.RevisePotential(19, 50, 80, random), 77, 83);
        Assert.Equal(74, PlayerDevelopment.RevisePotential(26, 60, 80, random));
        Assert.Equal(60, PlayerDevelopment.RevisePotential(30, 60, 80, random));
        Assert.Equal(70, PlayerDevelopment.RevisePotential(20, 70, 50, random));
    }

    [Fact]
    public void Retirement_follows_the_configured_rules()
    {
        var rules = RetirementRules.Default;
        Assert.Equal(0, rules.Chance(32, 60, true, false, false));
        Assert.Equal(1, rules.Chance(41, 80, true, true, false));
        Assert.True(rules.Chance(35, 60, false, false, false) > rules.Chance(35, 60, true, false, false));
        Assert.True(rules.Chance(35, 60, true, true, false) < rules.Chance(35, 60, true, false, false));
        Assert.True(rules.Chance(35, 30, true, false, false) > rules.Chance(35, 80, true, false, false));
        var strict = new RetirementRules { MinAge = 36, ForcedAge = 38 };
        Assert.Equal(0, strict.Chance(35, 40, false, false, true));
        Assert.Equal(1, strict.Chance(38, 90, true, true, false));
        Assert.Throws<InvalidOperationException>(() => new RetirementRules { MinAge = 40, ForcedAge = 39 }.Validated());
        Assert.Throws<InvalidOperationException>(() => new RetirementRules { ChancePerYear = 2 }.Validated());
    }

    [Fact]
    public void Retirement_rules_bind_from_settings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ebm:Retirement:MinAge"] = "35", ["Ebm:Retirement:ChancePerYear"] = "0.2" })
            .Build();
        var options = configuration.GetSection(EbmOptions.SectionName).Get<EbmOptions>()!;
        Assert.Equal((35, 0.2, RetirementRules.Default.ForcedAge), (options.Retirement.MinAge, options.Retirement.ChancePerYear, options.Retirement.ForcedAge));
    }

    [Fact]
    public void The_ai_coach_trains_the_weakest_area_and_gives_young_players_their_own_programme()
    {
        var today = new DateOnly(2026, 11, 16);
        var weakDefense = PlayerAttributes.Uniform(60);
        foreach (var attribute in TrainingFocuses.AttributesOf(TrainingFocus.Defense))
        {
            weakDefense = weakDefense.With(attribute, 40);
        }

        var squad = Enumerable.Range(0, 8).Select(i => Make(today.AddYears(-27), weakDefense, 60)).ToList();
        var young = Make(today.AddYears(-19), PlayerAttributes.Uniform(45).With(PlayerAttribute.ThreePoint, 20).With(PlayerAttribute.MidRange, 20), 80);
        var veteran = Make(today.AddYears(-33), weakDefense, 60);
        squad.Add(young);
        squad.Add(veteran);

        var plan = TrainingPlanner.Plan([.. squad.Select(p => (p, PlayerRatingCalculator.Default.Evaluate(p)))], today);
        Assert.Equal((TrainingFocus.Defense, TrainingIntensity.Normal), (plan.TeamFocus, plan.Intensity));
        Assert.Equal(TrainingFocus.Shooting, plan.Individuals.Single(i => i.PlayerId == young.Id).Focus);
        Assert.Equal(TrainingFocus.Physical, plan.Individuals.Single(i => i.PlayerId == veteran.Id).Focus);

        foreach (var player in squad)
        {
            player.UpdateFitness(70);
        }

        Assert.Equal(TrainingIntensity.Light, TrainingPlanner.Plan([.. squad.Select(p => (p, PlayerRatingCalculator.Default.Evaluate(p)))], today).Intensity);
        Assert.Equal(TrainingFocus.Balanced, TrainingPlanner.Plan([], today).TeamFocus);
    }

    [Fact]
    public void Training_plans_refuse_duplicate_players()
    {
        var plan = new TrainingPlan(TrainingPlanId.New(), ClubId.New());
        var player = PlayerId.New();
        Assert.Throws<DomainValidationException>(() =>
            plan.Update(TrainingFocus.Defense, TrainingIntensity.Hard, [new IndividualTraining(player, TrainingFocus.Shooting), new IndividualTraining(player, TrainingFocus.Physical)]));
        plan.Update(TrainingFocus.Defense, TrainingIntensity.Hard, [new IndividualTraining(player, TrainingFocus.Shooting)]);
        Assert.Equal(TrainingFocus.Shooting, plan.FocusOf(player));
    }

    [Fact]
    public void Retirement_is_final()
    {
        var player = Make(new DateOnly(1985, 1, 1), PlayerAttributes.Uniform(50), 50);
        player.Retire(new DateOnly(2027, 7, 1));
        Assert.True(player.IsRetired);
        Assert.Throws<DomainValidationException>(() => player.Retire(new DateOnly(2028, 7, 1)));
    }

    private static Player Make(DateOnly birth, PlayerAttributes attributes, int potential)
    {
        var player = new Player(PlayerId.New(), "Test", "Player", birth, CountryId.New(), 200, 95, Position.SmallForward, attributes);
        player.UpdateProfile(200, PositionSet.Empty, 1, 1, potential);
        return player;
    }
}
