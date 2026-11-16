using System.Reflection;
using Ebm.Desktop.ViewModels.Careers;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Competitions;
using Ebm.Domain.Seasons;
using Ebm.Services.Careers;
using Ebm.Services.Competitions;
using Ebm.Services.Editing;
using Ebm.Services.Import;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Careers;

/// <summary>The career screen drives the career service: start, continue to a match day, play, standings.</summary>
public sealed class CareerViewModelTests
{
    [Fact]
    public async Task Career_screen_starts_a_career_and_plays_through_the_calendar()
    {
        await using var db = await TestDatabase.CreateAsync();
        var data = typeof(CareerViewModelTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!;
        await db.Services.GetRequiredService<IDataImportService>().ImportAsync(
            [Path.Combine(data, "sample", "italy-sample.json"), Path.Combine(data, "sample", "italy-squads.json")], new ImportOptions());
        var lookups = new EditorLookups();
        lookups.Refresh(await db.Services.GetRequiredService<IDatabaseEditor>().LoadAsync());
        CareerViewModel vm = null!;
        vm = new CareerViewModel(db.Services.GetRequiredService<ICareerService>(), db.Services.GetRequiredService<ICompetitionService>(), lookups, () => vm.RefreshAsync());
        await vm.RefreshAsync();
        Assert.True(vm.NoCareer);
        Assert.Equal(14, vm.ClubOptions.Count);

        vm.SelectedClub = vm.ClubOptions.Single(o => o.Club.Name == "Vela Trieste");
        await vm.StartCareerCommand.ExecuteAsync(null);
        Assert.True(vm.HasCareer);
        Assert.Equal("Serie A2", vm.SelectedCompetition!.Competition.Name);
        Assert.Equal(6, vm.Standings.Count);

        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.False(vm.IsError, vm.Message);
        Assert.StartsWith("Today:", vm.NextMatch, StringComparison.Ordinal);
        await vm.PlayMatchCommand.ExecuteAsync(null);
        Assert.StartsWith("Final:", vm.Message, StringComparison.Ordinal);
        Assert.Contains(vm.Standings, s => s.IsUser && s.Played == 1);
        await vm.StartNextSeasonCommand.ExecuteAsync(null);
        Assert.True(vm.IsError);
        await vm.AdvanceDayCommand.ExecuteAsync(null);
        Assert.Equal(6, vm.News.Count); // the other two Serie A2 games and the four Serie A games
    }

    [Fact]
    public async Task Career_screen_shows_a_cup_draw_with_its_byes_and_ties()
    {
        await using var db = await TestDatabase.CreateAsync();
        var data = typeof(CareerViewModelTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!;
        await db.Services.GetRequiredService<IDataImportService>().ImportAsync(
            [Path.Combine(data, "sample", "italy-sample.json"), Path.Combine(data, "sample", "italy-squads.json")], new ImportOptions());
        var editor = db.Services.GetRequiredService<IDatabaseEditor>();
        var snapshot = await editor.LoadAsync();
        var cup = snapshot.Competitions.Single(c => c.Type == CompetitionType.Cup);
        await editor.SaveCompetitionAsync(new CompetitionDraft(cup.Id, cup.Name, cup.ShortName, cup.Type, cup.CountryId, null,
            """{"cup":{"startOffsetDays":30,"rounds":[{"name":"First round"},{"name":"Quarter-finals"},{"name":"Semi-finals"},{"name":"Final"}]}}"""));
        var season = (await db.ReadAsync(s => s.Repo<Season, SeasonId>().ListAsync())).Single();
        var entries = await db.ReadAsync(s => s.Repo<CompetitionParticipant, CompetitionParticipantId>().ListAsync());
        var competitions = db.Services.GetRequiredService<ICompetitionService>();
        await competitions.RegisterClubsAsync(cup.Id, season.Id, [.. entries.Select(p => p.ClubId).Distinct()]);
        var lookups = new EditorLookups();
        lookups.Refresh(await editor.LoadAsync());
        CareerViewModel vm = null!;
        vm = new CareerViewModel(db.Services.GetRequiredService<ICareerService>(), competitions, lookups, () => vm.RefreshAsync());
        await vm.RefreshAsync();
        vm.SelectedClub = vm.ClubOptions.Single(o => o.Club.Name == "Vela Trieste");
        await vm.StartCareerCommand.ExecuteAsync(null);
        Assert.False(vm.HasCup);

        await vm.AdvanceDayCommand.ExecuteAsync(null);
        vm.SelectedCompetition = vm.CompetitionsList.Single(c => c.Competition.Id == cup.Id);
        await vm.RefreshAsync();

        Assert.True(vm.HasCup);
        Assert.Equal("Byes", vm.CupLines[0].Date);
        Assert.Equal(6, vm.CupLines.Count(l => l.Date == "First round"));
        Assert.Equal(("Winner", "To be decided"), (vm.CupLines[^1].Date, vm.CupLines[^1].Text));
        Assert.Empty(vm.Standings);
    }
}
