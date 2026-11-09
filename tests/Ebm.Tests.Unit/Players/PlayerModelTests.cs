using Ebm.Domain.Common;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;

namespace Ebm.Tests.Unit.Players;

public sealed class PlayerModelTests
{
    private static Player NewPlayer(Position position = Position.SmallForward, int height = 200) =>
        new(PlayerId.New(), "Luca", "Test", new DateOnly(2000, 6, 15), CountryId.New(), height, 95, position, PlayerAttributes.Uniform(60));

    [Fact]
    public void Catalogue_has_the_specified_attributes_per_category()
    {
        var counts = PlayerCatalog.Categories.ToDictionary(c => c, c => PlayerCatalog.AttributesOf(c).Count());

        Assert.Equal(27, PlayerCatalog.Attributes.Count);
        Assert.Equal([4, 3, 4, 5, 2, 5, 4], PlayerCatalog.Categories.Select(c => counts[c]));
        Assert.Equal(AttributeCategory.Defense, PlayerCatalog.CategoryOf(PlayerAttribute.DefensivePositioning));
        Assert.Equal(AttributeCategory.Physical, PlayerCatalog.CategoryOf(PlayerAttribute.Stamina));
        Assert.Equal(AttributeCategory.Mental, PlayerCatalog.CategoryOf(PlayerAttribute.WorkRate));
        Assert.Equal(8, PlayerCatalog.Tendencies.Count);
    }

