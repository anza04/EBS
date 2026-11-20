using System.Globalization;
using Ebm.Domain.Medical;
using Ebm.Domain.Transfers;

namespace Ebm.Domain.News;

/// <summary>A written story: headline, body and tone (−2…+2).</summary>
public sealed record Story(string Headline, string Body, int Sentiment = 0);

/// <summary>A player's line in a match story.</summary>
public sealed record StarLine(string Name, int Points, int Rebounds, int Assists)
{
    public string Text => string.Create(CultureInfo.InvariantCulture, $"{Name} ({Points} pts, {Rebounds} reb, {Assists} ast)");
}

/// <summary>A played match seen from one club.</summary>
public sealed record MatchStory(
    string Club,
    string Opponent,
    bool Home,
    int ClubScore,
    int OpponentScore,
    int Overtimes,
    string Competition,
    StarLine? Star,
    StarLine? OpponentStar,
    int Streak)
{
    public bool Won => ClubScore > OpponentScore;

    public int Margin => Math.Abs(ClubScore - OpponentScore);

    public string Score => string.Create(CultureInfo.InvariantCulture, $"{ClubScore}–{OpponentScore}{(Overtimes > 0 ? Overtimes == 1 ? " (OT)" : $" ({Overtimes} OT)" : string.Empty)}");
}

/// <summary>A club's side of an upcoming match.</summary>
public sealed record PreviewSide(string Club, int? Position, string Form, IReadOnlyList<string> Unavailable);

/// <summary>
/// Writes news stories from game facts (docs/NEWS.md §3). Deterministic: the wording variant is picked from the story
/// key, so a story reads the same every time it is written; every number in a story comes from the facts given.
/// </summary>
public static class NewsWriter
{
    public static Story Review(string key, MatchStory m)
    {
        var verb = m.Won
            ? m.Margin >= 20 ? Pick(key, "crush", "rout", "thrash") : m.Margin <= 3 ? Pick(key, "edge", "survive", "hold off") : Pick(key, "beat", "defeat", "see off")
            : m.Margin >= 20 ? Pick(key, "crushed by", "routed by", "thrashed by") : m.Margin <= 3 ? Pick(key, "edged by", "pipped by", "beaten at the death by") : Pick(key, "beaten by", "fall to", "lose to");
        var venue = m.Home ? "at home" : "on the road";
        var body = $"{m.Club} {(m.Won ? "won" : "lost")} {m.Score} {venue} against {m.Opponent} ({m.Competition}).";
        if (m.Star is { } star)
        {
            body += $" {star.Text} led {m.Club}.";
        }

        if (m.OpponentStar is { } other)
        {
            body += $" For {m.Opponent}, {other.Text}.";
        }

        if (Math.Abs(m.Streak) >= 3)
        {
            body += string.Create(CultureInfo.InvariantCulture, $" That is {Math.Abs(m.Streak)} {(m.Streak > 0 ? "wins" : "defeats")} in a row.");
        }

        return new($"{m.Club} {verb} {m.Opponent} {m.ClubScore}–{m.OpponentScore}", body, m.Won ? m.Margin >= 15 ? 2 : 1 : m.Margin >= 15 ? -2 : -1);
    }

    public static Story PressConference(string key, string manager, MatchStory m)
    {
        var quote = m.Won
            ? m.Margin >= 15 ? Pick(key, "That was close to our best basketball. Everybody did their job.", "We controlled the game from the start. I am proud of the group.")
                : Pick(key, "A hard-fought win. We kept our composure when it mattered.", "Not pretty, but we found a way. That is what good teams do.")
            : m.Margin >= 15 ? Pick(key, "Unacceptable. We have to look at ourselves, starting with me.", "We were second best in every department tonight. We will work.")
                : Pick(key, "We were one or two plays away. Credit to them, but we will learn from it.", "Small details decided it. The effort was there; the execution was not.");
        var streak = m.Streak <= -3 ? $" Asked about the run of {-m.Streak} defeats: \"We stay together. Nobody is hiding.\""
            : m.Streak >= 3 ? $" On the run of {m.Streak} wins: \"We take it one game at a time.\"" : string.Empty;
        return new(
            $"{manager}: \"{(m.Won ? Pick(key + "h", "Proud of the group", "We found a way", "A big win") : Pick(key + "h", "We must be better", "Small details decided it", "We will learn from this"))}\"",
            $"{manager} after {m.Club} {m.Score} {m.Opponent}: \"{quote}\"{streak}",
            m.Won ? 1 : -1);
    }

