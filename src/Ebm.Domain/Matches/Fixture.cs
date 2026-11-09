using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Seasons;

namespace Ebm.Domain.Matches;

/// <summary>A scheduled game between two clubs in a competition season. Its result is a <see cref="Match"/>.</summary>
public sealed class Fixture : Entity<FixtureId>
{
    public const int MaxRound = 200;

    public Fixture(FixtureId id, CompetitionId competitionId, SeasonId seasonId, int round, DateOnly date, ClubId homeClubId, ClubId awayClubId)
        : base(id)
    {
        CompetitionId = Guard.Required(competitionId, nameof(competitionId));
        SeasonId = Guard.Required(seasonId, nameof(seasonId));
        Round = Guard.Range(round, 1, MaxRound, "Round");
        Date = date;
        HomeClubId = Guard.Required(homeClubId, nameof(homeClubId));
        AwayClubId = Guard.Required(awayClubId, nameof(awayClubId));
        if (homeClubId == awayClubId)
        {
            throw new DomainValidationException("A club cannot play against itself.");
        }
    }

    private Fixture()
    {
    }

    public CompetitionId CompetitionId { get; private set; }

    public SeasonId SeasonId { get; private set; }

    public int Round { get; private set; }

    public DateOnly Date { get; private set; }

    public ClubId HomeClubId { get; private set; }

    public ClubId AwayClubId { get; private set; }
}