    [Fact]
    public void Catalogue_names_fields_and_labels()
    {
        Assert.Equal("threePoint", PlayerCatalog.FieldName(PlayerAttribute.ThreePoint));
        Assert.Equal("Three point", PlayerCatalog.DisplayName(PlayerAttribute.ThreePoint));
        Assert.Equal("Crash offensive glass", PlayerCatalog.DisplayName(PlayerTendency.CrashOffensiveGlass));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Attribute_outside_1_to_100_is_refused(int value)
    {
        var ex = Assert.Throws<DomainValidationException>(() => PlayerAttributes.Uniform(50).With(PlayerAttribute.Dunk, value));
        Assert.Contains("Dunk", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Attribute_set_must_be_complete()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new PlayerAttributes(new Dictionary<PlayerAttribute, int> { [PlayerAttribute.Speed] = 50 }));
        Assert.Contains("Missing ratings", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Indexer_properties_and_dictionary_agree()
    {
        var attributes = PlayerAttributes.Uniform(40).With(PlayerAttribute.Block, 91).With(PlayerAttribute.WorkRate, 12);

        Assert.Equal(91, attributes.Block);
        Assert.Equal(91, attributes[PlayerAttribute.Block]);
        Assert.Equal(12, attributes.WorkRate);
        Assert.Equal(PlayerCatalog.Attributes, attributes.ToDictionary().Keys);
        Assert.Equal(40, attributes.ToDictionary()[PlayerAttribute.CloseShot]);
    }

    [Fact]
    public void Suggested_tendencies_are_weighted_means_of_attributes()
    {
        var attributes = PlayerAttributes.Uniform(50).With(PlayerAttribute.ThreePoint, 90).With(PlayerAttribute.MidRange, 70).With(PlayerAttribute.Steal, 81);

        var tendencies = PlayerTendencies.Suggest(attributes);

        Assert.Equal(85, tendencies.ThreePointShot); // (3·90 + 70) / 4
        Assert.Equal(65, tendencies.MidRangeShot); // (3·70 + 50) / 4
        Assert.Equal(71, tendencies.DefensiveGambling); // (2·81 + 50) / 3 = 70.67
        Assert.Equal(57, tendencies.Isolation); // (2·50 + 70) / 3 = 56.67
        Assert.All(PlayerTendencies.Suggest(PlayerAttributes.Uniform(33)).ToDictionary().Values, v => Assert.Equal(33, v));
    }

    [Fact]
    public void New_player_gets_documented_defaults()
    {
        var player = NewPlayer();

        Assert.Equal(200, player.WingspanCm);
        Assert.True(player.SecondaryPositions.IsEmpty);
        Assert.Equal((Player.DefaultExperience, Player.DefaultReputation, Player.DefaultPotential), (player.Experience, player.Reputation, player.Potential));
        Assert.Equal((Player.DefaultFitness, Player.DefaultMorale, InjuryStatus.Healthy), (player.Fitness, player.Morale, player.InjuryStatus));
        Assert.Null(player.InjuryReturnDate);
        Assert.Equal(PlayerTendencies.Suggest(player.Attributes).ToDictionary(), player.Tendencies.ToDictionary());
    }

    [Theory]
    [InlineData(169, 1, 1, 1, "Wingspan")]
    [InlineData(231, 1, 1, 1, "Wingspan")]
    [InlineData(200, 1, 1, 1, null)]
    [InlineData(200, 101, 1, 1, "Experience")]
    [InlineData(200, 50, 0, 1, "Reputation")]
    [InlineData(200, 50, 50, 101, "Potential")]
    public void Profile_values_are_range_checked(int wingspan, int experience, int reputation, int potential, string? error)
    {
        var player = NewPlayer();

        var ex = Record.Exception(() => player.UpdateProfile(wingspan, PositionSet.Empty, experience, reputation, potential));

        if (error is null)
        {
            Assert.Null(ex);
        }
        else
        {
            Assert.Contains(error, Assert.IsType<DomainValidationException>(ex).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Primary_position_is_never_a_secondary_position()
    {
        var player = NewPlayer(Position.SmallForward);
        Assert.Throws<DomainValidationException>(() => player.UpdateProfile(200, PositionSet.Of(Position.SmallForward), 1, 1, 50));

        player.UpdateProfile(200, PositionSet.Of(Position.ShootingGuard, Position.PowerForward), 1, 1, 50);
        player.UpdateDetails(player.FirstName, player.LastName, player.DateOfBirth, player.NationalityId, 200, 95, Position.PowerForward);

        Assert.Equal([Position.ShootingGuard], player.SecondaryPositions.Positions);
    }

    [Fact]
    public void Injury_status_and_return_date_must_match()
    {
        var player = NewPlayer();
        var back = new DateOnly(2026, 12, 1);

        Assert.Throws<DomainValidationException>(() => player.UpdateCondition(90, 50, InjuryStatus.Healthy, back));
        Assert.Throws<DomainValidationException>(() => player.UpdateCondition(90, 50, InjuryStatus.Injured, null));
        Assert.Throws<DomainValidationException>(() => player.UpdateCondition(101, 50, InjuryStatus.Healthy, null));
        Assert.Throws<DomainValidationException>(() => player.UpdateCondition(90, 0, InjuryStatus.Healthy, null));
        player.UpdateCondition(0, 100, InjuryStatus.DayToDay, null);
        player.UpdateCondition(70, 40, InjuryStatus.Injured, back);

        Assert.Equal((70, 40, InjuryStatus.Injured, (DateOnly?)back), (player.Fitness, player.Morale, player.InjuryStatus, player.InjuryReturnDate));
    }

    [Fact]
    public void Age_counts_whole_years()
    {
        var player = NewPlayer();

        Assert.Equal(25, player.AgeOn(new DateOnly(2026, 6, 14)));
        Assert.Equal(26, player.AgeOn(new DateOnly(2026, 6, 15)));
    }

    [Fact]
    public void Position_set_round_trips_through_text()
    {
        var set = PositionSet.Of(Position.Center, Position.PointGuard);

        Assert.Equal("PointGuard,Center", set.ToString());
        Assert.Equal(set, PositionSet.Parse("center, pointguard"));
        Assert.Equal(PositionSet.Empty, PositionSet.Parse(""));
        Assert.Throws<DomainValidationException>(() => PositionSet.Parse("Wing"));
        Assert.Throws<DomainValidationException>(() => PositionSet.Of((Position)9));
    }
}
