using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;

namespace Ebm.Tests.Unit.Contracts;

public sealed class ContractRulesTests
{
    private static readonly DateOnly Start = new(2026, 7, 1);
    private static readonly DateOnly End = new(2028, 6, 30);

    private static Contract Make(ContractTerms? terms = null) =>
        Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(), terms ?? new ContractTerms(Start, End, 240_000));

    [Fact]
    public void New_contracts_are_active_with_default_terms()
    {
        var c = Make();
        Assert.Equal(ContractStatus.Active, c.Status);
        Assert.Equal((PaymentFrequency.Monthly, 0L, true, (long?)null), (c.PaymentFrequency, c.SigningBonus, c.PerformanceBonuses.IsEmpty, c.ReleaseClause));
        Assert.Null(c.TerminationDate);
        Assert.Equal(End, c.EffectiveEndDate);
    }

    [Theory]
    [InlineData(-1, 0, null)]
    [InlineData(Contract.MaxAnnualSalary + 1, 0, null)]
    [InlineData(100_000, -5, null)]
    [InlineData(100_000, 0, 0L)]
    [InlineData(100_000, 0, Contract.MaxReleaseClause + 1)]
    public void Money_terms_are_range_checked(long salary, long bonus, long? clause) =>
        Assert.Throws<DomainValidationException>(() => Make(new ContractTerms(Start, End, salary, SigningBonus: bonus, ReleaseClause: clause)));

    [Fact]
    public void Dates_and_options_are_checked()
    {
        Assert.Throws<DomainValidationException>(() => Make(new ContractTerms(Start, Start, 1)));
        Assert.Throws<DomainValidationException>(() => Make(new ContractTerms(Start, Start.AddYears(Contract.MaxLengthYears).AddDays(1), 1)));
        Assert.Throws<DomainValidationException>(() => Make(new ContractTerms(Start, End, 1, ClubOptionUntil: End)));
        Assert.Throws<DomainValidationException>(() => Make(new ContractTerms(Start, End, 1, PlayerOptionUntil: End.AddYears(Contract.MaxOptionYears).AddDays(1))));
        Assert.Throws<DomainValidationException>(() => Make(new ContractTerms(Start, End, 1, PaymentFrequency: (PaymentFrequency)9)));
        var ok = Make(new ContractTerms(Start, End, 1, ClubOptionUntil: End.AddYears(1), PlayerOptionUntil: End.AddYears(2)));
        Assert.Equal((End.AddYears(1), End.AddYears(2)), (ok.ClubOptionUntil, ok.PlayerOptionUntil));
    }

    [Fact]
    public void Running_and_upcoming_follow_the_dates_and_the_early_end()
    {
        var c = Make();
        Assert.True(c.IsUpcomingOn(Start.AddDays(-1)));
        Assert.False(c.IsRunningOn(Start.AddDays(-1)));
        Assert.True(c.IsRunningOn(Start));
        Assert.True(c.IsRunningOn(End));
        Assert.False(c.IsRunningOn(End.AddDays(1)));
        Assert.Equal(ContractStatus.Expired, c.StatusOn(End.AddDays(1)));
        Assert.Equal(ContractStatus.Active, c.StatusOn(End));

        var running = Contract.RunningOn(new DateOnly(2027, 1, 1)).Compile();
        var open = Contract.RunningOrUpcomingOn(new DateOnly(2027, 1, 1)).Compile();
        c.Terminate(new DateOnly(2027, 1, 1), 0);
        Assert.False(c.IsRunningOn(new DateOnly(2027, 1, 1)));
        Assert.True(c.IsRunningOn(new DateOnly(2026, 12, 31)));
        Assert.False(running(c));
        Assert.False(open(c));
        Assert.True(Contract.RunningOn(new DateOnly(2026, 12, 31)).Compile()(c));
    }

    [Fact]
    public void Termination_ends_the_contract_once_and_records_the_settlement()
    {
        var c = Make();
        c.Terminate(new DateOnly(2027, 3, 1), 150_000);
        Assert.Equal((ContractStatus.Terminated, new DateOnly(2027, 3, 1), 150_000L), (c.Status, c.TerminationDate, c.TerminationCompensation));
        Assert.Equal(new DateOnly(2027, 2, 28), c.EffectiveEndDate);
        Assert.Throws<DomainValidationException>(() => c.Terminate(new DateOnly(2027, 3, 2), 0));

        var late = Make();
        Assert.Throws<DomainValidationException>(() => late.Terminate(End.AddDays(1), 0));
        Assert.Throws<DomainValidationException>(() => late.Terminate(Start.AddDays(10), -1));
    }

    [Fact]
    public void A_contract_cancelled_before_it_starts_never_runs()
    {
        var c = Make();
        c.Terminate(Start.AddDays(-30), 0);
        Assert.Equal(Start, c.TerminationDate);
        Assert.False(c.IsUpcomingOn(Start.AddDays(-1)));
        Assert.False(c.IsRunningOn(Start));
        Assert.False(Contract.RunningOrUpcomingOn(Start.AddDays(-1)).Compile()(c));
    }

    [Fact]
    public void Renewal_supersedes_the_contract_early_or_on_its_end()
    {
        var early = Make();
        early.Supersede(new DateOnly(2027, 7, 1));
        Assert.Equal((ContractStatus.Renewed, new DateOnly(2027, 7, 1)), (early.Status, early.TerminationDate));

        var onEnd = Make();
        onEnd.Supersede(End.AddDays(1));
        Assert.Equal(ContractStatus.Renewed, onEnd.Status);
        Assert.Null(onEnd.TerminationDate);
        Assert.True(onEnd.IsRunningOn(End));

        Assert.Throws<DomainValidationException>(() => Make().Supersede(End.AddDays(2)));
        Assert.Throws<DomainValidationException>(() => Make().Supersede(Start));
        Assert.Throws<DomainValidationException>(() => early.Supersede(End));
    }

    [Fact]
    public void Expiry_is_recorded_only_after_the_end_date()
    {
        var c = Make();
        Assert.Throws<DomainValidationException>(() => c.Expire(End));
        c.Expire(End.AddDays(1));
        Assert.Equal(ContractStatus.Expired, c.Status);
        Assert.Throws<DomainValidationException>(() => c.Expire(End.AddDays(2)));
    }

    [Fact]
    public void Options_extend_or_lapse_and_a_spent_option_is_cleared()
    {
        var c = Make(new ContractTerms(Start, End, 1, ClubOptionUntil: End.AddYears(2), PlayerOptionUntil: End.AddYears(1)));
        c.ExerciseOption(OptionHolder.Club, End);
        Assert.Equal(End.AddYears(2), c.EndDate);
        Assert.Null(c.ClubOptionUntil);
        Assert.Null(c.PlayerOptionUntil); // ends before the new end date: spent

        var d = Make(new ContractTerms(Start, End, 1, PlayerOptionUntil: End.AddYears(1)));
        Assert.Throws<DomainValidationException>(() => d.ExerciseOption(OptionHolder.Club, End));
        Assert.Throws<DomainValidationException>(() => d.ExerciseOption(OptionHolder.Player, End.AddDays(1)));
        d.DeclineOption(OptionHolder.Player, End);
        Assert.Equal(End, d.EndDate);
        Assert.Null(d.PlayerOptionUntil);
    }

    [Fact]
    public void Editing_terms_keeps_the_early_end_inside_the_dates()
    {
        var c = Make();
        c.Terminate(new DateOnly(2027, 3, 1), 0);
        Assert.Throws<DomainValidationException>(() => c.UpdateTerms(Start, new DateOnly(2027, 1, 31), 1));
        c.UpdateTerms(c.Terms with { AnnualSalary = 300_000 });
        Assert.Equal(300_000, c.AnnualSalary);
    }

    [Fact]
    public void Club_balance_changes_stay_within_range()
    {
        var club = new Club(ClubId.New(), "Club", "CLB", Ebm.Domain.Countries.CountryId.New());
        club.ChangeBalance(-500);
        Assert.Equal(-500, club.Balance);
        Assert.Throws<DomainValidationException>(() => club.ChangeBalance(-Club.MaxBalance));
    }
}

public sealed class PerformanceBonusesTests
{
    [Fact]
    public void Bonuses_round_trip_through_their_stored_form()
    {
        var bonuses = PerformanceBonuses.Parse(" leagueTitle : 50000 ; ReachPlayoffs:20000|PerGamePlayed:500 ");
        Assert.Equal("PerGamePlayed:500;ReachPlayoffs:20000;LeagueTitle:50000", bonuses.ToString());
        Assert.Equal(bonuses, PerformanceBonuses.Parse(bonuses.ToString()));
        Assert.Equal(70_000, bonuses.SeasonTotal);
        Assert.Equal(500, bonuses[BonusCondition.PerGamePlayed]);
        Assert.Same(PerformanceBonuses.None, PerformanceBonuses.Parse("  "));
    }

    [Theory]
    [InlineData("Title:100")]
    [InlineData("LeagueTitle")]
    [InlineData("LeagueTitle:abc")]
    [InlineData("LeagueTitle:0")]
    [InlineData("1:100")]
    [InlineData("LeagueTitle:100;LeagueTitle:200")]
    public void Invalid_bonuses_are_refused(string text) =>
        Assert.Throws<DomainValidationException>(() => PerformanceBonuses.Parse(text));
}
