using Ebm.Domain.Matches.Simulation;

namespace Ebm.Domain.Matches;

/// <summary>A player's advanced statistics (rates as fractions, ratings per 100 possessions; docs/MATCH_ENGINE.md §10).</summary>
public sealed record PlayerAdvancedLine(
    double TrueShooting,
    double EffectiveFieldGoal,
    double Usage,
    double Assist,
    double OffensiveRebound,
    double DefensiveRebound,
    double Rebound,
    double TurnoverRate,
    double? OffensiveRating,
    double? DefensiveRating,
    int PlusMinus)
{
    public double? NetRating => OffensiveRating - DefensiveRating;
}

/// <summary>A team's advanced statistics for a match (or summed matches).</summary>
public sealed record TeamAdvancedLine(
    double Possessions,
    double Pace,
    double OffensiveRating,
    double DefensiveRating,
    double TrueShooting,
    double EffectiveFieldGoal,
    double TurnoverRate,
    double OffensiveReboundRate,
    double FreeThrowRate)
{
    public double NetRating => OffensiveRating - DefensiveRating;
}

/// <summary>
/// Pure formulas over box scores. Team possessions are the mean of both teams' estimates
/// (FGA − ORB + TOV + 0.44·FTA), so one team's offensive rating is the other's defensive rating.
/// Player ratings are on-court: points for/against per 100 possessions played.
/// </summary>
public static class AdvancedStatistics
{
    public const double FreeThrowWeight = 0.44;

    /// <summary>Points / (2 × (FGA + 0.44 × FTA)).</summary>
    public static double TrueShooting(int points, int fieldGoalsAttempted, int freeThrowsAttempted) =>
        Ratio(points, 2 * (fieldGoalsAttempted + (FreeThrowWeight * freeThrowsAttempted)));

    /// <summary>(FGM + 0.5 × 3PM) / FGA.</summary>
    public static double EffectiveFieldGoal(int fieldGoalsMade, int threesMade, int fieldGoalsAttempted) =>
        Ratio(fieldGoalsMade + (0.5 * threesMade), fieldGoalsAttempted);

    /// <summary>TOV / (FGA + 0.44 × FTA + TOV).</summary>
    public static double TurnoverRate(int turnovers, int fieldGoalsAttempted, int freeThrowsAttempted) =>
        Ratio(turnovers, fieldGoalsAttempted + (FreeThrowWeight * freeThrowsAttempted) + turnovers);

    public static double EstimatedPossessions(TeamStatistics t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return t.FieldGoalsAttempted - t.OffensiveRebounds + t.Turnovers + (FreeThrowWeight * t.FreeThrowAttempted);
    }

    /// <summary>Sums box scores (a team's total, or a player's lines over several matches); shot data is dropped.</summary>
    public static BoxScore Total(IEnumerable<BoxScore> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var l = lines.ToList();
        return new BoxScore
        {
            Minutes = l.Sum(b => b.Minutes),
            TwoPointMade = l.Sum(b => b.TwoPointMade),
            TwoPointAttempted = l.Sum(b => b.TwoPointAttempted),
            ThreePointMade = l.Sum(b => b.ThreePointMade),
            ThreePointAttempted = l.Sum(b => b.ThreePointAttempted),
            FreeThrowMade = l.Sum(b => b.FreeThrowMade),
            FreeThrowAttempted = l.Sum(b => b.FreeThrowAttempted),
            OffensiveRebounds = l.Sum(b => b.OffensiveRebounds),
            DefensiveRebounds = l.Sum(b => b.DefensiveRebounds),
            Assists = l.Sum(b => b.Assists),
            Steals = l.Sum(b => b.Steals),
            Blocks = l.Sum(b => b.Blocks),
            Turnovers = l.Sum(b => b.Turnovers),
            PersonalFouls = l.Sum(b => b.PersonalFouls),
            PointsFor = l.Sum(b => b.PointsFor),
            PointsAgainst = l.Sum(b => b.PointsAgainst),
            OffensivePossessions = l.Sum(b => b.OffensivePossessions),
            DefensivePossessions = l.Sum(b => b.DefensivePossessions),
            DefendedAttempted = l.Sum(b => b.DefendedAttempted),
            DefendedMade = l.Sum(b => b.DefendedMade),
        };
    }

