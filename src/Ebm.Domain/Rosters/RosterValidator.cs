using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;

namespace Ebm.Domain.Rosters;

/// <summary>A club player as the roster rules see him, prepared by the caller for a given date.</summary>
/// <param name="LineupFamiliarity">Familiarity with <paramref name="LineupPosition"/>; <c>null</c> for non-starters.</param>
/// <param name="HasContract">A contract with the club is running on the validation date.</param>
/// <param name="IsNonEu">His nationality is outside the EU/EEA (counts against <see cref="SquadRules.MaxNonEuPlayers"/>).</param>
public sealed record RosterMember(
    PlayerId PlayerId,
    string Name,
    RosterRole Role,
    Position? LineupPosition,
    PlayerAvailability Availability,
    bool IsForeign,
    int Age,
    PositionFamiliarity? LineupFamiliarity,
    bool HasContract,
    bool IsNonEu = false);

public enum RosterIssueSeverity
{
    Error,
    Warning,
}

/// <summary>Every rule the validator checks. Errors make the roster invalid; warnings are advice.</summary>
public enum RosterIssueCode
{
    StartingFiveIncomplete,
    StartingPositionTaken,
    UnavailableStarter,
    TooFewPlayers,
    TooManyPlayers,
    MatchdaySquadTooLarge,
    TooManyForeignPlayers,
    TooManyNonEuPlayers,
    TooFewDomesticPlayers,
    UnderMinimumAge,
    DoubtfulStarter,
    UnavailableInSquad,
    UnfamiliarPosition,
    ShortRotation,
    NoContract,
}

/// <param name="PlayerId">The player concerned, when the issue is about one player.</param>
public sealed record RosterIssue(RosterIssueSeverity Severity, RosterIssueCode Code, string Message, PlayerId? PlayerId = null);

public sealed record RosterValidation(IReadOnlyList<RosterIssue> Issues)
{
    public bool IsValid => Issues.All(i => i.Severity != RosterIssueSeverity.Error);

    public IEnumerable<RosterIssue> Errors => Issues.Where(i => i.Severity == RosterIssueSeverity.Error);

    public IEnumerable<RosterIssue> Warnings => Issues.Where(i => i.Severity == RosterIssueSeverity.Warning);

    public bool Has(RosterIssueCode code) => Issues.Any(i => i.Code == code);
}

