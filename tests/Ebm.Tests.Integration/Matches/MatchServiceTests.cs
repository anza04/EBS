using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Tactics;
using Ebm.Services.Chemistry;
using Ebm.Services.Competitions;
using Ebm.Services.Matches;
using Ebm.Services.Tactics;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Matches;

public sealed class MatchServiceTests : IAsyncLifetime
{
    private static readonly Position[] Positions = Enum.GetValues<Position>();

    private TestDatabase _db = null!;
    private SampleWorld _world = null!;
    private Fixture _fixture = null!;

    private IMatchService Matches => _db.Services.GetRequiredService<IMatchService>();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(new FixedTimeProvider(new DateOnly(2026, 11, 9)));
        _world = await SampleWorld.SeedAsync(_db);
        _fixture = new Fixture(FixtureId.New(), _world.Championship.Id, _world.Season.Id, 2, new DateOnly(2026, 11, 15), _world.AwayClub.Id, _world.HomeClub.Id);
        await _db.WriteAsync(s =>
        {
            foreach (var club in new[] { _world.HomeClub.Id, _world.AwayClub.Id })
            {
                for (var i = 0; i < 10; i++)
                {
                    var player = new Player(PlayerId.New(), "Squad", $"P{i}{Guid.NewGuid():N}"[..12], new DateOnly(1998, 1, 1), _world.Italy.Id, 190 + (i % 5 * 5), 90, Positions[i % 5], PlayerAttributes.Uniform(55 + i));
                    s.Repo<Player, PlayerId>().Add(player);
                    s.Repo<Contract, ContractId>().Add(Contract.ForPlayer(ContractId.New(), club, player.Id, SampleWorld.SeasonStart, SampleWorld.SeasonStart.AddYears(2), 100_000));
                }
            }

            s.Repo<Fixture, FixtureId>().Add(_fixture);
        });
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task An_instant_match_is_played_stored_and_its_box_score_reopened()
    {
        var report = await Matches.PlayAsync(_fixture.Id, seed: 11);

        Assert.NotEqual(report.Match.HomeScore, report.Match.AwayScore);
        Assert.Equal(report.Match.HomeScore, report.HomeLines.Sum(l => l.BoxScore.Points));
        Assert.Equal(report.Match.AwayScore, report.AwayLines.Sum(l => l.BoxScore.Points));
        Assert.Equal(_world.AwayClub.Id, report.Home.Id); // the fixture's home club
        Assert.InRange(report.HomeLines.Count, 5, 12);
        Assert.Equal(report.HomeStatistics.Points, report.Match.HomeScore);

        var reopened = (await Matches.GetReportAsync(report.Match.Id))!;
        Assert.Equal(report.HomeLines.Select(l => (l.PlayerId, l.BoxScore.Points, l.BoxScore.Minutes)), reopened.HomeLines.Select(l => (l.PlayerId, l.BoxScore.Points, l.BoxScore.Minutes)));

        // Tactical decisions are stored with the result (Step 27): at least each side's game plan.
        Assert.Contains(reopened.TacticalNotes, n => n.Home && n.Period == 1 && n.Text.StartsWith("Game plan", StringComparison.Ordinal));
        Assert.Contains(reopened.TacticalNotes, n => !n.Home && n.Text.StartsWith("Game plan", StringComparison.Ordinal));

        var line = Assert.Single(await Matches.GetFixturesAsync(_world.Championship.Id, _world.Season.Id), f => f.Fixture.Id == _fixture.Id);
        Assert.Equal(report.Match.Id, line.Match!.Id);
        var standings = await _db.Services.GetRequiredService<ICompetitionService>().GetStandingsAsync(_world.Championship.Id, _world.Season.Id);
        Assert.All(standings, s => Assert.Equal(2, s.Played));

        await Assert.ThrowsAsync<DomainValidationException>(() => Matches.PlayAsync(_fixture.Id));
        var seasons = await Matches.GetCompetitionSeasonsAsync();
        Assert.Contains(seasons, c => c.Competition.Id == _world.Championship.Id && c.Season.Id == _world.Season.Id);
    }

    [Fact]
    public async Task A_clubs_season_statistics_hold_its_players_box_scores()
    {
        var club = _world.HomeClub.Id;
        var before = (await Matches.GetClubSeasonStatisticsAsync(club))!;
        Assert.Equal(_world.Season.Id, before.Season!.Id);

        var report = await Matches.PlayAsync(_fixture.Id, seed: 5);

        var stats = (await Matches.GetClubSeasonStatisticsAsync(club))!;
        var side = report.Home.Id == club ? report.HomeLines : report.AwayLines;
        Assert.Equal(before.Lines.Count + side.Count, stats.Lines.Count);
        Assert.Equal(before.Lines.Sum(l => l.BoxScore.Points) + side.Sum(l => l.BoxScore.Points), stats.Lines.Sum(l => l.BoxScore.Points));
        Assert.All(side, s => Assert.Contains(stats.Lines, l => l.PlayerId == s.PlayerId && l.CompetitionId == _world.Championship.Id));
        Assert.Contains(stats.Competitions, c => c.Id == _world.Championship.Id);
        Assert.Null(await Matches.GetClubSeasonStatisticsAsync(ClubId.New()));
    }

    [Fact]
    public async Task A_live_match_uses_the_selected_tactics_accepts_interventions_and_is_saved_at_the_end()
    {
        await _db.Services.GetRequiredService<ITacticsService>().SaveAsync(_world.HomeClub.Id, new TacticDraft(
            null, "Zone", OffensiveSystem.FiveOut, OffensiveSettings.Default, DefensiveSystem.Zone23, DefensiveSettings.Default, []));

        var preview = (await Matches.GetPreviewAsync(_fixture.Id))!;
        Assert.Equal(5, preview.Away.Players.Count(p => p.StartingPosition is not null)); // starters picked although no squad roles are stored
        Assert.Equal((await _db.Services.GetRequiredService<IChemistryService>().RateAsync(preview.Away)).Score, preview.Away.Chemistry); // Step 26
        Assert.Equal(DefensiveSystem.Zone23, preview.Away.Tactics.DefensiveSystem);

        var session = await Matches.StartAsync(_fixture.Id, SimulationMode.Live, seed: 3);
        var sim = session.Simulation;
        Assert.Equal(OffensiveSystem.FiveOut, sim.Tactics(TeamSide.Away).OffensiveSystem);
        sim.SetAutomatic(TeamSide.Away, false);
        sim.Step();
        sim.Substitute(TeamSide.Away, sim.OnCourt(TeamSide.Away)[0].Player.Id, sim.Lines(TeamSide.Away).First(l => !l.IsOnCourt).Player.Id);
        sim.CallTimeout(TeamSide.Away);
        sim.ChangeTactics(TeamSide.Away, TeamTactics.Default with { DefensiveSystem = DefensiveSystem.FullCourtPress });
        await Assert.ThrowsAsync<DomainException>(() => Matches.SaveAsync(session)); // not over yet

        sim.RunToEnd();
        var id = await Matches.SaveAsync(session);
        var report = (await Matches.GetReportAsync(id))!;
        Assert.Equal((sim.HomeScore, sim.AwayScore, sim.Overtimes), (report.Match.HomeScore, report.Match.AwayScore, report.Match.Overtimes));
        await Assert.ThrowsAsync<DomainValidationException>(() => Matches.SaveAsync(session));
    }
}