    public static TeamAdvancedLine Team(IEnumerable<BoxScore> team, IEnumerable<BoxScore> opponent)
    {
        var t = Total(team);
        var o = Total(opponent);
        var ts = TeamStatistics.From([t]);
        var os = TeamStatistics.From([o]);
        var possessions = (EstimatedPossessions(ts) + EstimatedPossessions(os)) / 2;
        var fga = ts.FieldGoalsAttempted;
        return new TeamAdvancedLine(
            possessions,
            t.Minutes == 0 ? possessions : possessions * 40 / (t.Minutes / 5.0),
            100 * Ratio(ts.Points, possessions),
            100 * Ratio(os.Points, possessions),
            TrueShooting(ts.Points, fga, ts.FreeThrowAttempted),
            EffectiveFieldGoal(ts.FieldGoalsMade, ts.ThreePointMade, fga),
            TurnoverRate(ts.Turnovers, fga, ts.FreeThrowAttempted),
            Ratio(ts.OffensiveRebounds, ts.OffensiveRebounds + os.DefensiveRebounds),
            Ratio(ts.FreeThrowAttempted, fga));
    }

    /// <summary>Advanced line of a player, given his box score and his and the opponent's team totals.</summary>
    public static PlayerAdvancedLine Player(BoxScore player, BoxScore team, BoxScore opponent)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(opponent);
        var p = player;
        var fgm = p.TwoPointMade + p.ThreePointMade;
        var fga = p.TwoPointAttempted + p.ThreePointAttempted;
        var teamFgm = team.TwoPointMade + team.ThreePointMade;
        var teamFga = team.TwoPointAttempted + team.ThreePointAttempted;
        var share = team.Minutes / 5.0;
        double OnCourt(double value, double total) => p.Minutes == 0 ? 0 : Ratio(value * share, p.Minutes * total);
        return new PlayerAdvancedLine(
            TrueShooting(p.Points, fga, p.FreeThrowAttempted),
            EffectiveFieldGoal(fgm, p.ThreePointMade, fga),
            OnCourt(fga + (FreeThrowWeight * p.FreeThrowAttempted) + p.Turnovers, teamFga + (FreeThrowWeight * team.FreeThrowAttempted) + team.Turnovers),
            p.Minutes == 0 || share == 0 ? 0 : Math.Max(0, Ratio(p.Assists, (p.Minutes / share * teamFgm) - fgm)),
            OnCourt(p.OffensiveRebounds, team.OffensiveRebounds + opponent.DefensiveRebounds),
            OnCourt(p.DefensiveRebounds, team.DefensiveRebounds + opponent.OffensiveRebounds),
            OnCourt(p.Rebounds, team.Rebounds + opponent.Rebounds),
            TurnoverRate(p.Turnovers, fga, p.FreeThrowAttempted),
            p.OffensivePossessions == 0 ? null : 100.0 * p.PointsFor / p.OffensivePossessions,
            p.DefensivePossessions == 0 ? null : 100.0 * p.PointsAgainst / p.DefensivePossessions,
            p.PlusMinus);
    }

    /// <summary>Advanced lines of a team's players for one match.</summary>
    public static IReadOnlyList<PlayerAdvancedLine> Players(IReadOnlyList<BoxScore> team, IEnumerable<BoxScore> opponent)
    {
        ArgumentNullException.ThrowIfNull(team);
        var t = Total(team);
        var o = Total(opponent);
        return [.. team.Select(p => Player(p, t, o))];
    }

    private static double Ratio(double value, double total) => total <= 0 ? 0 : value / total;
}
