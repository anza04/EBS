using Ebm.Domain.Clubs;
using Ebm.Domain.Players;
using Ebm.Domain.Staff;

namespace Ebm.Domain.History;

/// <summary>A player's figures in one competition season (regular season), for awards and career histories.</summary>
public sealed record PlayerSeasonLine(
    PlayerId PlayerId,
    ClubId ClubId,
    int Age,
    int Games,
    int Minutes,
    int Points,
    int Rebounds,
    int DefensiveRebounds,
    int Assists,
    int Steals,
    int Blocks,
    int Turnovers,
    double TeamWinShare)
{
    public double PointsPerGame => PerGame(Points);

    public double ReboundsPerGame => PerGame(Rebounds);

    public double AssistsPerGame => PerGame(Assists);

    public double MinutesPerGame => PerGame(Minutes);

    /// <summary>Per game: points + 1.2 rebounds + 1.5 assists + 2 steals + 2 blocks − turnovers.</summary>
    public double Efficiency => PerGame(Points + (1.2 * Rebounds) + (1.5 * Assists) + (2 * Steals) + (2 * Blocks) - Turnovers);

    /// <summary>Per game: 2 steals + 2 blocks + 0.5 defensive rebounds.</summary>
    public double Defense => PerGame((2 * Steals) + (2 * Blocks) + (0.5 * DefensiveRebounds));

    /// <summary>MVP score: efficiency weighted by the team's success, × (0.6 + 0.8 × win share).</summary>
    public double ValueScore => Efficiency * (0.6 + (0.8 * TeamWinShare));

    private double PerGame(double total) => Games == 0 ? 0 : total / Games;
}

/// <summary>An award decided by <see cref="HistoryRules.Awards"/>.</summary>
public sealed record AwardPick(AwardKind Kind, int Rank, PlayerSeasonLine Line, int Value);

/// <summary>A club's season for the coach award: reputation rank in the league (1 = highest) and final position.</summary>
public sealed record ClubSeasonResult(ClubId ClubId, int ReputationRank, int Position);

/// <summary>A player's career figures for the hall of fame.</summary>
public sealed record CareerSummary(int Seasons, int Games, int Points, int Rebounds, int Assists, int Titles, int Mvps, int OtherAwards, int AllLeague, int Reputation);

/// <summary>Pure rules of the long-term world (docs/HISTORY.md): awards, hall of fame, coaching careers, youth intake, reputation drift.</summary>
public static class HistoryRules
{
    /// <summary>Award candidates must play at least this share of the most games played by anybody in the season.</summary>
    public const double MinGameShare = 0.5;

    /// <summary>The young player award is for players this age or younger on the first day of the season.</summary>
    public const int YoungMaxAge = 22;

    public const int AllLeagueTeamSize = 5;

    /// <summary>Retired players with at least this <see cref="HallOfFameScore"/> are inducted.</summary>
    public const int HallOfFameThreshold = 50;

    /// <summary>Retired players with at least this <see cref="CoachingAptitude"/> start a coaching career.</summary>
    public const int CoachingMinAptitude = 60;

    /// <summary>Former players with at least this aptitude (or in the hall of fame) start as head coaches.</summary>
    public const int HeadCoachAptitude = 72;

    public const int YouthAge = 17;

    /// <summary>Youth players this age or older can be promoted to the first team.</summary>
    public const int PromotionAge = 19;

    /// <summary>Clubs from this reputation take two youth players a year instead of one.</summary>
    public const int SecondIntakeReputation = 60;

    /// <summary>Reputations move this share of the way back to <see cref="DriftCentre"/> every season.</summary>
    public const double DriftRate = 0.05;

    public const int DriftCentre = 50;

    /// <summary>
    /// The individual awards of a league season from the players' lines: MVP (best <see cref="PlayerSeasonLine.ValueScore"/>),
    /// top scorer, rebounder and passer (per game), best defender, young player and the all-league team (top five value
    /// scores). Only players with at least <see cref="MinGameShare"/> of the most games played are candidates; ties go to
    /// the player with fewer games, then the lower ID.
    /// </summary>
    public static IReadOnlyList<AwardPick> Awards(IReadOnlyList<PlayerSeasonLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            return [];
        }

        var minGames = Math.Max(1, (int)Math.Ceiling(lines.Max(l => l.Games) * MinGameShare));
        var eligible = lines.Where(l => l.Games >= minGames).ToList();
        var picks = new List<AwardPick>();
        void Best(AwardKind kind, IEnumerable<PlayerSeasonLine> pool, Func<PlayerSeasonLine, double> score)
        {
            if (pool.OrderByDescending(score).ThenBy(l => l.Games).ThenBy(l => l.PlayerId.Value).FirstOrDefault() is { } top)
            {
                picks.Add(new AwardPick(kind, 1, top, (int)Math.Round(score(top) * 10)));
            }
        }

