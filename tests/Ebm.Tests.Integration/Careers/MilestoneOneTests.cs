using System.Reflection;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Rosters;
using Ebm.Domain.Tactics;
using Ebm.Services;
using Ebm.Services.Careers;
using Ebm.Services.Clubs;
using Ebm.Services.Contracts;
using Ebm.Services.Import;
using Ebm.Services.Matches;
using Ebm.Services.Saves;
using Ebm.Services.Tactics;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Careers;

/// <summary>
/// Milestone 1 acceptance (Step 14): create a career, manage the club (squad, tactics, a signing), play a live match,
/// save and load, finish the season and play in the second one.
/// </summary>
public sealed class MilestoneOneTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    private T Get<T>()
        where T : notnull => _db.Services.GetRequiredService<T>();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
        var data = typeof(MilestoneOneTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!;
        var report = await Get<IDataImportService>().ImportAsync(
            [Path.Combine(data, "sample", "italy-sample.json"), Path.Combine(data, "sample", "italy-squads.json")], new ImportOptions());
        Assert.True(report.Committed);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task A_career_is_created_managed_saved_loaded_and_played_into_a_second_season()
    {
        var careers = Get<ICareerService>();
        var clubs = Get<IClubService>();
        var saves = Get<ISaveGameService>();
        var option = (await careers.GetClubOptionsAsync()).Single(o => o.Club.Name == "Aurora Milano");
        var state = await careers.StartAsync(option.Club.Id, "Milestone");
        var club = state.Club.Id;

        // Manage the club: a free-agent signing on the career date, a new starting five, a tactic.
        var contracts = Get<IContractService>();
        var agent = (await contracts.GetFreeAgentsAsync())[0];
        var end = state.Today.AddYears(1).AddDays(-1);
        var asking = (await contracts.NegotiateAsync(agent.Player.Id, club, new ContractTerms(state.Today, end, 1))).AskingSalary;
        await contracts.SignAsync(agent.Player.Id, club, new ContractTerms(state.Today, end, asking));
        var roster = (await clubs.GetRosterAsync(club))!;
        Assert.Equal(13, roster.FirstTeam.Count());
        var best = roster.Players.OrderByDescending(p => p.Evaluation.Overall).ToList();
        var positions = Enum.GetValues<Ebm.Domain.Players.Position>();
        await clubs.SaveRosterAsync(club,
        [
            .. best.Take(5).Select((p, i) => new RosterAssignment(p.Player.Id, RosterRole.Starter, positions[i])),
            .. best.Skip(5).Select(p => new RosterAssignment(p.Player.Id, RosterRole.Bench)),
        ]);
        var tactics = Get<ITacticsService>();
        await tactics.SaveAsync(club, new TacticDraft(null, "Milestone", OffensiveSystem.PickAndRoll, OffensiveSettings.Default, DefensiveSystem.ManToMan, DefensiveSettings.Default, []));

        // The first match, live: possession by possession with a timeout, then saved like the match centre does.
        var report = await careers.ContinueAsync();
        Assert.Equal(CalendarStop.UserMatch, report.StoppedBecause);
        var matches = Get<IMatchService>();
        var session = await matches.StartAsync(report.State.NextFixture!.Fixture.Id, SimulationMode.Live);
        var side = session.Fixture.HomeClubId == club ? TeamSide.Home : TeamSide.Away;
        for (var i = 0; i < 10; i++)
        {
            session.Simulation.Step();
        }

        session.Simulation.CallTimeout(side);
        session.Simulation.RunToEnd();
        await matches.SaveAsync(session);

        // Save, play on, load: the game returns to the saved day.
        var saved = await careers.LoadAsync();
        var info = await saves.SaveAsync("Milestone save");
        Assert.Contains("Milestone · Aurora Milano", info.Career, StringComparison.Ordinal);
        await careers.ContinueAsync(maxDays: 20);
        var later = (await careers.LoadAsync())!;
        Assert.True(later.Today > saved!.Today);
        var playedLater = (await _db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync())).Count;

        await saves.LoadAsync("Milestone save");
        var loaded = (await careers.LoadAsync())!;
        Assert.Equal(saved.Today, loaded.Today);
        Assert.Equal(saved.Today, Get<GameClock>().Today);
        Assert.True((await _db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync())).Count < playedLater);
        Assert.Single(await saves.ListAsync());

        // The rest of the season, then the second season's first match.
        while (true)
        {
            var step = await careers.ContinueAsync();
            if (step.StoppedBecause == CalendarStop.SeasonComplete)
            {
                break;
            }

            await careers.PlayUserMatchAsync();
            if ((await careers.LoadAsync())!.SeasonComplete)
            {
                break;
            }
        }

        var second = await careers.StartNextSeasonAsync();
        var first = await careers.ContinueAsync();
        Assert.Equal(CalendarStop.UserMatch, first.StoppedBecause);
        Assert.Equal(second.Season.Id, first.State.NextFixture!.Fixture.SeasonId);
        var match = await careers.PlayUserMatchAsync();
        Assert.False(match.IsForfeit);
        Assert.Equal(2, (await careers.GetClubHistoryAsync(club)).Count);
    }

    [Fact]
    public async Task Save_names_and_foreign_files_are_refused_and_a_new_game_starts_empty()
    {
        var saves = Get<ISaveGameService>();
        await Assert.ThrowsAsync<DomainValidationException>(() => saves.SaveAsync("bad/name"));
        await Assert.ThrowsAsync<DomainValidationException>(() => saves.SaveAsync(" "));
        await saves.SaveAsync("Before");
        await Assert.ThrowsAsync<DomainValidationException>(() => saves.SaveAsync("Before"));
        await saves.SaveAsync("Before", overwrite: true);
        await Assert.ThrowsAsync<DomainValidationException>(() => saves.LoadAsync("Missing"));

        await File.WriteAllTextAsync(Path.Combine(saves.SavesDirectory, "Garbage.ebmsave"), "not a database");
        await Assert.ThrowsAsync<DomainValidationException>(() => saves.LoadAsync("Garbage"));

        await saves.NewGameAsync();
        Assert.Empty(await _db.ReadAsync(s => s.Repo<Ebm.Domain.Clubs.Club, Ebm.Domain.Clubs.ClubId>().ListAsync()));
        await saves.LoadAsync("Before");
        Assert.Equal(14, (await _db.ReadAsync(s => s.Repo<Ebm.Domain.Clubs.Club, Ebm.Domain.Clubs.ClubId>().ListAsync())).Count);
        await saves.DeleteAsync("Garbage");
        Assert.Single(await saves.ListAsync());
    }
}
