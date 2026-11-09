using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Domain.Staff;
using static Ebm.Tests.Unit.Domain.TestData;

namespace Ebm.Tests.Unit.Domain;

/// <summary>Construction rules of the Step 02 entities.</summary>
public sealed class EntityRulesTests
{
    [Fact]
    public void Entity_rejects_empty_id()
    {
        Assert.Throws<DomainValidationException>(() => new Country(default, "ITA", "Italy"));
    }

    [Fact]
    public void New_ids_are_unique()
    {
        Assert.NotEqual(PlayerId.New(), PlayerId.New());
    }

    [Theory]
    [InlineData("ita", "ITA")]
    [InlineData(" esp ", "ESP")]
    public void Country_code_is_normalised(string code, string expected)
    {
        Assert.Equal(expected, new Country(CountryId.New(), code, "Name").Code);
    }

    [Theory]
    [InlineData("IT")]
    [InlineData("IT1")]
    [InlineData("ITAL")]
    [InlineData("")]
    public void Country_code_must_be_three_letters(string code)
    {
        Assert.Throws<DomainValidationException>(() => new Country(CountryId.New(), code, "Italy"));
    }

    [Fact]
    public void Names_are_trimmed_and_length_checked()
    {
        Assert.Equal("Italy", new Country(CountryId.New(), "ITA", "  Italy ").Name);
        Assert.Throws<DomainValidationException>(() => new Country(CountryId.New(), "ITA", new string('x', Country.NameMaxLength + 1)));
        Assert.Throws<DomainValidationException>(() => new Club(ClubId.New(), " ", "X", CountryId.New()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Division.MaxLevel + 1)]
    public void Division_level_is_bounded(int level)
    {
        Assert.Throws<DomainValidationException>(() => new Division(DivisionId.New(), LeagueId.New(), "Serie A", level));
    }

    [Fact]
    public void Division_requires_league()
    {
        Assert.Throws<DomainValidationException>(() => new Division(DivisionId.New(), default, "Serie A", 1));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("""{"rounds":4}""")]
    [InlineData("""{"pointsForWin":"two"}""")]
    public void Competition_rules_must_be_a_json_object(string rules)
    {
        Assert.Throws<DomainValidationException>(() =>
            new Competition(CompetitionId.New(), "Serie A", "LBA", CompetitionType.League, null, null, rules));
    }

    [Fact]
    public void Competition_rules_are_stored_and_updatable()
    {
        var competition = new Competition(CompetitionId.New(), "Serie A", "LBA", CompetitionType.League, CountryId.New(), DivisionId.New());
        Assert.Equal(Competition.EmptyRules, competition.RulesJson);

        competition.UpdateRules("""{"pointsForWin":2}""");

        Assert.Equal("""{"pointsForWin":2}""", competition.RulesJson);
    }

    [Fact]
    public void Season_must_end_after_it_starts()
    {
        Assert.Throws<DomainValidationException>(() => new Season(SeasonId.New(), "2026-27", Day, Day));
    }

    [Theory]
    [InlineData(Player.MinHeightCm - 1, 90)]
    [InlineData(Player.MaxHeightCm + 1, 90)]
    [InlineData(200, Player.MinWeightKg - 1)]
    public void Player_body_measurements_are_bounded(int height, int weight)
    {
        Assert.Throws<DomainValidationException>(() =>
            new Player(PlayerId.New(), "A", "B", Day, CountryId.New(), height, weight, Position.Center, Attributes()));
    }

    [Theory]
    [InlineData(PlayerAttributes.MinRating - 1)]
    [InlineData(PlayerAttributes.MaxRating + 1)]
    public void Player_attributes_are_bounded(int value)
    {
        Assert.Throws<DomainValidationException>(() => Attributes(value));
    }

    [Fact]
    public void Player_rejects_undefined_position()
    {
        Assert.Throws<DomainValidationException>(() =>
            new Player(PlayerId.New(), "A", "B", Day, CountryId.New(), 200, 95, (Position)99, Attributes()));
    }

    [Fact]
    public void Club_history_entry_closes_once_and_not_before_start()
    {
        var entry = new PlayerClubHistoryEntry(PlayerClubHistoryEntryId.New(), PlayerId.New(), ClubId.New(), Day);
        Assert.True(entry.IsCurrent);

        Assert.Throws<DomainValidationException>(() => entry.Close(Day.AddDays(-1)));
        entry.Close(Day.AddDays(30));

        Assert.False(entry.IsCurrent);
        Assert.Equal(Day.AddDays(30), entry.EndDate);
        Assert.Throws<DomainValidationException>(() => entry.Close(Day.AddDays(60)));
    }

    [Fact]
    public void Contract_belongs_to_exactly_one_person()
    {
        var player = Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(), Day, Day.AddYears(2), 250_000);
        var staff = Contract.ForStaff(ContractId.New(), ClubId.New(), StaffMemberId.New(), Day, Day.AddYears(1), 90_000);

        Assert.NotNull(player.PlayerId);
        Assert.Null(player.StaffMemberId);
        Assert.Null(staff.PlayerId);
        Assert.NotNull(staff.StaffMemberId);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(365, -1)]
    [InlineData(365, Contract.MaxAnnualSalary + 1)]
    public void Contract_dates_and_salary_are_validated(int days, long salary)
    {
        Assert.Throws<DomainValidationException>(() =>
            Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(), Day, Day.AddDays(days), salary));
    }

    [Fact]
    public void Fixture_requires_two_different_clubs()
    {
        var club = ClubId.New();
        Assert.Throws<DomainValidationException>(() =>
            new Fixture(FixtureId.New(), CompetitionId.New(), SeasonId.New(), 1, Day, club, club));
    }

    [Fact]
    public void Match_cannot_end_in_a_draw()
    {
        Assert.Throws<DomainValidationException>(() => new Match(MatchId.New(), FixtureId.New(), 80, 80));
        Assert.True(new Match(MatchId.New(), FixtureId.New(), 81, 80, overtimes: 1).HomeWon);
    }

    [Fact]
    public void Staff_member_requires_known_role()
    {
        Assert.Throws<DomainValidationException>(() =>
            new StaffMember(StaffMemberId.New(), "A", "B", Day, CountryId.New(), (StaffRole)99));
    }
}
