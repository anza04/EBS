namespace Ebm.Domain.News;

/// <summary>What the fans expect from their club's league season (docs/NEWS.md §5).</summary>
public enum FanExpectation
{
    Title,
    Playoffs,
    MidTable,
    Survival,
}

/// <summary>How the fans feel about the season so far; the value is the story sentiment.</summary>
public enum FanMood
{
    Angry = -2,
    Restless = -1,
    Content = 1,
    Delighted = 2,
}

/// <summary>Pure media rules: reputation points, fan expectations and mood, streaks (docs/NEWS.md).</summary>
public static class MediaRules
{
    /// <summary>A win is an upset when the loser's reputation is at least this much higher.</summary>
    public const int UpsetGap = 15;

    /// <summary>A run of this many wins (or defeats) makes a media story.</summary>
    public const int StreakLength = 5;

    /// <summary>Fans judge the season after this many league games.</summary>
    public const int FanJudgementGames = 5;

    /// <summary>Rumours about a contract running down start this many days before it ends.</summary>
    public const int ContractRumorDays = 120;

    /// <summary>Clubs outside the user's club get injury stories from this severity.</summary>
    public const Medical.InjurySeverity HeadlineSeverity = Medical.InjurySeverity.Serious;

    /// <summary>Reputation points of an event; <see cref="ReputationCause.CompetitionReward"/> comes from the competition's rules (0 here).</summary>
    public static int Points(ReputationCause cause) => cause switch
    {
        ReputationCause.UpsetWin => 1,
        ReputationCause.UpsetDefeat => -1,
        ReputationCause.LeagueTitle => 3,
        ReputationCause.CupWin => 2,
        ReputationCause.Promotion => 2,
        ReputationCause.Relegation => -3,
        ReputationCause.ObjectiveMet => 1,
        ReputationCause.ObjectiveMissed => -1,
        _ => 0,
    };

    /// <summary>The winner beat a club whose reputation is at least <see cref="UpsetGap"/> higher.</summary>
    public static bool IsUpset(int winnerReputation, int loserReputation) => loserReputation - winnerReputation >= UpsetGap;

    /// <summary>
    /// The fans' expectation from the club's reputation rank among the <paramref name="clubs"/> of its league (1 = highest):
    /// the top club expects the title, the top third the playoffs, the bottom third survival.
    /// </summary>
    public static FanExpectation Expect(int reputationRank, int clubs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(clubs, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(reputationRank, 1);
        return reputationRank == 1 && clubs > 1 ? FanExpectation.Title
            : reputationRank <= Math.Max(1, clubs / 3) ? FanExpectation.Playoffs
            : reputationRank <= (clubs * 2 + 2) / 3 ? FanExpectation.MidTable
            : FanExpectation.Survival;
    }

    /// <summary>The worst league position that still meets <paramref name="expectation"/>.</summary>
    public static int TargetPosition(FanExpectation expectation, int clubs) => expectation switch
    {
        FanExpectation.Title => 1,
        FanExpectation.Playoffs => Math.Max(1, clubs / 3),
        FanExpectation.MidTable => (clubs * 2 + 2) / 3,
        _ => Math.Max(1, clubs - 2),
    };

    /// <summary>
    /// The fans' mood: positions better than the target and recent wins please them. Score = (target − position) /
    /// max(1, clubs / 6) + (wins in the last five − 2.5) / 2.5; ≥ 1 delighted, ≥ 0 content, ≥ −1 restless, else angry.
    /// </summary>
    public static FanMood Mood(FanExpectation expectation, int position, int clubs, int recentWins, int recentGames)
    {
        var form = recentGames == 0 ? 0 : ((recentWins * 5.0 / recentGames) - 2.5) / 2.5;
        var score = ((TargetPosition(expectation, clubs) - position) / (double)Math.Max(1, clubs / 6)) + form;
        return score >= 1 ? FanMood.Delighted : score >= 0 ? FanMood.Content : score >= -1 ? FanMood.Restless : FanMood.Angry;
    }

    /// <summary>The current run from the most recent result backwards: positive for wins, negative for defeats.</summary>
    public static int Streak(IEnumerable<bool> resultsNewestFirst)
    {
        ArgumentNullException.ThrowIfNull(resultsNewestFirst);
        var run = 0;
        foreach (var won in resultsNewestFirst)
        {
            if (run != 0 && won != run > 0)
            {
                break;
            }

            run += won ? 1 : -1;
        }

        return run;
    }
}
