using System.Text.Json.Serialization;
using Ebm.Domain.Common;
using Ebm.Domain.Matches;

namespace Ebm.Domain.Competitions.Rules;

/// <summary>How the ties of a knockout round are formed.</summary>
public enum CupDraw
{
    /// <summary>A fixed bracket by seed (1 v 8, 4 v 5, 2 v 7, 3 v 6), no draw: the top two seeds can only meet in the final.</summary>
    Bracket,

    /// <summary>Every round is drawn: the better half of the seeds is drawn against the other half.</summary>
    Seeded,

    /// <summary>Every round is an open draw: any club can meet any other.</summary>
    Open,
}

/// <summary>Who hosts a single-game tie.</summary>
public enum CupHost
{
    /// <summary>The better seed (e.g. the higher-placed club).</summary>
    HigherSeed,

    /// <summary>The weaker seed (e.g. the lower-division club), as in many national cups.</summary>
    LowerSeed,
}

/// <summary>
/// The format of a cup competition (<see cref="CompetitionRules.Cup"/>): optional qualification from a league table,
/// an optional group stage, then knockout rounds of single games or two-legged ties ending with the final.
/// Documented in docs/CUP_FORMATS.md.
/// </summary>
public sealed record CupRules
{
    public const int MaxRounds = 6;
    public const int MaxStartOffsetDays = 300;

    /// <summary>Knockout rounds, first round first; the last one is the final.</summary>
    public IReadOnlyList<CupRound> Rounds { get; init; } = [];

    /// <summary>Clubs taken from a league table; <c>null</c> = the clubs entered (registered) in the cup.</summary>
    public CupQualification? Qualification { get; init; }

    /// <summary>Round-robin groups before the knockout rounds; <c>null</c> = straight knockout.</summary>
    public CupGroupStage? GroupStage { get; init; }

    public CupDraw Draw { get; init; } = CupDraw.Seeded;

    public CupHost Host { get; init; } = CupHost.HigherSeed;

    /// <summary>Days after the season start of the first cup games (later when the qualification is decided later).</summary>
    public int StartOffsetDays { get; init; }

    /// <summary>Days from the last game of a round (or the group stage) to the next round.</summary>
    public int DaysBetweenRounds { get; init; } = 7;

    /// <summary>Days between the two legs of a tie.</summary>
    public int DaysBetweenLegs { get; init; } = 7;

    /// <summary>
    /// Clubs qualified each season from the previous season's results (international competitions,
    /// docs/INTERNATIONAL_COMPETITIONS.md); <c>null</c> = the clubs entered, or <see cref="Qualification"/>.
    /// </summary>
    public CupEntry? Entry { get; init; }

    /// <summary>Prize money and reputation paid to the clubs when the cup is over; <c>null</c> = none.</summary>
    public CupRewards? Rewards { get; init; }

    /// <summary>Clubs that start the knockout rounds: <c>2^rounds</c> including byes.</summary>
    [JsonIgnore]
    public int BracketSize => 1 << Rounds.Count;

    /// <summary>The number of clubs the cup needs: fixed by the qualification, else any count the rounds can take.</summary>
    internal void ValidateForClubs(int clubs)
    {
        if (Qualification is { } qualification)
        {
            CompetitionRules.Require(clubs == qualification.Positions, $"the cup takes the top {qualification.Positions} clubs, but {clubs} are entered");
        }

        var knockout = clubs;
        if (GroupStage is { } groups)
        {
            CompetitionRules.Require(clubs >= groups.Groups * Math.Max(2, groups.Advance + 1) && clubs <= groups.Groups * CupGroupStage.MaxGroupSize,
                $"{clubs} clubs cannot make {groups.Groups} groups of {groups.Advance + 1} to {CupGroupStage.MaxGroupSize} clubs");
            knockout = groups.Groups * groups.Advance;
        }

        // Byes only in the first round: more than half the bracket must play.
        CompetitionRules.Require(knockout <= BracketSize && knockout > BracketSize / 2,
            $"{Rounds.Count} knockout rounds take {(BracketSize / 2) + 1} to {BracketSize} clubs, but {knockout} reach them");
    }

