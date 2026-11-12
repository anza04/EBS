using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Matches;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;

namespace Ebm.Services.Matches;

/// <summary>
/// Plays fixtures with the match engine and stores their results and box scores (docs/MATCH_ENGINE.md §6). One DI
/// scope per call; rule violations throw <see cref="Domain.Common.DomainException"/>.
/// </summary>
public interface IMatchService
{
    /// <summary>The fixtures of a competition season in date order, with their results.</summary>
    Task<IReadOnlyList<FixtureLine>> GetFixturesAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default);

    /// <summary>Both teams' matchday squads and tactics as they are now, or <c>null</c> when the fixture does not exist.</summary>
    Task<MatchPreview?> GetPreviewAsync(FixtureId fixtureId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a match: squads come from the clubs' rosters, tactics from their selected tactics. Nothing is stored until
    /// <see cref="SaveAsync"/>. <paramref name="seed"/> makes the match reproducible (random when omitted).
    /// </summary>
    Task<MatchSession> StartAsync(FixtureId fixtureId, SimulationMode mode, int? seed = null, CancellationToken cancellationToken = default);

    /// <summary>Stores a finished match: result, standings and every player's box score, in one transaction.</summary>
    Task<MatchId> SaveAsync(MatchSession session, CancellationToken cancellationToken = default);

    /// <summary>Instant mode: simulates the fixture to the end and stores it.</summary>
    Task<MatchReport> PlayAsync(FixtureId fixtureId, int? seed = null, CancellationToken cancellationToken = default);

    /// <summary>A stored match with its box score, or <c>null</c> when it does not exist.</summary>
    Task<MatchReport?> GetReportAsync(MatchId matchId, CancellationToken cancellationToken = default);
}

/// <summary>One fixture of a schedule. <paramref name="Match"/> is set once played.</summary>
public sealed record FixtureLine(Fixture Fixture, string HomeClub, string AwayClub, Match? Match);

/// <summary>What the match preview shows.</summary>
public sealed record MatchPreview(Fixture Fixture, string Competition, TeamSheet Home, TeamSheet Away, Match? Result);

/// <summary>A match in progress: the fixture and its simulation. The UI drives the simulation, then saves the session.</summary>
public sealed class MatchSession
{
    internal MatchSession(Fixture fixture, string competition, MatchSimulation simulation)
    {
        Fixture = fixture;
        Competition = competition;
        Simulation = simulation;
    }

    public Fixture Fixture { get; }

    public string Competition { get; }

    public MatchSimulation Simulation { get; }

    public bool IsSaved { get; internal set; }
}

/// <summary>One player's line of a stored box score.</summary>
public sealed record BoxScoreLine(PlayerId PlayerId, string Player, BoxScore BoxScore);

/// <summary>A stored match: result, box scores of both teams and team statistics.</summary>
public sealed record MatchReport(
    Match Match,
    Fixture Fixture,
    string Competition,
    Club Home,
    Club Away,
    IReadOnlyList<BoxScoreLine> HomeLines,
    IReadOnlyList<BoxScoreLine> AwayLines)
{
    public TeamStatistics HomeStatistics => TeamStatistics.From(HomeLines.Select(l => l.BoxScore));

    public TeamStatistics AwayStatistics => TeamStatistics.From(AwayLines.Select(l => l.BoxScore));
}
