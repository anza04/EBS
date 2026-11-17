using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Scouting;
using Ebm.Domain.Staff;

namespace Ebm.Tests.Unit.Scouting;

/// <summary>Step 20: accuracy, margins, estimates and assignments (docs/SCOUTING.md).</summary>
public sealed class ScoutingRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static Player Prospect(int value = 60) =>
        new(PlayerId.New(), "Test", "Prospect", new DateOnly(2004, 3, 1), CountryId.New(), 200, 95, Position.SmallForward, PlayerAttributes.Uniform(value).With(PlayerAttribute.ThreePoint, value + 15));

    [Fact]
    public void Accuracy_grows_with_scout_ability_study_time_and_visibility()
    {
        var baseline = ScoutingRules.Accuracy(50, 14, 50);
        Assert.True(ScoutingRules.Accuracy(90, 14, 50) > baseline);
        Assert.True(ScoutingRules.Accuracy(50, 28, 50) > baseline);
        Assert.True(ScoutingRules.Accuracy(50, 14, 90) > baseline);
        Assert.Equal(ScoutingRules.Accuracy(50, ScoutingRules.FullStudyDays, 50), ScoutingRules.Accuracy(50, 200, 50)); // time is capped
        Assert.Equal(ScoutingRules.MaxAccuracy, ScoutingRules.Accuracy(100, 100, 100));
        Assert.Equal(ScoutingRules.MinAccuracy, ScoutingRules.Accuracy(1, 0, 1));
    }

    [Fact]
    public void Visibility_rewards_reputation_home_country_and_international_play_and_penalises_youth()
    {
        Assert.Equal(40, ScoutingRules.Visibility(40, false, false, 25));
        Assert.Equal(75, ScoutingRules.Visibility(40, true, true, 25));
        Assert.Equal(25, ScoutingRules.Visibility(40, false, false, 19));
        Assert.Equal(1, ScoutingRules.Visibility(5, false, false, 18));
    }

    [Fact]
    public void Margins_shrink_as_accuracy_grows_and_repeated_reports_improve()
    {
        Assert.Equal(24, ScoutingRules.Margin(ScoutingRules.MinAccuracy));
        Assert.Equal(1, ScoutingRules.Margin(ScoutingRules.MaxAccuracy));
        Assert.True(ScoutingRules.PotentialMargin(60) > ScoutingRules.Margin(60));
        Assert.Equal(65, ScoutingRules.Combine(40, 60));
        Assert.Equal(70, ScoutingRules.Combine(70, 60));
        Assert.Equal(40, ScoutingRules.Combine(40, null));
        Assert.Equal(ScoutingRules.MaxAccuracy, ScoutingRules.Combine(40, 97));
    }

    [Fact]
    public void Estimates_stay_within_the_margin_and_are_deterministic()
    {
        var player = Prospect();
        var truth = PlayerRatingCalculator.Default.Evaluate(player);
        foreach (var accuracy in new[] { 5, 30, 60, 90 })
        {
            for (var seed = 0; seed < 50; seed++)
            {
                var estimate = ScoutingEstimator.Default.Estimate(player, accuracy, seed);
                Assert.All(PlayerCatalog.Attributes, a => Assert.InRange(estimate.Attributes[a] - player.Attributes[a], -estimate.Margin, estimate.Margin));
                Assert.InRange(estimate.Overall - truth.Overall, -estimate.Margin, estimate.Margin);
                Assert.InRange(estimate.Potential - truth.PotentialRating, -estimate.PotentialMargin, estimate.PotentialMargin);
                Assert.True(estimate.Potential >= estimate.Overall);
            }
        }

        Assert.Equal(ScoutingEstimator.Default.Estimate(player, 40, 7).Attributes.ToDictionary(), ScoutingEstimator.Default.Estimate(player, 40, 7).Attributes.ToDictionary());
    }

    [Fact]
    public void Better_accuracy_gives_smaller_errors_and_poor_scouting_differs_from_the_truth()
    {
        var player = Prospect();
        double MeanError(int accuracy) => Enumerable.Range(0, 200)
            .Select(seed => ScoutingEstimator.Default.Estimate(player, accuracy, seed))
            .Average(e => PlayerCatalog.Attributes.Average(a => Math.Abs(e.Attributes[a] - player.Attributes[a])));

        Assert.True(MeanError(90) < MeanError(50));
        Assert.True(MeanError(50) < MeanError(10));
        Assert.Contains(PlayerCatalog.Attributes, a => ScoutingEstimator.Default.Estimate(player, 10, 1).Attributes[a] != player.Attributes[a]);
    }

    [Fact]
    public void Stable_seeds_do_not_depend_on_the_process()
    {
        var a = Guid.Parse("0196f5e4-0000-7000-8000-000000000001");
        var b = Guid.Parse("0196f5e4-0000-7000-8000-000000000002");
        Assert.Equal(ScoutingRules.StableSeed(a, b, 3), ScoutingRules.StableSeed(a, b, 3));
        Assert.NotEqual(ScoutingRules.StableSeed(a, b, 3), ScoutingRules.StableSeed(b, a, 3));
        Assert.NotEqual(ScoutingRules.StableSeed(a, b, 3), ScoutingRules.StableSeed(a, b, 4));
    }

    [Fact]
    public void Assignments_need_their_target_and_last_their_focus_duration()
    {
        var club = ClubId.New();
        var scout = StaffMemberId.New();
        Assert.Throws<DomainValidationException>(() => ScoutingAssignment.ForGroup(ScoutingAssignmentId.New(), club, scout, ScoutingFocus.Region, Today));
        Assert.Throws<DomainValidationException>(() => ScoutingAssignment.ForGroup(ScoutingAssignmentId.New(), club, scout, ScoutingFocus.League, Today));
        Assert.Throws<DomainValidationException>(() => ScoutingAssignment.ForGroup(ScoutingAssignmentId.New(), club, scout, ScoutingFocus.Position, Today));
        Assert.Throws<DomainValidationException>(() => ScoutingAssignment.ForGroup(ScoutingAssignmentId.New(), club, scout, ScoutingFocus.Player, Today));
        Assert.Throws<DomainValidationException>(() => ScoutingAssignment.ForGroup(ScoutingAssignmentId.New(), club, scout, ScoutingFocus.Position, Today, position: Position.Center, maxAge: 99));

        var youth = ScoutingAssignment.ForGroup(ScoutingAssignmentId.New(), club, scout, ScoutingFocus.Youth, Today);
        Assert.Equal((ScoutingRules.YouthMaxAge, Today.AddDays(28)), (youth.MaxAge, youth.DueDate));
        var player = ScoutingAssignment.ForPlayer(ScoutingAssignmentId.New(), club, scout, Today, PlayerId.New());
        Assert.Equal(7, player.Days);
        Assert.Equal(28, ScoutingRules.StudyDays(ScoutingFocus.Player, player.Days));
        Assert.False(player.IsDueOn(Today.AddDays(6)));
        Assert.True(player.IsDueOn(Today.AddDays(7)));

        player.Complete(Today.AddDays(7));
        Assert.Equal((AssignmentStatus.Completed, (DateOnly?)Today.AddDays(7)), (player.Status, player.EndedOn));
        Assert.Throws<DomainValidationException>(() => player.Cancel(Today.AddDays(8)));
        Assert.False(player.IsDueOn(Today.AddDays(8)));
    }

    [Fact]
    public void Scout_numbers_and_ability_follow_club_reputation()
    {
        Assert.Equal((1, 2, 3), (ScoutingRules.ScoutsFor(10), ScoutingRules.ScoutsFor(50), ScoutingRules.ScoutsFor(90)));
        Assert.True(ScoutingRules.TypicalAbility(90) > ScoutingRules.TypicalAbility(20));
        Assert.Throws<DomainValidationException>(() => new StaffMember(StaffMemberId.New(), "A", "B", new DateOnly(1970, 1, 1), CountryId.New(), StaffRole.Scout, 0));
    }
}