    public static Story Interview(string key, StarLine star, MatchStory m) => new(
        $"{star.Name}: \"{(m.Won ? Pick(key, "The team made it easy for me", "The win is what counts") : Pick(key, "My numbers mean nothing after a defeat", "We have to bounce back"))}\"",
        string.Create(CultureInfo.InvariantCulture, $"{star.Name} spoke after {m.Club} {m.Score} {m.Opponent}, with {star.Points} points, {star.Rebounds} rebounds and {star.Assists} assists. ")
            + (m.Won ? "\"I just took what the defence gave me. Everybody contributed.\"" : "\"Individual numbers mean nothing if we lose. We go back to work tomorrow.\""),
        m.Won ? 1 : 0);

    public static Story Preview(string key, PreviewSide club, PreviewSide opponent, bool home, string competition, DateOnly date)
    {
        static string Side(PreviewSide s) =>
            $"{s.Club}{(s.Position is { } p ? string.Create(CultureInfo.InvariantCulture, $" ({Ordinal(p)})") : string.Empty)}{(s.Form.Length > 0 ? $", form {s.Form}" : string.Empty)}"
            + (s.Unavailable.Count > 0 ? $"; unavailable: {string.Join(", ", s.Unavailable)}" : "; full squad available");
        var (homeSide, awaySide) = home ? (club, opponent) : (opponent, club);
        return new(
            $"Preview: {homeSide.Club} v {awaySide.Club}",
            string.Create(CultureInfo.InvariantCulture, $"{competition}, {date:ddd d MMM}. ") + $"Home: {Side(homeSide)}. Away: {Side(awaySide)}. "
                + Pick(key, "A test of where the season is heading.", "Both coaches know what is at stake.", "Expect a tight contest."));
    }

    public static Story Transfer(TransferKind kind, string player, string? from, string to, long fee, DateOnly? loanUntil, bool rumoured)
    {
        var confirmed = rumoured ? " The move confirms the reports of the last weeks." : string.Empty;
        return kind switch
        {
            TransferKind.FreeAgentSigning => new($"{to} sign {player}", $"{player} has signed with {to}{(from is null ? " as a free agent" : $" after leaving {from}")}.{confirmed}"),
            TransferKind.Loan => new($"{player} joins {to} on loan", $"{player} moves from {from} to {to} on loan{(loanUntil is { } u ? string.Create(CultureInfo.InvariantCulture, $" until {u:d MMM yyyy}") : string.Empty)}{(fee > 0 ? $" for a fee of {Money(fee)}" : string.Empty)}.{confirmed}"),
            TransferKind.LoanReturn => new($"{player} returns to {to}", $"{player}'s loan at {from} is over; he is back at {to}."),
            TransferKind.ReleaseClause => new($"{to} trigger {player}'s release clause", $"{to} paid {player}'s {Money(fee)} release clause; {from} could not refuse.{confirmed}"),
            TransferKind.OptionToBuy => new($"{to} make {player}'s move permanent", $"{to} exercised the option to buy {player} from {from} for {Money(fee)}.{confirmed}"),
            _ => new($"{player} joins {to} from {from}", $"{to} have signed {player} from {from} for {Money(fee)}.{confirmed}"),
        };
    }

    public static Story Injury(string player, string club, string injury, InjurySeverity severity, InjurySource source, DateOnly expectedReturn, DateOnly today)
    {
        var weeks = Math.Max(1, (int)Math.Round((expectedReturn.DayNumber - today.DayNumber) / 7.0));
        var length = expectedReturn.DayNumber - today.DayNumber <= 7 ? "about a week" : string.Create(CultureInfo.InvariantCulture, $"about {weeks} weeks");
        return new(
            $"{club}: {player} out with {Article(injury)}",
            string.Create(CultureInfo.InvariantCulture, $"{player} ({club}) suffered {Article(injury)} in {(source == InjurySource.Match ? "a match" : "training")} ({severity.ToString().ToLowerInvariant()}). ")
                + string.Create(CultureInfo.InvariantCulture, $"The medical staff expect him back around {expectedReturn:d MMM yyyy}, {length}."),
            severity >= InjurySeverity.Serious ? -2 : -1);
    }

    public static Story ContractRumor(string key, string player, string club, DateOnly contractEnd) => new(
        Pick(key, $"{player} could leave {club} on a free", $"Rival clubs circle {club}'s {player}", $"{player}'s future at {club} in doubt"),
        string.Create(CultureInfo.InvariantCulture, $"{player}'s contract with {club} runs out on {contractEnd:d MMM yyyy} and no extension has been agreed. ")
            + "Sources close to the player say he is keeping his options open.",
        -1);

