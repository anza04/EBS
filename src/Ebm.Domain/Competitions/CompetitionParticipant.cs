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

    /// <summary>The club's seed in a cup (1 = best), set by the draw; <c>null</c> before the draw and in leagues.</summary>
    public int? Seed { get; private set; }

    public void SetSeed(int? seed) => Seed = seed is null ? null : Guard.Range(seed.Value, 1, Rules.RegistrationRules.TeamLimit, "Seed");

    /// <summary>Prize money paid when the cup was over (euros); <c>null</c> = not paid (yet), or a league.</summary>
    public long? PrizeMoney { get; private set; }

    /// <summary>Reputation points gained when the cup was over; <c>null</c> = not paid (yet).</summary>
    public int? ReputationGain { get; private set; }

    /// <summary>Records the cup rewards; paid once only.</summary>
    public void RecordReward(long prizeMoney, int reputationGain)
    {
        if (PrizeMoney is not null)
        {
            throw new DomainValidationException("The rewards of this entry have already been paid.");
        }

        PrizeMoney = prizeMoney >= 0 ? prizeMoney : throw new DomainValidationException("Prize money cannot be negative.");
        ReputationGain = Guard.Range(reputationGain, 0, Club.MaxReputation, "Reputation gain");
    }
}
