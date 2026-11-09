using Ebm.Desktop.Startup;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Players;
using Ebm.Services.Data;
using Ebm.Services.Editing;
using Ebm.Services.Import;
using Ebm.Services.Players;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Editing;

public sealed class DatabaseEditorTests : IAsyncLifetime
{
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private IDatabaseEditor Editor => _db.Services.GetRequiredService<IDatabaseEditor>();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
        _world = await SampleWorld.SeedAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Players_can_be_added_and_edited_including_attributes()
    {
        var id = await Editor.SavePlayerAsync(Draft(null, "Luca", "Nuovo"));
        await Editor.SavePlayerAsync(Draft(id, "Luca", "Nuovo", height: 210, rating: 90));

        var snapshot = await Editor.LoadAsync();
        var player = snapshot.Players.Single(p => p.Id == id);
        Assert.Equal(210, player.HeightCm);
        Assert.Equal(90, player.Attributes.Passing);
        Assert.Equal(2, snapshot.Players.Count);
        Assert.Equal((215, 40, 30, 75), (player.WingspanCm, player.Experience, player.Reputation, player.Potential));
        Assert.Equal([Position.PowerForward], player.SecondaryPositions.Positions);
        Assert.Equal((90, 55, InjuryStatus.Healthy), (player.Fitness, player.Morale, player.InjuryStatus));
        Assert.Equal(90, player.Tendencies.PostUp);
    }

    [Fact]
    public async Task Player_page_saves_profile_condition_and_tendencies()
    {
        var page = new PlayersPageViewModel(Editor, new EditorLookups(), () => Task.CompletedTask);
        page.StartNewCommand.Execute(null);
        (page.FirstName, page.LastName, page.DateOfBirth, page.NationalityId) = ("Page", "Player", new DateTime(2002, 2, 2), _world.Italy.Id);
        (page.HeightCm, page.WeightKg, page.Position) = ("198", "92", Position.SmallForward);
        page.SecondaryPositions[(int)Position.ShootingGuard].IsChecked = true;
        page.SecondaryPositions[(int)Position.SmallForward].IsChecked = true; // the primary position is ignored
        (page.InjuryStatus, page.InjuryReturnDate) = (InjuryStatus.Injured, new DateTime(2026, 12, 24));
        page.Ratings[(int)PlayerAttribute.ThreePoint].Value = "88";
        page.SuggestTendenciesCommand.Execute(null);

        await page.SaveCommand.ExecuteAsync(null);

        Assert.False(page.IsError, page.Message);
        var player = (await Editor.LoadAsync()).Players.Single(p => p.LastName == "Player");
        Assert.Equal(198, player.WingspanCm); // blank wingspan defaults to the height
        Assert.Equal([Position.ShootingGuard], player.SecondaryPositions.Positions);
        Assert.Equal(new DateOnly(2026, 12, 24), player.InjuryReturnDate);
        Assert.Equal(PlayerTendencies.Suggest(player.Attributes).ToDictionary(), player.Tendencies.ToDictionary());
        Assert.Equal(79, player.Tendencies.ThreePointShot); // (3·88 + 50) / 4 = 78.5
    }

    [Fact]
    public async Task Duplicate_or_invalid_players_are_refused_and_nothing_is_saved()
    {
        var p = _world.Player;

        var duplicate = await Assert.ThrowsAsync<DuplicateRecordException>(() =>
            Editor.SavePlayerAsync(Draft(null, p.FirstName.ToUpperInvariant(), p.LastName, p.DateOfBirth)));
        await Assert.ThrowsAsync<DomainValidationException>(() => Editor.SavePlayerAsync(Draft(p.Id, p.FirstName, p.LastName, height: 400)));
        await Assert.ThrowsAsync<DomainException>(() => Editor.SavePlayerAsync(Draft(PlayerId.New(), "Ghost", "Player")));

        Assert.Contains("already exists", duplicate.Message, StringComparison.Ordinal);
        var stored = (await Editor.LoadAsync()).Players.Single();
        Assert.Equal(p.HeightCm, stored.HeightCm);
    }