    internal void Validate()
    {
        CompetitionRules.Require(Rounds is { Count: >= 1 and <= MaxRounds }, $"cup.rounds needs 1 to {MaxRounds} knockout rounds (the last is the final)");
        foreach (var round in Rounds!)
        {
            CompetitionRules.Require(round is not null && !string.IsNullOrWhiteSpace(round.Name), "every cup round needs a name");
            CompetitionRules.Require(round!.Legs is 1 or 2, "cup round legs must be 1 (single game) or 2 (home and away, on aggregate)");
            CompetitionRules.Require(round.DaysAfter is null or (>= 1 and <= 120), "cup round daysAfter must be 1 to 120");
            CompetitionRules.Require(!round.Neutral || round.Legs == 1, "a neutral-venue cup round must be a single game");
        }

        CompetitionRules.Require(Entry is null || Qualification is null, "use either cup.entry or cup.qualification");
        Entry?.Validate();
        Rewards?.Validate();

        Guard.Defined(Draw, "Rules: cup.draw");
        Guard.Defined(Host, "Rules: cup.host");
        Guard.Range(StartOffsetDays, 0, MaxStartOffsetDays, "Rules: cup.startOffsetDays");
        Guard.Range(DaysBetweenRounds, 1, 60, "Rules: cup.daysBetweenRounds");
        Guard.Range(DaysBetweenLegs, 1, 30, "Rules: cup.daysBetweenLegs");
        Qualification?.Validate();
        GroupStage?.Validate();
        if (GroupStage is { } groups)
        {
            var qualified = groups.Groups * groups.Advance;
            CompetitionRules.Require(qualified <= BracketSize && qualified > BracketSize / 2,
                $"the group stage sends {qualified} clubs to {Rounds.Count} knockout rounds, which take {(BracketSize / 2) + 1} to {BracketSize}");
        }

        if (Qualification is { } qualification)
        {
            ValidateForClubs(qualification.Positions);
        }
    }
}

/// <summary>A knockout round.</summary>
public sealed record CupRound
{
    public string Name { get; init; } = string.Empty;

    /// <summary>1 = a single game; 2 = home and away, decided on aggregate (overtime in the second leg when it is level).</summary>
    public int Legs { get; init; } = 1;

    /// <summary>Days from the previous round's last game to this round (e.g. 1 for a Final Four weekend); <c>null</c> = <see cref="CupRules.DaysBetweenRounds"/>.</summary>
    public int? DaysAfter { get; init; }

    /// <summary>Played at a neutral venue (a Final Four): no home advantage. Single games only.</summary>
    public bool Neutral { get; init; }
}

/// <summary>Where the clubs of an <see cref="CupEntrySlot"/> come from.</summary>
public enum CupSlotType
{
    /// <summary>Last season's winner of this competition (the title holder).</summary>
    Holder,

    /// <summary>Last season's winner of another competition (<see cref="CupEntrySlot.Competition"/>, by short name).</summary>
    Winner,

    /// <summary>The best clubs of each listed country's league at a division level, by last season's final table.</summary>
    League,

    /// <summary>The clubs of highest reputation among those playing a league at the division level (a wildcard ranking).</summary>
    Reputation,
}

/// <summary>
/// Season-by-season qualification of an international competition. Every season the slots are filled in order
/// until the competition has <c>registration.maxTeams</c> clubs; a club takes one competition only, the lowest
/// <see cref="Tier"/> (EuroLeague 1, EuroCup 2, …), so a club already qualified elsewhere is skipped and the next
/// club takes its place. Clubs entered by hand (wildcards) keep their place. Documented in docs/INTERNATIONAL_COMPETITIONS.md.
/// </summary>
public sealed record CupEntry
{
    public const int MaxTier = 10;

    public int Tier { get; init; } = 1;

    public IReadOnlyList<CupEntrySlot> Slots { get; init; } = [];

    internal void Validate()
    {
        Guard.Range(Tier, 1, MaxTier, "Rules: cup.entry.tier");
        CompetitionRules.Require(Slots is { Count: > 0 }, "cup.entry.slots needs at least one slot");
        foreach (var slot in Slots)
        {
            CompetitionRules.Require(slot is not null, "cup.entry.slots cannot contain null");
            slot!.Validate();
        }
    }
}

/// <summary>One qualification route (<see cref="CupEntry"/>).</summary>
public sealed record CupEntrySlot
{
    public CupSlotType Type { get; init; }

    /// <summary>Short name of the competition whose winner qualifies (<see cref="CupSlotType.Winner"/>).</summary>
    public string? Competition { get; init; }

    /// <summary>ISO alpha-3 codes (<see cref="CupSlotType.League"/>: each country in turn; <see cref="CupSlotType.Reputation"/>: empty = any).</summary>
    public IReadOnlyList<string> Countries { get; init; } = [];

    /// <summary>Division level of the leagues the clubs come from.</summary>
    public int Level { get; init; } = 1;

