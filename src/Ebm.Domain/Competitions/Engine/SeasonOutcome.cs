using Ebm.Domain.Clubs;

namespace Ebm.Domain.Competitions.Engine;

/// <summary>
/// The result of a finished competition season (<see cref="CompetitionSeason.Outcome"/>). The champion is the
/// playoff winner, or the leader of the final table when there are no playoffs. Promoted clubs move up one
/// division, relegated clubs down one. <see cref="RunnerUp"/> is the losing finalist (or the second of the table).
/// </summary>
public sealed record SeasonOutcome(ClubId Champion, IReadOnlyList<ClubId> Promoted, IReadOnlyList<ClubId> Relegated, ClubId? RunnerUp = null);
