using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;

namespace Ebm.Tests.Unit.Rosters;

public sealed class RosterRulesTests
{
    private static readonly Position[] Positions = Enum.GetValues<Position>();

    /// <summary>A valid roster: five starters at different positions, five rotation players, two bench, two inactive, one youth.</summary>
    private static List<RosterMember> CompleteRoster()
    {
        var members = new List<RosterMember>();
        for (var i = 0; i < 15; i++)
        {
            var role = i switch { < 5 => RosterRole.Starter, < 10 => RosterRole.Rotation, < 12 => RosterRole.Bench, < 14 => RosterRole.Inactive, _ => RosterRole.Youth };
            members.Add(Member(i, role, role == RosterRole.Starter ? Positions[i] : null));
        }

        return members;
    }

    private static RosterMember Member(int i, RosterRole role, Position? position = null, PlayerAvailability availability = PlayerAvailability.Available, bool foreign = false, int age = 25) =>
        new(PlayerId.From(new Guid(i + 1, 0, 0, new byte[8])), $"Player {i}", role, position, availability, foreign, age,
            position is null ? null : PositionFamiliarity.Primary, HasContract: true);

    private static List<RosterMember> Replace(List<RosterMember> roster, int index, Func<RosterMember, RosterMember> change)
    {
        roster[index] = change(roster[index]);
        return roster;
    }

