using Ebm.Domain.Careers;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;

namespace Ebm.Tests.Unit.Competitions;

public sealed class SeasonLifecycleTests
{
    private static List<ClubId> Clubs(int count) => [.. Enumerable.Range(0, count).Select(_ => ClubId.New())];

    [Fact]
    public void Promotion_and_relegation_swap_clubs_between_adjacent_divisions()
    {
        var top = Clubs(8);
        var second = Clubs(6);
        var third = Clubs(4);
        var next = SeasonTransition.NextEntries(
        [
            new DivisionSeason(1, top, new SeasonOutcome(top[0], [], [top[6], top[7]])),
            new DivisionSeason(2, second, new SeasonOutcome(second[0], [second[0], second[3]], [second[5]])),
            new DivisionSeason(3, third, new SeasonOutcome(third[1], [third[1]], [third[3]])),
        ]);

        Assert.True(next[1].ToHashSet().SetEquals([.. top.Take(6), second[0], second[3]]));
        Assert.Equal(6, next[2].Count);
        Assert.Contains(top[6], next[2]);
        Assert.Contains(third[1], next[2]);
        Assert.DoesNotContain(second[5], next[2]);
        Assert.Contains(second[5], next[3]);
        Assert.Contains(third[3], next[3]); // no division below: relegation places are ignored
        Assert.Equal(4, next[3].Count);
    }

    [Fact]
    public void The_next_season_has_the_same_dates_one_year_later()
    {
        var season = new Season(SeasonId.New(), "2026-27", new DateOnly(2026, 10, 1), new DateOnly(2027, 6, 30));

        var next = SeasonCalendar.Next(season, SeasonId.New());

        Assert.Equal(("2027-28", new DateOnly(2027, 10, 1), new DateOnly(2028, 6, 30)), (next.Name, next.StartDate, next.EndDate));
        Assert.Equal("2027", SeasonCalendar.NameFor(new DateOnly(2027, 3, 1), new DateOnly(2027, 11, 30)));
    }

    [Fact]
    public void Career_dates_only_move_forward()
    {
        var career = new Career(CareerId.New(), "Manager", ClubId.New(), SeasonId.New(), new DateOnly(2026, 9, 24));

        career.MoveTo(new DateOnly(2026, 9, 25));
        career.StartSeason(SeasonId.New());

        Assert.Equal((new DateOnly(2026, 9, 25), 2), (career.CurrentDate, career.SeasonsPlayed));
        Assert.Throws<DomainValidationException>(() => career.MoveTo(new DateOnly(2026, 9, 24)));
        Assert.Throws<DomainValidationException>(() => new Career(CareerId.New(), " ", ClubId.New(), SeasonId.New(), default));
    }

    [Fact]
    public void A_forfeit_is_20_0_to_the_other_side()
    {
        var home = Match.Forfeit(MatchId.New(), FixtureId.New(), homeForfeits: true);
        var away = Match.Forfeit(MatchId.New(), FixtureId.New(), homeForfeits: false);

        Assert.Equal((0, 20, true), (home.HomeScore, home.AwayScore, home.IsForfeit));
        Assert.Equal((20, 0, true), (away.HomeScore, away.AwayScore, away.IsForfeit));
        Assert.False(new Match(MatchId.New(), FixtureId.New(), 80, 70).IsForfeit);
    }
}