    /// <summary>Clubs taken: per country for <see cref="CupSlotType.League"/>; for <see cref="CupSlotType.Reputation"/> 0 = until the competition is full.</summary>
    public int Clubs { get; init; } = 1;

    internal void Validate()
    {
        Guard.Defined(Type, "Rules: cup.entry.slots.type");
        Guard.Range(Level, 1, 10, "Rules: cup.entry.slots.level");
        Guard.Range(Clubs, Type == CupSlotType.Reputation ? 0 : 1, RegistrationRules.TeamLimit, "Rules: cup.entry.slots.clubs");
        CompetitionRules.Require(Countries is not null && Countries.All(c => c is { Length: 3 } && c.All(char.IsAsciiLetter)),
            "cup.entry.slots.countries must list ISO 3166-1 alpha-3 codes");
        CompetitionRules.Require(Type != CupSlotType.League || Countries!.Count > 0, "a League entry slot needs its countries");
        CompetitionRules.Require(Type != CupSlotType.Winner || !string.IsNullOrWhiteSpace(Competition), "a Winner entry slot needs the competition's short name");
    }
}

/// <summary>
/// Paid once the cup is over: <see cref="Entry"/> to every club, <see cref="Win"/> per game won, <see cref="Knockout"/>
/// to the clubs reaching the knockout rounds after a group stage, and the finalists' prizes; reputation alike (club
/// reputation is capped at 100).
/// </summary>
public sealed record CupRewards
{
    public const long MaxPrize = 100_000_000;
    public const int MaxReputation = 10;

    public long Entry { get; init; }

    public long Win { get; init; }

    public long Knockout { get; init; }

    public long Winner { get; init; }

    public long RunnerUp { get; init; }

    public int ReputationEntry { get; init; }

    public int ReputationKnockout { get; init; }

    public int ReputationWinner { get; init; }

    public int ReputationRunnerUp { get; init; }

    internal void Validate()
    {
        foreach (var (value, name) in new[] { (Entry, "entry"), (Win, "win"), (Knockout, "knockout"), (Winner, "winner"), (RunnerUp, "runnerUp") })
        {
            CompetitionRules.Require(value is >= 0 and <= MaxPrize, $"cup.rewards.{name} must be 0 to {MaxPrize:N0} euros");
        }

        foreach (var (value, name) in new[] { (ReputationEntry, "reputationEntry"), (ReputationKnockout, "reputationKnockout"), (ReputationWinner, "reputationWinner"), (ReputationRunnerUp, "reputationRunnerUp") })
        {
            Guard.Range(value, 0, MaxReputation, $"Rules: cup.rewards.{name}");
        }
    }
}

/// <summary>
/// The cup takes the top <see cref="Positions"/> clubs of a division of the cup's country (the division at
/// <see cref="Level"/> of a league of that country) from its regular-season table after round <see cref="AfterRound"/>
/// (e.g. the table at the halfway point for a Final Eight). Table positions are the seeds.
/// </summary>
public sealed record CupQualification
{
    public int Level { get; init; } = 1;

    public int Positions { get; init; }

    public int AfterRound { get; init; }

    internal void Validate()
    {
        Guard.Range(Level, 1, 10, "Rules: cup.qualification.level");
        Guard.Range(Positions, 2, RegistrationRules.TeamLimit, "Rules: cup.qualification.positions");
        Guard.Range(AfterRound, 1, Fixture.MaxRound, "Rules: cup.qualification.afterRound");
    }
}

/// <summary>
/// Groups drawn from pots of seeds (one club of each pot per group), a round robin in each, the top
/// <see cref="Advance"/> of each group to the knockout rounds (group winners seeded first).
/// </summary>
public sealed record CupGroupStage
{
    public const int MaxGroups = 8;

    /// <summary>Up to 20: one group can be a whole league phase (the EuroLeague's 20-club round robin).</summary>
    public const int MaxGroupSize = 20;

    public int Groups { get; init; } = 2;

    public int Legs { get; init; } = 2;

    public int Advance { get; init; } = 2;

    public int DaysBetweenRounds { get; init; } = 7;

    internal void Validate()
    {
        Guard.Range(Groups, 1, MaxGroups, "Rules: cup.groupStage.groups");
        Guard.Range(Legs, 1, RegularSeasonRules.MaxLegs, "Rules: cup.groupStage.legs");
        Guard.Range(Advance, 1, MaxGroupSize - 1, "Rules: cup.groupStage.advance");
        Guard.Range(DaysBetweenRounds, 1, 30, "Rules: cup.groupStage.daysBetweenRounds");
        CompetitionRules.Require(Groups * Advance >= 2, "the group stage must send at least two clubs on");
    }
}
