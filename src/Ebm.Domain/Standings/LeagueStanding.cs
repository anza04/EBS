using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;

namespace Ebm.Domain.Standings;

/// <summary>
/// A club's row in a competition's table for one season. Standings points per result come from the
/// competition rules, so the caller passes them in.
/// </summary>
public sealed class LeagueStanding : Entity<LeagueStandingId>
{
    public LeagueStanding(LeagueStandingId id, CompetitionId competitionId, SeasonId seasonId, ClubId clubId)
        : base(id)
    {
        CompetitionId = Guard.Required(competitionId, nameof(competitionId));
        SeasonId = Guard.Required(seasonId, nameof(seasonId));
        ClubId = Guard.Required(clubId, nameof(clubId));
    }

    private LeagueStanding()
    {
    }

    public CompetitionId CompetitionId { get; private set; }

    public SeasonId SeasonId { get; private set; }

    public ClubId ClubId { get; private set; }

    public int Won { get; private set; }

    public int Lost { get; private set; }

    public int Played => Won + Lost;

    public int PointsFor { get; private set; }

    public int PointsAgainst { get; private set; }

    public int PointDifference => PointsFor - PointsAgainst;

    /// <summary>Table points, awarded according to the competition rules.</summary>
    public int StandingPoints { get; private set; }

    public void RecordResult(int scored, int conceded, int standingPointsAwarded)
    {
        Guard.Range(scored, 0, Match.MaxScore, "Points scored");
        Guard.Range(conceded, 0, Match.MaxScore, "Points conceded");
        Guard.Range(standingPointsAwarded, 0, 10, "Standing points awarded");
        if (scored == conceded)
        {
            throw new DomainValidationException("A basketball match cannot end in a draw.");
        }

        if (scored > conceded)
        {
            Won++;
        }
        else
        {
            Lost++;
        }

        PointsFor += scored;
        PointsAgainst += conceded;
        StandingPoints += standingPointsAwarded;
    }
}
