using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Services.Data;

namespace Ebm.Tests.Unit.Players;

/// <summary>Step 16: nationalities, data status and estimated attributes (docs/PLAYER_DATABASE.md).</summary>
public sealed class PlayerDataQualityTests
{
    private static readonly CountryId Usa = CountryId.New();
    private static readonly CountryId Italy = CountryId.New();

    private static Player NewPlayer(string first = "Ana", string last = "Test") =>
        new(PlayerId.New(), first, last, new DateOnly(2000, 1, 1), Usa, 200, 95, Position.SmallForward, PlayerAttributes.Uniform(50));

    [Fact]
    public void Second_nationality_counts_for_eu_status_and_never_equals_the_nationality()
    {
        var player = NewPlayer();
        Assert.False(player.HasNationality(c => c == Italy));

        player.UpdateSecondNationality(Italy);
        Assert.True(player.HasNationality(c => c == Italy));
        Assert.Equal([Usa, Italy], player.Nationalities);
        Assert.Throws<DomainValidationException>(() => player.UpdateSecondNationality(Usa));

        player.UpdateDetails("Ana", "Test", new DateOnly(2000, 1, 1), Italy, 200, 95, Position.SmallForward);
        Assert.Null(player.SecondNationalityId);
    }

    [Fact]
    public void Verified_data_needs_a_source_and_new_records_are_generated()
    {
        var player = NewPlayer();
        Assert.Equal((DataStatus.Generated, DataStatus.Generated), (player.ProfileStatus, player.AttributesStatus));
        Assert.Throws<DomainValidationException>(() => player.UpdateDataQuality(DataStatus.Verified, DataStatus.Estimated, " "));

        player.UpdateDataQuality(DataStatus.Verified, DataStatus.Estimated, " Federation website ");
        Assert.Equal("Federation website", player.Source);

        var contract = Contract.ForPlayer(ContractId.New(), ClubId.New(), player.Id, new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30), 100_000);
        Assert.Equal(DataStatus.Generated, contract.TermsStatus);
        Assert.Throws<DomainValidationException>(() => contract.UpdateDataQuality(DataStatus.Verified, null));
    }

    [Fact]
    public void Missing_attributes_are_estimated_from_the_known_ones_or_the_reputation()
    {
        var known = new Dictionary<PlayerAttribute, int> { [PlayerAttribute.ThreePoint] = 80, [PlayerAttribute.Passing] = 70, [PlayerAttribute.Speed] = 60 };
        var partial = AttributeEstimator.Complete(known, Position.Center, reputation: 1);
        Assert.Equal(80, partial[PlayerAttribute.ThreePoint]);
        Assert.Equal(80, partial[PlayerAttribute.DefensiveRebound]); // average 70, center rebounding +10
        Assert.Equal(60, partial[PlayerAttribute.MidRange]); // center shooting -10

        var unknown = AttributeEstimator.Complete(new Dictionary<PlayerAttribute, int>(), Position.SmallForward, reputation: 100);
        Assert.All(PlayerCatalog.Attributes, a => Assert.Equal(AttributeEstimator.QualityFromReputation(100), unknown[a]));
        Assert.Equal(AttributeEstimator.Complete(known, Position.Center, 1).ToDictionary(), partial.ToDictionary());
    }

    [Theory]
    [InlineData("Đorđević", "Djordjevic")]
    [InlineData("Šarūnas", "SARUNAS")]
    [InlineData("Łukasz", "Lukasz")]
    [InlineData("Søren  Müller", "soren muller")]
    public void Duplicate_keys_ignore_accents_across_leagues(string spelling, string other) =>
        Assert.Equal(DuplicateKeys.Player("Ana", spelling, new DateOnly(2000, 1, 1)), DuplicateKeys.Player("Ana", other, new DateOnly(2000, 1, 1)));
}