    public static Story Upset(string winner, string loser, int winnerScore, int loserScore, string competition) => new(
        $"Shock: {winner} stun {loser}",
        string.Create(CultureInfo.InvariantCulture, $"Few saw it coming: {winner} beat {loser} {winnerScore}–{loserScore} ({competition}). ")
            + $"Pundits call it one of the surprises of the season; {loser}'s supporters want answers.",
        2);

    public static Story Streak(string club, int streak) => streak > 0
        ? new(string.Create(CultureInfo.InvariantCulture, $"{club} on fire: {streak} straight wins"), string.Create(CultureInfo.InvariantCulture, $"{club} have won {streak} games in a row. The press is starting to talk about them as contenders."), 2)
        : new(string.Create(CultureInfo.InvariantCulture, $"Crisis at {club}: {-streak} defeats in a row"), string.Create(CultureInfo.InvariantCulture, $"{club} have lost {-streak} games in a row. Questions are being asked about the coach and the squad."), -2);

    public static Story Reputation(string club, ReputationCause cause, int before, int after, string because) => new(
        after > before ? $"{club}'s standing grows" : $"{club}'s standing suffers",
        string.Create(CultureInfo.InvariantCulture, $"{because} The club's reputation moves from {before} to {after} ({after - before:+0;-0}). ") + Describe(cause),
        Math.Sign(after - before));

    public static Story Expectations(string club, FanExpectation expectation, string competition, int reputationRank, int clubs) => new(
        $"{club} fans {expectation switch { FanExpectation.Title => "expect the title", FanExpectation.Playoffs => "expect a playoff run", FanExpectation.MidTable => "want a solid season", _ => "hope to avoid relegation" }}",
        string.Create(CultureInfo.InvariantCulture, $"With the {Ordinal(reputationRank)} biggest name among the {clubs} clubs of the {competition}, ")
            + expectation switch
            {
                FanExpectation.Title => "supporters will accept nothing less than the title.",
                FanExpectation.Playoffs => "supporters expect a top-third finish and a playoff run.",
                FanExpectation.MidTable => "supporters want a comfortable mid-table season.",
                _ => "supporters would settle for staying up.",
            });

    public static Story Mood(string club, FanMood mood, FanExpectation expectation, int position, int clubs, string form) => new(
        mood switch
        {
            FanMood.Delighted => $"{club} fans dream big",
            FanMood.Content => $"{club} fans happy with the season so far",
            FanMood.Restless => $"{club} fans grow restless",
            _ => $"{club} fans turn on the club",
        },
        string.Create(CultureInfo.InvariantCulture, $"{club} are {Ordinal(position)} of {clubs} (form {form}); the fans expected {expectation switch { FanExpectation.Title => "a title challenge", FanExpectation.Playoffs => "a playoff place", FanExpectation.MidTable => "mid-table", _ => "survival" }}. ")
            + mood switch
            {
                FanMood.Delighted => "The stands are full and the mood is euphoric.",
                FanMood.Content => "The supporters are behind the team.",
                FanMood.Restless => "Whistles were heard at the last home game.",
                _ => "Supporters' groups have called for changes.",
            },
        (int)mood);

    public static string Ordinal(int n) => n + ((n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    public static string Money(long euros) => euros >= 1_000_000
        ? string.Create(CultureInfo.InvariantCulture, $"€{euros / 1_000_000.0:0.##}m")
        : string.Create(CultureInfo.InvariantCulture, $"€{euros / 1000.0:0.#}k");

    private static string Describe(ReputationCause cause) => cause switch
    {
        ReputationCause.UpsetWin => "Beating a bigger club gets noticed.",
        ReputationCause.UpsetDefeat => "Losing to a much smaller club hurts.",
        ReputationCause.LeagueTitle => "Champions attract players and sponsors.",
        ReputationCause.CupWin => "A trophy in the cabinet.",
        ReputationCause.Promotion => "A step up in level.",
        ReputationCause.Relegation => "Dropping a division damages the name.",
        ReputationCause.ObjectiveMet => "The board's objective was met.",
        ReputationCause.CompetitionReward => "A good run in a competition.",
        ReputationCause.LongTermDrift => "Past glories fade; new names rise.",
        _ => "The board's objective was missed.",
    };

    private static string Article(string noun) => ("aeiou".Contains(char.ToLowerInvariant(noun[0]), StringComparison.Ordinal) ? "an " : "a ") + noun.ToLowerInvariant();

    /// <summary>A variant chosen by a stable (FNV-1a) hash of <paramref name="key"/>.</summary>
    private static string Pick(string key, params string[] options)
    {
        var hash = 2166136261u;
        foreach (var c in key)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return options[hash % (uint)options.Length];
    }
}
