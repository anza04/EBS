using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Seasons;

namespace Ebm.Domain.Competitions;

/// <summary>A club's entry in one competition for one season. A club can hold entries in many competitions.</summary>
public sealed class CompetitionParticipant : Entity<CompetitionParticipantId>
{
    public CompetitionParticipant(CompetitionParticipantId id, CompetitionId competitionId, SeasonId seasonId, ClubId clubId)
        : base(id)
    {
        CompetitionId = Guard.Required(competitionId, nameof(competitionId));
        SeasonId = Guard.Required(seasonId, nameof(seasonId));
        ClubId = Guard.Required(clubId, nameof(clubId));
    }

    private CompetitionParticipant()
    {
    }

    public CompetitionId CompetitionId { get; private set; }

    public SeasonId SeasonId { get; private set; }

    public ClubId ClubId { get; private set; }
}
