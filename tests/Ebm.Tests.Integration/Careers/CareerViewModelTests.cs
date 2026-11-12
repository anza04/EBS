using System.Reflection;
using Ebm.Desktop.ViewModels.Careers;
using Ebm.Desktop.ViewModels.Editor;
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
}
