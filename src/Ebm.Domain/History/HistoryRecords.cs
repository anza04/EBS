using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Domain.Staff;

namespace Ebm.Domain.History;

/// <summary>An individual award of a league season (docs/HISTORY.md §3).</summary>
public enum AwardKind
{
    MostValuablePlayer,
    TopScorer,
    TopRebounder,
    TopAssists,
    BestDefender,
    YoungPlayer,

    /// <summary>One of the five players of the season's all-league team (<see cref="SeasonAward.Rank"/> 1–5).</summary>
    AllLeagueTeam,

    /// <summary>The coach of the club that most outperformed its reputation; <see cref="SeasonAward.PlayerId"/> is empty.</summary>
    CoachOfTheSeason,
}

public readonly record struct SeasonAwardId(Guid Value) : IEntityId<SeasonAwardId>
{
    public static SeasonAwardId New() => new(Guid.CreateVersion7());

    public static SeasonAwardId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// An award of one league competition season, decided when the season is over and never changed. Links are plain IDs
/// (no foreign keys), so the record outlives players or staff removed in the editor.
/// </summary>
public sealed class SeasonAward : Entity<SeasonAwardId>
{
    public SeasonAward(SeasonAwardId id, SeasonId seasonId, CompetitionId competitionId, AwardKind kind, int rank, ClubId clubId, PlayerId? playerId, StaffMemberId? staffMemberId, int value)
        : base(id)
    {
        SeasonId = Guard.Required(seasonId, nameof(seasonId));
        CompetitionId = Guard.Required(competitionId, nameof(competitionId));
        Kind = Guard.Defined(kind, "Award");
        Rank = Guard.Range(rank, 1, 5, "Award rank");
        ClubId = Guard.Required(clubId, nameof(clubId));
        PlayerId = Guard.Optional(playerId, nameof(playerId));
        StaffMemberId = Guard.Optional(staffMemberId, nameof(staffMemberId));
        Value = Guard.Range(value, -1000, 100_000, "Award value");
        if (kind != AwardKind.CoachOfTheSeason && playerId is null)
        {
            throw new DomainValidationException("A player award needs a player.");
        }
    }

    private SeasonAward()
    {
    }

    public SeasonId SeasonId { get; private set; }

    public CompetitionId CompetitionId { get; private set; }

    public AwardKind Kind { get; private set; }

    /// <summary>1, or the slot in the all-league team.</summary>
    public int Rank { get; private set; }

    /// <summary>The club the winner represented.</summary>
    public ClubId ClubId { get; private set; }

    public PlayerId? PlayerId { get; private set; }

    /// <summary>The club's head coach for <see cref="AwardKind.CoachOfTheSeason"/>; empty when the club had none (the career manager).</summary>
    public StaffMemberId? StaffMemberId { get; private set; }

    /// <summary>The figure behind the award, ×10 (22.4 points a game = 224; places gained for the coach award).</summary>
    public int Value { get; private set; }
}

public readonly record struct HallOfFameEntryId(Guid Value) : IEntityId<HallOfFameEntryId>
{
    public static HallOfFameEntryId New() => new(Guid.CreateVersion7());

    public static HallOfFameEntryId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>A retired player inducted into the hall of fame, with the career figures at induction (docs/HISTORY.md §5).</summary>
public sealed class HallOfFameEntry : Entity<HallOfFameEntryId>
{
    public HallOfFameEntry(HallOfFameEntryId id, PlayerId playerId, DateOnly inductedOn, CareerSummary career, int score)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(career);
        PlayerId = Guard.Required(playerId, nameof(playerId));
        InductedOn = inductedOn;
        Score = Guard.Range(score, 0, 100_000, "Hall of fame score");
        Seasons = Guard.Range(career.Seasons, 0, 100, "Seasons");
        Games = Guard.Range(career.Games, 0, 10_000, "Games");
        Points = Guard.Range(career.Points, 0, 1_000_000, "Points");
        Titles = Guard.Range(career.Titles, 0, 1000, "Titles");
        Awards = Guard.Range(career.Mvps + career.OtherAwards + career.AllLeague, 0, 1000, "Awards");
    }

    private HallOfFameEntry()
    {
    }

    /// <summary>Unique: a player is inducted once.</summary>
    public PlayerId PlayerId { get; private set; }

    public DateOnly InductedOn { get; private set; }

    public int Score { get; private set; }

    public int Seasons { get; private set; }

    public int Games { get; private set; }

    public int Points { get; private set; }

    public int Titles { get; private set; }

    public int Awards { get; private set; }
}

public readonly record struct LeagueSeasonSnapshotId(Guid Value) : IEntityId<LeagueSeasonSnapshotId>
{
    public static LeagueSeasonSnapshotId New() => new(Guid.CreateVersion7());

    public static LeagueSeasonSnapshotId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// How strong a league was at the end of one season (docs/HISTORY.md §7): the record of long-term league evolution.
/// Unique per competition and season.
/// </summary>
public sealed class LeagueSeasonSnapshot : Entity<LeagueSeasonSnapshotId>
{
    public LeagueSeasonSnapshot(LeagueSeasonSnapshotId id, CompetitionId competitionId, SeasonId seasonId, int clubs, int meanReputation, int topReputation, int meanOverall, ClubId champion)
        : base(id)
    {
        CompetitionId = Guard.Required(competitionId, nameof(competitionId));
        SeasonId = Guard.Required(seasonId, nameof(seasonId));
        Clubs = Guard.Range(clubs, 1, 1000, "Clubs");
        MeanReputation = Guard.Range(meanReputation, Club.MinReputation, Club.MaxReputation, "Mean reputation");
        TopReputation = Guard.Range(topReputation, Club.MinReputation, Club.MaxReputation, "Top reputation");
        MeanOverall = Guard.Range(meanOverall, 0, 100, "Mean overall");
        Champion = Guard.Required(champion, nameof(champion));
    }

    private LeagueSeasonSnapshot()
    {
    }

    public CompetitionId CompetitionId { get; private set; }

    public SeasonId SeasonId { get; private set; }

    public int Clubs { get; private set; }

    public int MeanReputation { get; private set; }

    public int TopReputation { get; private set; }

    /// <summary>Mean overall rating of the clubs' best eight players.</summary>
    public int MeanOverall { get; private set; }

    public ClubId Champion { get; private set; }
}
