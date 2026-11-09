using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Services.Data;

namespace Ebm.Tests.Unit.Domain;

/// <summary>Edit operations used by the database editor and the importer keep the constructor rules.</summary>
public sealed class EditRulesTests
{
    private static readonly CountryId Italy = CountryId.New();

    [Fact]
    public void Player_details_can_be_updated_and_are_validated()
    {
        var player = new Player(PlayerId.New(), "Mario", "Rossi", new DateOnly(2000, 1, 1), Italy, 200, 95, Position.Center, TestData.Attributes());

        player.UpdateDetails("Luigi", "Bianchi", new DateOnly(1999, 2, 2), Italy, 190, 85, Position.PointGuard);

        Assert.Equal("Luigi Bianchi", player.FullName);
        Assert.Equal(Position.PointGuard, player.Position);
        var error = Assert.Throws<DomainValidationException>(() =>
            player.UpdateDetails("Luigi", "Bianchi", new DateOnly(1999, 2, 2), Italy, 300, 85, Position.PointGuard));
        Assert.Contains("Height", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_terms_can_be_updated_but_invalid_terms_change_nothing()
    {
        var start = TestData.Day;
        var contract = Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(), start, start.AddYears(1), 100_000);

        contract.UpdateTerms(start, start.AddYears(3), 250_000);
        Assert.Throws<DomainValidationException>(() => contract.UpdateTerms(start, start.AddDays(-1), 1));
        Assert.Throws<DomainValidationException>(() => contract.UpdateTerms(start, start.AddYears(5), -1));

        Assert.Equal(start.AddYears(3), contract.EndDate);
        Assert.Equal(250_000, contract.AnnualSalary);
    }

    [Fact]
    public void Club_league_division_and_competition_details_are_validated()
    {
        var club = new Club(ClubId.New(), "Old", "OLD", Italy);
        club.UpdateDetails("New name", "NEW", Italy, null, 1950);
        Assert.Equal("New name", club.Name);
        Assert.Throws<DomainValidationException>(() => club.UpdateDetails("New", "NEW", Italy, null, 1700));

        var league = new League(LeagueId.New(), "League", "LG", Italy);
        league.UpdateDetails("International", "INT", null);
        Assert.Null(league.CountryId);

        var division = new Division(DivisionId.New(), league.Id, "First", 1);
        Assert.Throws<DomainValidationException>(() => division.UpdateDetails("First", 0));

        var competition = new Competition(CompetitionId.New(), "Cup", "CUP", CompetitionType.Cup, Italy, null);
        competition.UpdateDetails("Supercup", "SUP", CompetitionType.Tournament, null, null);
        Assert.Equal(CompetitionType.Tournament, competition.Type);
    }

    [Theory]
    [InlineData("ita", "ITA")]
    [InlineData(" esp ", "ESP")]
    public void Country_codes_are_normalised(string code, string expected) =>
        Assert.Equal(expected, Country.NormaliseCode(code));

    [Theory]
    [InlineData("IT")]
    [InlineData("I1A")]
    public void Invalid_country_codes_are_rejected(string code) =>
        Assert.Throws<DomainValidationException>(() => Country.NormaliseCode(code));

    [Fact]
    public void Duplicate_keys_ignore_case_and_extra_spaces()
    {
        var dob = new DateOnly(2000, 5, 10);
        var a = new Player(PlayerId.New(), "Mario", "De  Rossi", dob, Italy, 200, 95, Position.Center, TestData.Attributes());
        var b = new Player(PlayerId.New(), " mario ", "de rossi", dob, CountryId.New(), 190, 90, Position.PointGuard, TestData.Attributes());
        var c = new Player(PlayerId.New(), "Mario", "De Rossi", dob.AddDays(1), Italy, 200, 95, Position.Center, TestData.Attributes());

        Assert.Equal(DuplicateKeys.Of(a), DuplicateKeys.Of(b));
        Assert.NotEqual(DuplicateKeys.Of(a), DuplicateKeys.Of(c));
        Assert.NotEqual(
            DuplicateKeys.Of(new Club(ClubId.New(), "Aurora", "AUR", Italy)),
            DuplicateKeys.Of(new Club(ClubId.New(), "Aurora", "AUR", CountryId.New())));
    }
}
