using Ebm.Domain.Common;

namespace Ebm.Domain.Matches;

/// <summary>The played result of a fixture. Basketball has no draws.</summary>
public sealed class Match : Entity<MatchId>
{
    public const int MaxScore = 300;
    public const int MaxOvertimes = 10;

    /// <summary>Score of a game awarded by forfeit (FIBA: 20–0 to the side that could play).</summary>
    public const int ForfeitScore = 20;

    public Match(MatchId id, FixtureId fixtureId, int homeScore, int awayScore, int overtimes = 0)
        : this(id, fixtureId, homeScore, awayScore, overtimes, isForfeit: false)
    {
    }

    private Match(MatchId id, FixtureId fixtureId, int homeScore, int awayScore, int overtimes, bool isForfeit)
        : base(id)
    {
        IsForfeit = isForfeit;
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

    /// <summary>A game awarded 20–0 because a club could not field five players. It has no box score.</summary>
    public static Match Forfeit(MatchId id, FixtureId fixtureId, bool homeForfeits) =>
        new(id, fixtureId, homeForfeits ? 0 : ForfeitScore, homeForfeits ? ForfeitScore : 0, 0, isForfeit: true);

    public FixtureId FixtureId { get; private set; }

    public int HomeScore { get; private set; }

    public int AwayScore { get; private set; }

    public int Overtimes { get; private set; }

    public bool IsForfeit { get; private set; }

    public bool HomeWon => HomeScore > AwayScore;
}