/// <summary>Finds invalid roster configurations. Pure: the same members and rules always give the same issues.</summary>
/// <remarks>Rules and their reasons: docs/ROSTER_RULES.md.</remarks>
public static class RosterValidator
{
    /// <summary>Lineup, availability and squad-size checks: everything a club's roster must satisfy.</summary>
    public static RosterValidation Validate(IReadOnlyList<RosterMember> members, SquadRules rules)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(rules);
        var issues = new List<RosterIssue>();
        CheckLineup(members, rules, issues);
        CheckRegistration(members, rules, issues);
        CheckContracts(members, issues);
        return new RosterValidation(issues);
    }

    /// <summary>Only the registration checks (squad size, nationality, age): used per competition.</summary>
    public static RosterValidation ValidateRegistration(IReadOnlyList<RosterMember> members, SquadRules rules)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(rules);
        var issues = new List<RosterIssue>();
        CheckRegistration(members, rules, issues);
        return new RosterValidation(issues);
    }

    private static void CheckLineup(IReadOnlyList<RosterMember> members, SquadRules rules, List<RosterIssue> issues)
    {
        var starters = members.Where(m => m.Role == RosterRole.Starter).ToList();
        if (starters.Count != SquadRules.Starters)
        {
            issues.Add(Error(RosterIssueCode.StartingFiveIncomplete, $"The starting five has {starters.Count} players; it needs {SquadRules.Starters}."));
        }

        foreach (var group in starters.GroupBy(s => s.LineupPosition!.Value).Where(g => g.Count() > 1))
        {
            issues.Add(Error(RosterIssueCode.StartingPositionTaken,
                $"{string.Join(" and ", group.Select(s => s.Name))} all start at {PlayerCatalog.DisplayName(group.Key)}; each starter needs a different position."));
        }

        foreach (var starter in starters)
        {
            if (starter.Availability == PlayerAvailability.Unavailable)
            {
                issues.Add(Error(RosterIssueCode.UnavailableStarter, $"{starter.Name} is injured and cannot start.", starter.PlayerId));
            }
            else if (starter.Availability == PlayerAvailability.Doubtful)
            {
                issues.Add(Warning(RosterIssueCode.DoubtfulStarter, $"{starter.Name} is day-to-day and may miss the game.", starter.PlayerId));
            }

            if (starter.LineupFamiliarity == PositionFamiliarity.Unfamiliar)
            {
                issues.Add(Warning(RosterIssueCode.UnfamiliarPosition,
                    $"{starter.Name} starts at {PlayerCatalog.DisplayName(starter.LineupPosition!.Value)}, a position he is not used to.", starter.PlayerId));
            }
        }

        foreach (var player in members.Where(m => m.Role is RosterRole.Rotation or RosterRole.Bench && m.Availability == PlayerAvailability.Unavailable))
        {
            issues.Add(Warning(RosterIssueCode.UnavailableInSquad, $"{player.Name} is injured but in the matchday squad.", player.PlayerId));
        }

        var rotation = members.Count(m => m.Role is RosterRole.Starter or RosterRole.Rotation && m.Availability != PlayerAvailability.Unavailable);
        if (rotation < rules.RecommendedRotation)
        {
            issues.Add(Warning(RosterIssueCode.ShortRotation,
                $"Only {rotation} available players in the starting five and rotation; {rules.RecommendedRotation} are recommended."));
        }
    }

    private static void CheckRegistration(IReadOnlyList<RosterMember> members, SquadRules rules, List<RosterIssue> issues)
    {
        var firstTeam = members.Where(m => m.Role.IsFirstTeam()).ToList();
        if (firstTeam.Count < rules.MinPlayers)
        {
            issues.Add(Error(RosterIssueCode.TooFewPlayers, $"The first team has {firstTeam.Count} players; at least {rules.MinPlayers} are required."));
        }

        if (firstTeam.Count > rules.MaxPlayers)
        {
            issues.Add(Error(RosterIssueCode.TooManyPlayers,
                $"The first team has {firstTeam.Count} players; at most {rules.MaxPlayers} can be registered. Move players to the youth squad or release them."));
        }

        var active = members.Count(m => m.Role.IsActive());
        if (active > rules.MatchdayPlayers)
        {
            issues.Add(Error(RosterIssueCode.MatchdaySquadTooLarge,
                $"{active} players are in the matchday squad (starters, rotation and bench); at most {rules.MatchdayPlayers} are allowed. Make the rest inactive."));
        }

        var foreign = firstTeam.Count(m => m.IsForeign);
        if (rules.MaxForeignPlayers is { } maxForeign && foreign > maxForeign)
        {
            issues.Add(Error(RosterIssueCode.TooManyForeignPlayers, $"The first team has {foreign} foreign players; at most {maxForeign} are allowed."));
        }

        var nonEu = firstTeam.Count(m => m.IsNonEu);
        if (rules.MaxNonEuPlayers is { } maxNonEu && nonEu > maxNonEu)
        {
            issues.Add(Error(RosterIssueCode.TooManyNonEuPlayers, $"The first team has {nonEu} non-EU players; at most {maxNonEu} are allowed."));
        }

        var domestic = firstTeam.Count - foreign;
        if (domestic < rules.MinDomesticPlayers)
        {
            issues.Add(Error(RosterIssueCode.TooFewDomesticPlayers, $"The first team has {domestic} domestic players; at least {rules.MinDomesticPlayers} are required."));
        }

        if (rules.MinAge is { } minAge)
        {
            foreach (var player in firstTeam.Where(m => m.Age < minAge))
            {
                issues.Add(Error(RosterIssueCode.UnderMinimumAge, $"{player.Name} is {player.Age}; first-team players must be at least {minAge}.", player.PlayerId));
            }
        }
    }

    private static void CheckContracts(IReadOnlyList<RosterMember> members, List<RosterIssue> issues)
    {
        foreach (var player in members.Where(m => m.Role.IsFirstTeam() && !m.HasContract))
        {
            issues.Add(Warning(RosterIssueCode.NoContract, $"{player.Name} has no running contract with the club.", player.PlayerId));
        }
    }

    private static RosterIssue Error(RosterIssueCode code, string message, PlayerId? player = null) => new(RosterIssueSeverity.Error, code, message, player);

    private static RosterIssue Warning(RosterIssueCode code, string message, PlayerId? player = null) => new(RosterIssueSeverity.Warning, code, message, player);
}