    [Fact]
    public async Task Clubs_contracts_leagues_and_competitions_can_be_edited()
    {
        var club = _world.AwayClub;
        await Editor.SaveClubAsync(new ClubDraft(club.Id, "Renamed Bologna", "RBO", club.CountryId, _world.Arena.Id, 1950));
        var contractId = await Editor.SaveContractAsync(new ContractDraft(null, club.Id, _world.Player.Id, new DateOnly(2028, 7, 1), new DateOnly(2030, 6, 30), 500_000));
        await Editor.SaveContractAsync(new ContractDraft(contractId, club.Id, _world.Player.Id, new DateOnly(2028, 7, 1), new DateOnly(2031, 6, 30), 600_000));
        await Editor.SaveLeagueAsync(new LeagueDraft(_world.League.Id, "Renamed League", "RL", null));
        await Editor.SaveCompetitionAsync(new CompetitionDraft(_world.Cup.Id, "Cup", "CUP", CompetitionType.Cup, _world.Italy.Id, null, """{"regularSeason":{"legs":4}}"""));

        var snapshot = await Editor.LoadAsync();
        Assert.Equal("Renamed Bologna", snapshot.Clubs.Single(c => c.Id == club.Id).Name);
        Assert.Equal(600_000, snapshot.PlayerContracts.Single(c => c.Id == contractId).AnnualSalary);
        Assert.Null(snapshot.Leagues.Single().CountryId);
        Assert.Equal("""{"regularSeason":{"legs":4}}""", snapshot.Competitions.Single(c => c.Id == _world.Cup.Id).RulesJson);
    }

    [Fact]
    public async Task Contract_club_cannot_change_and_bad_rules_are_refused()
    {
        var contract = (await Editor.LoadAsync()).PlayerContracts.Single();

        await Assert.ThrowsAsync<DomainValidationException>(() => Editor.SaveContractAsync(
            new ContractDraft(contract.Id, _world.AwayClub.Id, _world.Player.Id, contract.StartDate, contract.EndDate, 1)));
        await Assert.ThrowsAsync<DuplicateRecordException>(() => Editor.SaveContractAsync(
            new ContractDraft(null, contract.ClubId, _world.Player.Id, contract.StartDate, contract.EndDate.AddYears(1), 1)));
        await Assert.ThrowsAsync<DomainValidationException>(() => Editor.SaveCompetitionAsync(
            new CompetitionDraft(_world.Cup.Id, "Cup", "CUP", CompetitionType.Cup, null, null, "[1,2]")));
    }

    [Fact]
    public async Task Editor_view_model_shows_imported_data_and_reports_errors_on_the_page()
    {
        var picker = new FixedFilePicker([Path.Combine(_db.TempFolder, "missing.json")]);
        var vm = new DatabaseEditorViewModel(Editor, _db.Services.GetRequiredService<IDataImportService>(), picker, _db.Services.GetRequiredService<IPlayerProfileService>(), _db.Services.GetRequiredService<Ebm.Services.Clubs.IClubService>());
        await vm.ReloadAsync();

        Assert.Single(vm.Players.Items);
        Assert.Equal(2, vm.Clubs.Items.Count);
        Assert.Equal($"{_world.Player.LastName}, {_world.Player.FirstName}", vm.Contracts.Items.Single().Player);

        vm.Players.SelectedItem = vm.Players.Items[0];
        vm.Players.HeightCm = "tall";
        await vm.Players.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.Players.IsError);
        Assert.Equal("Height (cm) must be a whole number.", vm.Players.Message);

        vm.Players.HeightCm = "205";
        await vm.Players.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.Players.IsError);
        Assert.Equal(205, vm.Players.SelectedItem!.HeightCm);

        vm.Import.ChooseFilesCommand.Execute(null);
        await vm.Import.ImportCommand.ExecuteAsync(null);
        Assert.Contains(vm.Import.Problems, p => p.StartsWith("missing.json", StringComparison.Ordinal));
    }

    private PlayerDraft Draft(PlayerId? id, string first, string last, DateOnly? dob = null, int height = 200, int rating = 60) =>
        new(id, first, last, dob ?? new DateOnly(2001, 1, 1), _world.Italy.Id, height, 95, height + 5, Position.Center, PositionSet.Of(Position.PowerForward),
            40, 30, 75, 90, 55, InjuryStatus.Healthy, null, PlayerAttributes.Uniform(rating), PlayerTendencies.Suggest(PlayerAttributes.Uniform(rating)));

    private sealed class FixedFilePicker(IReadOnlyList<string> files) : IFilePicker
    {
        public IReadOnlyList<string>? PickImportFiles() => files;
    }
}