    [Fact]
    public void A_complete_roster_is_valid_without_warnings()
    {
        var result = RosterValidator.Validate(CompleteRoster(), SquadRules.Default);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Four_starters_or_a_shared_lineup_position_are_errors()
    {
        var four = Replace(CompleteRoster(), 4, m => m with { Role = RosterRole.Bench, LineupPosition = null, LineupFamiliarity = null });
        var shared = Replace(CompleteRoster(), 4, m => m with { LineupPosition = Position.PointGuard });

        Assert.True(RosterValidator.Validate(four, SquadRules.Default).Has(RosterIssueCode.StartingFiveIncomplete));
        var sharedResult = RosterValidator.Validate(shared, SquadRules.Default);
        Assert.False(sharedResult.IsValid);
        Assert.Contains(sharedResult.Errors, i => i.Code == RosterIssueCode.StartingPositionTaken && i.Message.Contains("Player 0 and Player 4", StringComparison.Ordinal));
    }

    [Fact]
    public void Injured_starter_is_an_error_and_injured_or_doubtful_reserves_are_warnings()
    {
        var injuredStarter = Replace(CompleteRoster(), 0, m => m with { Availability = PlayerAvailability.Unavailable });
        var result = RosterValidator.Validate(injuredStarter, SquadRules.Default);
        Assert.Contains(result.Errors, i => i.Code == RosterIssueCode.UnavailableStarter && i.PlayerId == injuredStarter[0].PlayerId);

        var other = CompleteRoster();
        other[1] = other[1] with { Availability = PlayerAvailability.Doubtful };
        other[5] = other[5] with { Availability = PlayerAvailability.Unavailable };
        var warnings = RosterValidator.Validate(other, SquadRules.Default);
        Assert.True(warnings.IsValid);
        Assert.Equal([RosterIssueCode.DoubtfulStarter, RosterIssueCode.UnavailableInSquad], warnings.Warnings.Select(w => w.Code));
    }

    [Fact]
    public void Squad_size_and_matchday_limits_are_enforced()
    {
        var small = CompleteRoster().Where(m => m.Role is not (RosterRole.Inactive or RosterRole.Bench)).ToList(); // 10 first team
        Assert.True(RosterValidator.Validate(small, SquadRules.Default).IsValid);
        Assert.True(RosterValidator.Validate(small[..9], SquadRules.Default).Has(RosterIssueCode.TooFewPlayers));

        var crowded = CompleteRoster().Select(m => m.Role == RosterRole.Inactive ? m with { Role = RosterRole.Bench } : m).ToList();
        Assert.True(RosterValidator.Validate(crowded, SquadRules.Default).Has(RosterIssueCode.MatchdaySquadTooLarge)); // 14 active > 12

        var big = CompleteRoster().Concat(Enumerable.Range(20, 5).Select(i => Member(i, RosterRole.Inactive))).ToList(); // 19 first team
        Assert.True(RosterValidator.Validate(big, SquadRules.Default).Has(RosterIssueCode.TooManyPlayers));
        var youth = big.Select(m => m.Role == RosterRole.Inactive && m.Name.EndsWith('2') ? m with { Role = RosterRole.Youth } : m).ToList();
        Assert.False(RosterValidator.Validate(youth, SquadRules.Default).Has(RosterIssueCode.TooManyPlayers)); // youth players do not count
    }

    [Fact]
    public void Competition_registration_rules_limit_foreign_domestic_and_young_players()
    {
        var roster = CompleteRoster().Select((m, i) => i < 6 ? m with { IsForeign = true } : m).ToList();
        roster[13] = roster[13] with { Age = 16 };
        var rules = new SquadRules { MaxForeignPlayers = 5, MinDomesticPlayers = 9, MinAge = 17 }.Validate();

        var result = RosterValidator.ValidateRegistration(roster, rules);

        Assert.Equal([RosterIssueCode.TooManyForeignPlayers, RosterIssueCode.TooFewDomesticPlayers, RosterIssueCode.UnderMinimumAge], result.Errors.Select(e => e.Code));
        Assert.True(RosterValidator.ValidateRegistration(roster, SquadRules.Default).IsValid);
    }

    [Fact]
    public void Unfamiliar_lineup_position_short_rotation_and_missing_contract_are_warnings()
    {
        var roster = CompleteRoster();
        roster[2] = roster[2] with { LineupFamiliarity = PositionFamiliarity.Unfamiliar };
        roster[9] = roster[9] with { Role = RosterRole.Bench };
        roster[10] = roster[10] with { HasContract = false };
        roster[11] = roster[11] with { Role = RosterRole.Inactive };

        var result = RosterValidator.Validate(roster, SquadRules.Default);

        Assert.True(result.IsValid);
        Assert.Equal([RosterIssueCode.UnfamiliarPosition, RosterIssueCode.NoContract], result.Warnings.Select(w => w.Code));
        roster[7] = roster[7] with { Role = RosterRole.Bench };
        roster[8] = roster[8] with { Role = RosterRole.Bench }; // 7 left in the starting five and rotation
        Assert.True(RosterValidator.Validate(roster, SquadRules.Default).Has(RosterIssueCode.ShortRotation));
    }

    [Fact]
    public void Roster_spot_requires_a_lineup_position_only_for_starters_and_an_order_only_for_rotation()
    {
        var player = PlayerId.New();
        var club = ClubId.New();
        var spot = new RosterSpot(RosterSpotId.New(), player, club, RosterRole.Starter, Position.Center);

        Assert.Throws<DomainValidationException>(() => spot.Assign(RosterRole.Starter, null, null));
        Assert.Throws<DomainValidationException>(() => spot.Assign(RosterRole.Bench, Position.Center, null));
        Assert.Throws<DomainValidationException>(() => spot.Assign(RosterRole.Rotation, null, null));
        Assert.Throws<DomainValidationException>(() => spot.Assign(RosterRole.Bench, null, 1));
        Assert.Throws<DomainValidationException>(() => spot.Assign(RosterRole.Rotation, null, 0));
        spot.Assign(RosterRole.Rotation, null, 3);
        Assert.Equal((RosterRole.Rotation, (Position?)null, (int?)3), (spot.Role, spot.LineupPosition, spot.RotationOrder));
        Assert.True(RosterRole.Inactive.IsFirstTeam());
        Assert.False(RosterRole.Youth.IsFirstTeam());
        Assert.False(RosterRole.Inactive.IsActive());
    }

    [Fact]
    public void Availability_follows_the_injury_status()
    {
        var player = new Player(PlayerId.New(), "Ann", "Test", new DateOnly(2000, 1, 1), CountryId.New(), 190, 85, Position.PointGuard, PlayerAttributes.Uniform(50));
        Assert.Equal(PlayerAvailability.Available, RosterRoles.AvailabilityOf(player));
        player.UpdateCondition(100, 50, InjuryStatus.DayToDay, null);
        Assert.Equal(PlayerAvailability.Doubtful, RosterRoles.AvailabilityOf(player));
        player.UpdateCondition(100, 50, InjuryStatus.Injured, new DateOnly(2026, 12, 1));
        Assert.Equal(PlayerAvailability.Unavailable, RosterRoles.AvailabilityOf(player));
    }

    [Fact]
    public void Competition_rules_read_and_validate_squad_registration_settings()
    {
        var rules = CompetitionRules.Parse("""{"registration":{"squad":{"maxPlayers":16,"maxForeignPlayers":6,"minAge":16}}}""");

        Assert.Equal((10, 16, 12, (int?)6, (int?)16), (rules.Registration.Squad.MinPlayers, rules.Registration.Squad.MaxPlayers, rules.Registration.Squad.MatchdayPlayers,
            rules.Registration.Squad.MaxForeignPlayers, rules.Registration.Squad.MinAge));
        Assert.Same(SquadRules.Default, CompetitionRules.Parse("{}").Registration.Squad);
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse("""{"registration":{"squad":{"maxPlayers":8}}}"""));
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse("""{"registration":{"squad":{"matchdayPlayers":20}}}"""));
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse("""{"registration":{"squad":{"homegrown":4}}}"""));
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse("""{"registration":{"squad":null}}"""));
    }

    [Fact]
    public void Club_profile_values_are_validated_and_default_for_new_clubs()
    {
        var club = new Club(ClubId.New(), "Test Club", "TST", CountryId.New());
        Assert.Equal(((string?)null, Club.DefaultReputation, 0L, 0L, BoardExpectation.MidTable), (club.City, club.Reputation, club.Balance, club.WageBudget, club.BoardExpectation));

        club.UpdateProfile("  Turin ", 80, -2_500_000, 9_000_000, BoardExpectation.ReachPlayoffs);
        Assert.Equal(("Turin", 80, -2_500_000L, 9_000_000L, BoardExpectation.ReachPlayoffs), (club.City, club.Reputation, club.Balance, club.WageBudget, club.BoardExpectation));
        club.UpdateProfile(" ", 80, 0, 0, BoardExpectation.MidTable);
        Assert.Null(club.City);

        Assert.Throws<DomainValidationException>(() => club.UpdateProfile(null, 0, 0, 0, BoardExpectation.MidTable));
        Assert.Throws<DomainValidationException>(() => club.UpdateProfile(null, 50, 0, -1, BoardExpectation.MidTable));
        Assert.Throws<DomainValidationException>(() => club.UpdateProfile(null, 50, Club.MaxBalance + 1, 0, BoardExpectation.MidTable));
        Assert.Throws<DomainValidationException>(() => club.UpdateProfile(null, 50, 0, 0, (BoardExpectation)99));
    }
}
