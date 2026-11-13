using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Seasons;

namespace Ebm.Domain.Matches;

/// <summary>Part of a competition season a fixture belongs to.</summary>
public enum FixtureStage
{
    RegularSeason,
    Playoffs,

    /// <summary>Round robin among the groups formed after the regular season.</summary>
    SecondPhase,

    /// <summary>Single games deciding the last two playoff seeds (round 1: two games, round 2: one).</summary>
    PlayIn,
}

/// <summary>A scheduled game between two clubs in a competition season. Its result is a <see cref="Match"/>.</summary>
/// <remarks>
/// <see cref="Round"/> counts within the stage (regular-season round, or playoff round 1 = first round);
/// <see cref="GameNumber"/> is the game within a playoff series and always 1 in the regular season.
/// </remarks>
public sealed class Fixture : Entity<FixtureId>
{
    public const int MaxRound = 200;
    public const int MaxGameNumber = 7;

    public Fixture(
        FixtureId id,
        CompetitionId competitionId,
        SeasonId seasonId,
        int round,
        DateOnly date,
        ClubId homeClubId,
        ClubId awayClubId,
        FixtureStage stage = FixtureStage.RegularSeason,
        int gameNumber = 1)
        : base(id)
    {
        CompetitionId = Guard.Required(competitionId, nameof(competitionId));
        SeasonId = Guard.Required(seasonId, nameof(seasonId));
        Round = Guard.Range(round, 1, MaxRound, "Round");
        Date = date;
        HomeClubId = Guard.Required(homeClubId, nameof(homeClubId));
        AwayClubId = Guard.Required(awayClubId, nameof(awayClubId));
        Stage = Guard.Defined(stage, "Fixture stage");
        GameNumber = Guard.Range(gameNumber, 1, stage == FixtureStage.Playoffs ? MaxGameNumber : 1, "Game number");
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

    public FixtureStage Stage { get; private set; }

    public int Round { get; private set; }

    public int GameNumber { get; private set; } = 1;

    public DateOnly Date { get; private set; }

    public ClubId HomeClubId { get; private set; }

    public ClubId AwayClubId { get; private set; }
}