        Best(AwardKind.MostValuablePlayer, eligible, l => l.ValueScore);
        Best(AwardKind.TopScorer, eligible, l => l.PointsPerGame);
        Best(AwardKind.TopRebounder, eligible, l => l.ReboundsPerGame);
        Best(AwardKind.TopAssists, eligible, l => l.AssistsPerGame);
        Best(AwardKind.BestDefender, eligible, l => l.Defense);
        Best(AwardKind.YoungPlayer, eligible.Where(l => l.Age <= YoungMaxAge), l => l.ValueScore);
        picks.AddRange(eligible.OrderByDescending(l => l.ValueScore).ThenBy(l => l.Games).ThenBy(l => l.PlayerId.Value).Take(AllLeagueTeamSize)
            .Select((l, i) => new AwardPick(AwardKind.AllLeagueTeam, i + 1, l, (int)Math.Round(l.ValueScore * 10))));
        return picks;
    }

    /// <summary>The club that finished most places above its reputation rank (ties: the better position); <c>null</c> without clubs.</summary>
    public static ClubSeasonResult? CoachOfTheSeason(IEnumerable<ClubSeasonResult> clubs)
    {
        ArgumentNullException.ThrowIfNull(clubs);
        return clubs.OrderByDescending(c => c.ReputationRank - c.Position).ThenBy(c => c.Position).ThenBy(c => c.ClubId.Value).FirstOrDefault();
    }

    /// <summary>
    /// games / 10 + points / 200 + (rebounds + assists) / 300 + 8 per title + 15 per MVP + 5 per other award + 3 per
    /// all-league selection + reputation above 60 (so stars from before the game's records count too).
    /// </summary>
    public static int HallOfFameScore(CareerSummary career)
    {
        ArgumentNullException.ThrowIfNull(career);
        return (career.Games / 10) + (career.Points / 200) + ((career.Rebounds + career.Assists) / 300) + (8 * career.Titles)
            + (15 * career.Mvps) + (5 * career.OtherAwards) + (3 * career.AllLeague) + Math.Max(0, career.Reputation - 60);
    }

    /// <summary>Mean of leadership, decision making, court vision and work rate.</summary>
    public static int CoachingAptitude(PlayerAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return (attributes.Leadership + attributes.DecisionMaking + attributes.CourtVision + attributes.WorkRate) / 4;
    }

    /// <summary>The role a retired player starts his coaching career in; <c>null</c> when he does not coach.</summary>
    public static StaffRole? CoachingRole(PlayerAttributes attributes, bool hallOfFame)
    {
        var aptitude = CoachingAptitude(attributes);
        return aptitude < CoachingMinAptitude && !hallOfFame ? null
            : aptitude >= HeadCoachAptitude || hallOfFame ? StaffRole.HeadCoach
            : StaffRole.AssistantCoach;
    }

    /// <summary>
    /// A former player's staff attributes: tactical knowledge from his reading of the game, development from his work
    /// rate, motivation from his leadership, some scouting, little medical knowledge, his playing reputation; no
    /// experience yet. Styles follow the player: leaders motivate, hard workers develop.
    /// </summary>
    public static StaffProfile CoachProfile(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var a = player.Attributes;
        static int Clamp(int v) => Math.Clamp(v, StaffMember.MinAbility, StaffMember.MaxAbility);
        var style = a.Leadership >= a.WorkRate + 10 ? CoachingStyle.Motivator : a.WorkRate >= a.Leadership + 10 ? CoachingStyle.YouthDevelopment : CoachingStyle.Balanced;
        return new StaffProfile(
            Clamp(((a.DecisionMaking + a.CourtVision + a.DefensivePositioning) / 3) - 5),
            Clamp(a.WorkRate - 10),
            Clamp(a.Leadership),
            Clamp(a.Concentration - 15),
            20,
            Clamp(player.Reputation),
            0,
            style);
    }

    /// <summary>Youth players a club takes in a year.</summary>
    public static int IntakeSize(int reputation) => reputation >= SecondIntakeReputation ? 2 : 1;

    /// <summary>
    /// A youth player's level and potential: quality 25 + reputation / 5 (± 4), potential quality + 20 + reputation / 5
    /// + 0…15 (at most 95). Better clubs have better academies.
    /// </summary>
    public static (int Quality, int Potential) Prospect(int reputation, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var quality = Math.Clamp(25 + (reputation / 5) + random.Next(-4, 5), 20, 60);
        return (quality, Math.Min(95, quality + 20 + (reputation / 5) + random.Next(0, 16)));
    }

    /// <summary>The youth players to promote: at least <see cref="PromotionAge"/>, at least as good as the weakest first-team player, best first, while there is room.</summary>
    public static IReadOnlyList<PlayerId> Promotions(IEnumerable<(PlayerId Player, int Age, int Overall)> youth, IReadOnlyCollection<int> firstTeamOveralls, int maxPlayers)
    {
        ArgumentNullException.ThrowIfNull(youth);
        ArgumentNullException.ThrowIfNull(firstTeamOveralls);
        var room = maxPlayers - firstTeamOveralls.Count;
        var weakest = firstTeamOveralls.Count == 0 ? 0 : firstTeamOveralls.Min();
        return room <= 0 ? [] : [.. youth.Where(y => y.Age >= PromotionAge && y.Overall >= weakest).OrderByDescending(y => y.Overall).ThenBy(y => y.Player.Value).Take(room).Select(y => y.Player)];
    }

    /// <summary>The season's pull back to the centre: −round-toward-zero((reputation − 50) × 5%), so 90 loses 2, 70 loses 1, 60 nothing.</summary>
    public static int Drift(int reputation) => -(int)Math.Round((reputation - DriftCentre) * DriftRate, MidpointRounding.ToZero);
}
