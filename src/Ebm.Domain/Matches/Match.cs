using Ebm.Domain.Common;

namespace Ebm.Domain.Matches;

/// <summary>The played result of a fixture. Basketball has no draws.</summary>
public sealed class Match : Entity<MatchId>
{
    public const int MaxScore = 300;
    public const int MaxOvertimes = 10;

    public Match(MatchId id, FixtureId fixtureId, int homeScore, int awayScore, int overtimes = 0)
        : base(id)
    {
        FixtureId = Guard.Required(fixtureId, nameof(fixtureId));
        HomeScore = Guard.Range(homeScore, 0, MaxScore, "Home score");
        AwayScore = Guard.Range(awayScore, 0, MaxScore, "Away score");
        Overtimes = Guard.Range(overtimes, 0, MaxOvertimes, "Overtimes");
        if (homeScore == awayScore)
        {
            throw new DomainValidationException("A basketball match cannot end in a draw.");
        }
    }

    private Match()
    {
    }

    public FixtureId FixtureId { get; private set; }

    public int HomeScore { get; private set; }

    public int AwayScore { get; private set; }

    public int Overtimes { get; private set; }

    public bool HomeWon => HomeScore > AwayScore;
}
