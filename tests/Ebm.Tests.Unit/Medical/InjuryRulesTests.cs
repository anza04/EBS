using Ebm.Domain.Common;
using Ebm.Domain.Countries;
using Ebm.Domain.Development;
using Ebm.Domain.Management;
using Ebm.Domain.Medical;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Hosting.Configuration;
using Microsoft.Extensions.Configuration;

namespace Ebm.Tests.Unit.Medical;

/// <summary>Step 22: injury risk, types, recovery, medical assessment and the injury record (docs/INJURIES.md).</summary>
public sealed class InjuryRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static readonly InjuryRiskFactors Average = new(25, 100, 60, 50, 50, 50, 0, false, false, 1.0);

    [Fact]
    public void An_average_player_has_average_risk()
    {
        Assert.Equal(1.0, InjuryRules.RiskMultiplier(Average), 3);
    }

    [Fact]
    public void Risk_grows_with_workload_fatigue_age_and_history_and_falls_with_physique_and_medical_care()
    {
        var baseline = InjuryRules.RiskMultiplier(Average);
        Assert.Equal(2.0, InjuryRules.RiskMultiplier(Average with { Fitness = 40 }), 3);
        Assert.True(InjuryRules.RiskMultiplier(Average with { MinutesThisWeek = 160 }) > baseline);
        Assert.True(InjuryRules.RiskMultiplier(Average with { Age = 34 }) > baseline);
        Assert.True(InjuryRules.RiskMultiplier(Average with { RecentInjuries = 2 }) > baseline);
        Assert.True(InjuryRules.RiskMultiplier(Average with { RecentlyReturned = true }) > baseline);
        Assert.True(InjuryRules.RiskMultiplier(Average with { PlayingHurt = true }) > baseline);
        Assert.True(InjuryRules.RiskMultiplier(Average with { Strength = 90, Stamina = 90 }) < baseline);
        Assert.True(InjuryRules.RiskMultiplier(Average with { PhysioAbility = 95 }) < baseline);
    }

    [Fact]
    public void Match_and_training_chances_follow_minutes_intensity_and_frequency()
    {
        var rules = InjuryRules.Default;
        Assert.Equal(0, rules.MatchChance(0, Average));
        Assert.True(rules.MatchChance(36, Average) > rules.MatchChance(12, Average));
        Assert.InRange(rules.MatchChance(30, Average), 0.02, 0.05);

        var light = rules.TrainingChance(TrainingIntensity.Light, false, Average);
        var normal = rules.TrainingChance(TrainingIntensity.Normal, false, Average);
        var hard = rules.TrainingChance(TrainingIntensity.Hard, false, Average);
        Assert.True(light < normal && normal < hard);
        Assert.Equal(light, rules.TrainingChance(TrainingIntensity.Hard, true, Average), 6);

        var off = new InjuryRules { Frequency = 0 };
        Assert.Equal(0, off.MatchChance(40, Average));
        Assert.Equal(0, off.TrainingChance(TrainingIntensity.Hard, false, Average));
    }

    [Fact]
    public void Proneness_is_stable_per_player_and_bounded()
    {
        var id = PlayerId.New();
        Assert.Equal(InjuryRules.Proneness(id), InjuryRules.Proneness(id));
        Assert.All(Enumerable.Range(0, 200).Select(_ => InjuryRules.Proneness(PlayerId.New())), p => Assert.InRange(p, 0.7, 1.3));
    }

    [Fact]
    public void Injury_types_follow_frequency_with_recurrence_and_fewer_contact_injuries_in_training()
    {
        var random = new Random(7);
        int Count(InjurySource source, IReadOnlySet<BodyPart> recent, Func<InjuryDefinition, bool> what) =>
            Enumerable.Range(0, 20_000).Count(_ => what(InjuryRules.ChooseType(source, recent, random)));

        var knees = Count(InjurySource.Match, new HashSet<BodyPart>(), d => d.BodyPart == BodyPart.Knee);
        var kneesAgain = Count(InjurySource.Match, new HashSet<BodyPart> { BodyPart.Knee }, d => d.BodyPart == BodyPart.Knee);
        Assert.True(kneesAgain > knees * 1.5, $"{kneesAgain} vs {knees}");

        var contactInMatches = Count(InjurySource.Match, new HashSet<BodyPart>(), d => d.Contact);
        var contactInTraining = Count(InjurySource.Training, new HashSet<BodyPart>(), d => d.Contact);
        Assert.True(contactInTraining < contactInMatches * 0.7, $"{contactInTraining} vs {contactInMatches}");
        Assert.Equal(InjuryType.AnkleSprain, Enumerable.Range(0, 2_000).Select(_ => InjuryRules.ChooseType(InjurySource.Match, new HashSet<BodyPart>(), random).Type)
            .GroupBy(t => t).MaxBy(g => g.Count())!.Key);
    }

    [Fact]
    public void Recovery_days_stay_in_the_range_and_recurrences_heal_slower()
    {
        var random = new Random(3);
        foreach (var definition in InjuryCatalog.All)
        {
            Assert.All(Enumerable.Range(0, 50).Select(_ => InjuryRules.Default.RecoveryDays(definition, false, random)), d => Assert.InRange(d, definition.MinDays, definition.MaxDays));
        }

        var sprain = InjuryCatalog.Of(InjuryType.AnkleSprain);
        double Mean(bool recurrence) => Enumerable.Range(0, 2_000).Average(_ => InjuryRules.Default.RecoveryDays(sprain, recurrence, random));
        Assert.True(Mean(true) > Mean(false) * 1.15);
    }

    [Theory]
    [InlineData(5, InjurySeverity.Minor)]
    [InlineData(14, InjurySeverity.Moderate)]
    [InlineData(40, InjurySeverity.Serious)]
    [InlineData(240, InjurySeverity.Severe)]
    public void Severity_comes_from_the_recovery_time(int days, InjurySeverity expected) =>
        Assert.Equal(expected, InjuryCatalog.SeverityFor(days));

    [Fact]
    public void An_injury_goes_through_treatment_and_rehabilitation_to_recovery()
    {
        var injury = new Injury(InjuryId.New(), PlayerId.New(), null, InjuryType.HamstringStrain, InjurySource.Match, Today, 20, false);
        Assert.Equal((InjuryPhase.Treatment, InjuryStatus.Injured, TrainingRestriction.NoTraining), (injury.Phase, injury.Status, injury.Restriction));

        for (var day = 1; day <= 14; day++)
        {
            injury.Recover(Today.AddDays(day), 1);
        }

        Assert.Equal((InjuryPhase.Rehabilitation, InjuryStatus.Injured, TrainingRestriction.LightOnly), (injury.Phase, injury.Status, injury.Restriction));
        for (var day = 15; day <= 20; day++)
        {
            injury.Recover(Today.AddDays(day), 1);
        }

        Assert.Equal((InjuryPhase.Recovered, InjuryStatus.Healthy, Today.AddDays(21)), (injury.Phase, injury.Status, injury.RecoveredOn));
        Assert.Equal(21, injury.DaysOut(Today.AddDays(40)));
        Assert.Throws<DomainValidationException>(() => injury.Recover(Today.AddDays(21), 1));
    }

    [Fact]
    public void Minor_injuries_leave_the_player_doubtful_and_playing_on_them_makes_them_worse()
    {
        var injury = new Injury(InjuryId.New(), PlayerId.New(), null, InjuryType.AnkleSprain, InjurySource.Training, Today, 5, false);
        Assert.Equal((InjurySeverity.Minor, InjuryStatus.DayToDay, TrainingRestriction.LightOnly), (injury.Severity, injury.Status, injury.Restriction));

        injury.Aggravate(6);
        Assert.Equal((11, InjurySeverity.Moderate, InjuryStatus.Injured, 1), (injury.RecoveryDays, injury.Severity, injury.Status, injury.Aggravations));
    }

    [Fact]
    public void Better_physios_heal_faster_and_estimate_better()
    {
        Assert.True(InjuryRules.RecoverySpeed(90, 25) > InjuryRules.RecoverySpeed(30, 25));
        Assert.True(InjuryRules.RecoverySpeed(50, 34) < InjuryRules.RecoverySpeed(50, 25));

        double MeanError(int ability)
        {
            var random = new Random(11);
            var speed = InjuryRules.RecoverySpeed(ability, 25);
            var truth = 30 / speed;
            return Enumerable.Range(0, 2_000).Average(_ => Math.Abs(InjuryRules.EstimateReturn(Today, 30, speed, ability, random).DayNumber - Today.DayNumber - truth));
        }

        Assert.True(MeanError(95) < MeanError(25) / 2);
        Assert.True(InjuryRules.EstimateReturn(Today, 0.2, 1, 50, new Random(1)) > Today);
    }

    [Fact]
    public void Players_return_from_long_injuries_short_of_fitness()
    {
        Assert.Equal(97, InjuryRules.ReturnFitness(5));
        Assert.Equal(64, InjuryRules.ReturnFitness(60));
        Assert.Equal(55, InjuryRules.ReturnFitness(250));
    }

    [Fact]
    public void Injury_settings_bind_and_are_validated()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ebm:Injuries:Frequency"] = "0.5", ["Ebm:Injuries:ReturnWindowDays"] = "14" })
            .Build();
        var options = configuration.GetSection(EbmOptions.SectionName).Get<EbmOptions>()!;
        Assert.Equal((0.5, 14, InjuryRules.Default.MatchRiskPerMinute), (options.Injuries.Frequency, options.Injuries.ReturnWindowDays, options.Injuries.MatchRiskPerMinute));
        Assert.Same(options.Injuries, options.Injuries.Validated());
        Assert.Throws<InvalidOperationException>(() => new InjuryRules { Frequency = 6 }.Validated());
        Assert.Throws<InvalidOperationException>(() => new InjuryRules { RecurrenceRecoveryFactor = 0.5 }.Validated());
    }

    [Fact]
    public void The_ai_coach_rests_hurt_players_and_avoids_hard_training_in_an_injury_crisis()
    {
        var italy = CountryId.New();
        Player Make(InjuryStatus status)
        {
            var player = new Player(PlayerId.New(), "Med", "Test", Today.AddYears(-21), italy, 200, 95, Position.SmallForward, PlayerAttributes.Uniform(55));
            player.UpdateCondition(100, 50, status, status == InjuryStatus.Healthy ? null : Today.AddDays(30));
            return player;
        }

        var hurt = Make(InjuryStatus.DayToDay);
        var injured = Make(InjuryStatus.Injured);
        List<Player> healthy = [.. Enumerable.Range(0, 8).Select(_ => Make(InjuryStatus.Healthy))];
        var squad = healthy.Append(hurt).Append(injured).Select(p => (p, PlayerRatingCalculator.Default.Evaluate(p))).ToList();
        var programme = TrainingPlanner.Plan(squad, Today);
        Assert.Equal(TrainingIntensity.Hard, programme.Intensity);
        Assert.Contains(programme.Individuals, i => i.PlayerId == hurt.Id && i.Focus == TrainingFocus.Recovery);
        Assert.DoesNotContain(programme.Individuals, i => i.PlayerId == injured.Id);

        var crisis = squad.Take(6).Concat(Enumerable.Range(0, 3).Select(_ => Make(InjuryStatus.Injured)).Select(p => (p, PlayerRatingCalculator.Default.Evaluate(p)))).ToList();
        Assert.Equal(TrainingIntensity.Normal, TrainingPlanner.Plan(crisis, Today).Intensity);
    }
}
