using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Matches;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Seasons;
using Ebm.Services.Clubs;
using Ebm.Services.Competitions;
using Ebm.Services.Data;
using Ebm.Services.Tactics;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Matches;

/// <summary>Implements <see cref="IMatchService"/> over the rosters, the tactics and the pure <see cref="MatchSimulation"/>.</summary>
internal sealed class MatchService(
    IServiceScopeFactory scopes,
    IClubService clubs,
    ITacticsService tactics,
    PlayerRatingCalculator calculator) : IMatchService
{
    public async Task<IReadOnlyList<FixtureLine>> GetFixturesAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var fixtures = await Repo<Fixture, FixtureId>(s).ListAsync(f => f.CompetitionId == competitionId && f.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var ids = fixtures.Select(f => f.Id).ToList();
        var matches = (await Repo<Match, MatchId>(s).ListAsync(m => ids.Contains(m.FixtureId), cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.FixtureId);
        var names = (await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);
        return
        [
            .. fixtures.OrderBy(f => f.Date).ThenBy(f => f.Stage).ThenBy(f => f.Round).ThenBy(f => f.GameNumber)
                .Select(f => new FixtureLine(f, names.GetValueOrDefault(f.HomeClubId, "?"), names.GetValueOrDefault(f.AwayClubId, "?"), matches.GetValueOrDefault(f.Id))),
        ];
    }

    public async Task<MatchPreview?> GetPreviewAsync(FixtureId fixtureId, CancellationToken cancellationToken = default)
    {
        Fixture? fixture;
        string competition;
        Match? result;
        using (var scope = scopes.CreateScope())
        {
            var s = scope.ServiceProvider;
            fixture = await Repo<Fixture, FixtureId>(s).FindAsync(fixtureId, cancellationToken).ConfigureAwait(false);
            if (fixture is null)
            {
                return null;
            }

            competition = (await Repo<Competition, CompetitionId>(s).FindAsync(fixture.CompetitionId, cancellationToken).ConfigureAwait(false))?.Name ?? string.Empty;
            result = (await Repo<Match, MatchId>(s).ListAsync(m => m.FixtureId == fixtureId, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        }

        var home = await SheetAsync(fixture.HomeClubId, cancellationToken).ConfigureAwait(false);
        var away = await SheetAsync(fixture.AwayClubId, cancellationToken).ConfigureAwait(false);
        return new MatchPreview(fixture, competition, home, away, result);
    }

    public async Task<MatchSession> StartAsync(FixtureId fixtureId, SimulationMode mode, int? seed = null, CancellationToken cancellationToken = default)
    {
        var preview = await GetPreviewAsync(fixtureId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The fixture no longer exists in the database.");
        if (preview.Result is not null)
        {
            throw new DomainValidationException("This fixture has already been played.");
        }

        var setup = new MatchSetup(preview.Home, preview.Away, seed ?? Random.Shared.Next());
        return new MatchSession(preview.Fixture, preview.Competition, new MatchSimulation(setup, mode, calculator));
    }

    public async Task<MatchId> SaveAsync(MatchSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsSaved)
        {
            throw new DomainValidationException("This match has already been saved.");
        }

        var result = session.Simulation.Result();
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var match = await CompetitionService.AddResultAsync(s, session.Fixture.Id, result.HomeScore, result.AwayScore, result.Overtimes, cancellationToken).ConfigureAwait(false);
        var statistics = Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>(s);
        foreach (var side in TeamSides.Both)
        {
            var club = session.Simulation.Setup.Sheet(side).ClubId;
            foreach (var line in result.Lines(side).Where(l => l.SecondsPlayed > 0))
            {
                statistics.Add(new PlayerMatchStatistics(PlayerMatchStatisticsId.New(), match.Id, line.Player.Id, club, line.ToBoxScore()));
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        session.IsSaved = true;
        return match.Id;
    }

    public async Task<MatchReport> PlayAsync(FixtureId fixtureId, int? seed = null, CancellationToken cancellationToken = default)
    {
        var session = await StartAsync(fixtureId, SimulationMode.Instant, seed, cancellationToken).ConfigureAwait(false);
        session.Simulation.RunToEnd();
        var id = await SaveAsync(session, cancellationToken).ConfigureAwait(false);
        return (await GetReportAsync(id, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<MatchReport?> GetReportAsync(MatchId matchId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        if (await Repo<Match, MatchId>(s).FindAsync(matchId, cancellationToken).ConfigureAwait(false) is not { } match)
        {
            return null;
        }

        var fixture = (await Repo<Fixture, FixtureId>(s).FindAsync(match.FixtureId, cancellationToken).ConfigureAwait(false))!;
        var competition = (await Repo<Competition, CompetitionId>(s).FindAsync(fixture.CompetitionId, cancellationToken).ConfigureAwait(false))?.Name ?? string.Empty;
        var home = (await Repo<Club, ClubId>(s).FindAsync(fixture.HomeClubId, cancellationToken).ConfigureAwait(false))!;
        var away = (await Repo<Club, ClubId>(s).FindAsync(fixture.AwayClubId, cancellationToken).ConfigureAwait(false))!;
        var lines = await Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>(s).ListAsync(x => x.MatchId == matchId, cancellationToken).ConfigureAwait(false);
        var playerIds = lines.Select(l => l.PlayerId).ToList();
        var names = (await Repo<Player, PlayerId>(s).ListAsync(p => playerIds.Contains(p.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id, p => p.FullName);

        IReadOnlyList<BoxScoreLine> Side(ClubId club) =>
        [
            .. lines.Where(l => l.ClubId == club)
                .Select(l => new BoxScoreLine(l.PlayerId, names.GetValueOrDefault(l.PlayerId, "?"), l.BoxScore))
                .OrderByDescending(l => l.BoxScore.Minutes).ThenBy(l => l.Player),
        ];

        return new MatchReport(match, fixture, competition, home, away, Side(home.Id), Side(away.Id));
    }

    private async Task<TeamSheet> SheetAsync(ClubId clubId, CancellationToken cancellationToken)
    {
        var roster = await clubs.GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("A club of this fixture no longer exists in the database.");
        var teamTactics = await tactics.GetMatchTacticsAsync(clubId, cancellationToken).ConfigureAwait(false);
        return TeamSheet.Pick(
            clubId,
            roster.Club.Name,
            roster.Players.Select(p => (p.Player, p.Role, p.LineupPosition, p.RotationOrder)),
            teamTactics,
            calculator);
    }

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();
}
