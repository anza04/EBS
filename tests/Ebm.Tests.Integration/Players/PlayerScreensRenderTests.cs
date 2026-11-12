using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ebm.Desktop.Startup;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Rosters;
using Ebm.Services.Editing;
using Ebm.Services.Import;
using Ebm.Services.Players;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Players;

/// <summary>
/// Renders the player and club screens off-screen with the real themes, so missing resources or broken templates fail here.
/// Set EBM_SCREENSHOT_DIR to also save the renders as PNG files for visual review.
/// </summary>
public sealed class PlayerScreensRenderTests
{
    [Fact]
    public async Task Player_and_club_screens_render_with_the_application_themes()
    {
        await using var db = await TestDatabase.CreateAsync(new FixedTimeProvider(new DateOnly(2026, 11, 9)));
        await db.Services.GetRequiredService<IDataImportService>()
            .ImportAsync([Path.Combine(DataFolder(), "sample", "italy-sample.json")], new ImportOptions());
        var editor = new DatabaseEditorViewModel(
            db.Services.GetRequiredService<IDatabaseEditor>(),
            db.Services.GetRequiredService<IDataImportService>(),
            new NoFiles(),
            db.Services.GetRequiredService<IPlayerProfileService>(),
            db.Services.GetRequiredService<Ebm.Services.Clubs.IClubService>(),
            db.Services.GetRequiredService<Ebm.Services.Contracts.IContractService>(),
            db.Services.GetRequiredService<Ebm.Services.Transfers.ITransferService>(),
            db.Services.GetRequiredService<Ebm.Services.Tactics.ITacticsService>(),
            db.Services.GetRequiredService<Ebm.Services.Matches.IMatchService>(),
            db.Services.GetRequiredService<Ebm.Services.Competitions.ICompetitionService>());
        await editor.ReloadAsync();
        var players = editor.Lookups.Players;
        editor.Players.SelectedItem = editor.Players.Items[0];
        editor.Profile.PlayerId = players[0].Id;
        await editor.Profile.RefreshAsync();
        (editor.Comparison.PlayerA, editor.Comparison.PlayerB) = (players[0].Id, players[1].Id);
        await editor.Comparison.RefreshAsync();
        var club = editor.Lookups.Clubs.Single(c => c.Name == "Aurora Milano").Id; // the sample club with a full squad
        (editor.ClubProfile.ClubId, editor.Squad.ClubId) = (club, club);
        await editor.ClubProfile.RefreshAsync();
        await editor.Squad.RefreshAsync();

        // Build a lineup through the squad screen: a starter per position, five in the rotation, the rest on the bench.
        var rows = editor.Squad.Rows.ToList();
        foreach (var position in Enum.GetValues<Ebm.Domain.Players.Position>())
        {
            rows.First(r => r.Role != RosterRole.Starter && r.Player.Player.Position == position).Role = RosterRole.Starter;
        }

        foreach (var (row, i) in rows.Where(r => r.Role != RosterRole.Starter).Select((r, i) => (r, i)))
        {
            row.Role = i < 5 ? RosterRole.Rotation : RosterRole.Bench;
            row.RotationOrder = i < 5 ? $"{5 - i}" : string.Empty;
        }

        Assert.True(editor.Squad.IsValid, string.Join(" | ", editor.Squad.Issues.Select(i => i.Message)));
        await editor.Squad.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Saved. The roster is valid.", editor.Squad.Message);
        var rotation = rows.Where(r => r.Role == RosterRole.Rotation).OrderBy(r => int.Parse(r.RotationOrder, System.Globalization.CultureInfo.InvariantCulture)).Select(r => r.Name);
        Assert.Equal(rotation, editor.Squad.Rows.Where(r => r.Role == RosterRole.Rotation).Select(r => r.Name)); // reloaded in rotation order
        await editor.ClubProfile.RefreshAsync();

        // Sign a free agent through the club contracts screen: ask, take his counter-offer, submit.
        var contracts = editor.ClubContracts;
        contracts.ClubId = club;
        await contracts.RefreshAsync();
        Assert.Equal(12, contracts.Current.Count);
        Assert.Equal(4, contracts.FreeAgents.Count);
        Assert.Contains(contracts.Current, r => r.Options != "—");
        contracts.StartSigningCommand.Execute(null);
        contracts.OfferPlayerId = contracts.FreeAgents[0].Id;
        contracts.OfferSalary = "50000";
        await contracts.AskPlayerCommand.ExecuteAsync(null);
        Assert.NotNull(contracts.Negotiation);
        if (contracts.AcceptCounterCommand.CanExecute(null))
        {
            contracts.AcceptCounterCommand.Execute(null);
        }
        else
        {
            contracts.OfferSalary = "5,000,000".Replace(",", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator, StringComparison.Ordinal);
        }

        await contracts.SubmitOfferCommand.ExecuteAsync(null);
        Assert.False(contracts.IsError, contracts.Message);
        Assert.Equal(13, contracts.Current.Count);
        Assert.Equal(3, contracts.FreeAgents.Count);
        contracts.SelectedContract = contracts.Current.First(r => r.Contract.ClubOptionUntil is not null);
        Assert.True(contracts.SelectedHasClubOption);
        editor.Profile.PlayerId = contracts.SelectedContract.Contract.PlayerId;
        await editor.Profile.RefreshAsync();
        Assert.Single(editor.Profile.ContractHistory);

        // Transfer market: search, pick a player of another club, ask his club, pay the asking fee and buy him.
        var market = editor.TransferMarket;
        market.ClubId = club;
        await market.RefreshAsync();
        Assert.Single(market.History); // the free agent signed above
        Assert.Equal(3, market.Results.Count(r => r.Market.Status == Ebm.Services.Transfers.MarketStatus.FreeAgent));
        market.Filter = Ebm.Services.Transfers.MarketFilter.UnderContract;
        market.MinOverall = "60";
        await market.SearchCommand.ExecuteAsync(null);
        Assert.NotEmpty(market.Results);
        Assert.All(market.Results, r => Assert.NotEqual("Aurora Milano", r.Club));
        market.SelectedPlayer = market.Results.First(r => r.Market.ReleaseClause is null);
        Assert.NotEmpty(market.PlayerDetails);
        await market.AskClubCommand.ExecuteAsync(null);
        Assert.Contains("accepts", market.Negotiation, StringComparison.Ordinal);
        await market.AskPlayerCommand.ExecuteAsync(null);
        Assert.NotNull(market.Negotiation);
        market.OfferEnd = new DateTime(2028, 6, 30);
        await market.BuyCommand.ExecuteAsync(null);
        Assert.False(market.IsError, market.Message);
        Assert.Equal(2, market.History.Count);
        Assert.Equal(14, editor.ClubContracts.Current.Count);
        await market.RunAiWindowCommand.ExecuteAsync(null);
        Assert.False(market.IsError, market.Message);

        // Tactics screen: create a pick-and-roll tactic with a role change, save it and use it for the next match.
        var tactics = editor.Tactics;
        tactics.ClubId = club;
        await tactics.RefreshAsync();
        Assert.Empty(tactics.Saved);
        Assert.NotEmpty(tactics.Assignments);
        tactics.Name = "Pick and roll";
        tactics.OffensiveSystem = Ebm.Domain.Tactics.OffensiveSystem.PickAndRoll;
        tactics.Tempo = 5;
        tactics.DefensiveSystem = Ebm.Domain.Tactics.DefensiveSystem.Zone23;
        tactics.Assignments[0].Role = Ebm.Domain.Tactics.TacticalRole.ShotCreator;
        tactics.Assignments[0].MinutesTarget = "32";
        await tactics.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Tactic 'Pick and roll' saved.", tactics.Message);
        Assert.Equal("Next match: Pick and roll", tactics.SelectedText);
        Assert.Equal(Ebm.Domain.Tactics.TacticalRole.ShotCreator, tactics.Assignments[0].Role);

        // Match centre: give an opponent a playable squad, create the schedule and play Aurora's game live.
        var opponent = editor.Lookups.Clubs.Single(c => c.Name == "Fortezza Bologna").Id!.Value;
        var italy = editor.Lookups.Countries[0].Id!.Value;
        await db.WriteAsync(s =>
        {
            foreach (var position in Enum.GetValues<Ebm.Domain.Players.Position>())
            {
                var player = new Ebm.Domain.Players.Player(Ebm.Domain.Players.PlayerId.New(), "Extra", $"{position}", new DateOnly(1999, 1, 1), italy, 200, 95, position, Ebm.Domain.Players.PlayerAttributes.Uniform(58));
                s.Repo<Ebm.Domain.Players.Player, Ebm.Domain.Players.PlayerId>().Add(player);
                s.Repo<Ebm.Domain.Contracts.Contract, Ebm.Domain.Contracts.ContractId>().Add(Ebm.Domain.Contracts.Contract.ForPlayer(Ebm.Domain.Contracts.ContractId.New(), opponent, player.Id, new DateOnly(2026, 7, 1), new DateOnly(2028, 6, 30), 80_000));
            }
        });
        var center = editor.Matches;
        await center.RefreshAsync();
        center.SelectedCompetition = center.CompetitionSeasons.Single(c => c.Competition.Name == "Serie A");
        await center.CreateScheduleCommand.ExecuteAsync(null);
        Assert.False(center.IsError, center.Message);
        var game = center.Fixtures.First(f => (f.Home, f.Away) is ("Aurora Milano", "Fortezza Bologna") or ("Fortezza Bologna", "Aurora Milano"));
        center.SelectedFixture = game;
        await center.ShowSelectedAsync();
        Assert.Equal(12, (game.Home == "Aurora Milano" ? center.HomeSheet : center.AwaySheet).Count);
        center.Managed = game.Home == "Aurora Milano" ? Ebm.Desktop.ViewModels.Matches.ManagedSide.Home : Ebm.Desktop.ViewModels.Matches.ManagedSide.Away;
        await center.StartMatchCommand.ExecuteAsync(null);
        Assert.False(center.IsError, center.Message);
        Assert.True(center.CanIntervene);
        for (var i = 0; i < 10; i++)
        {
            await center.NextPossessionCommand.ExecuteAsync(null);
        }

        center.SubOut = center.OnCourt[0].Id;
        center.SubIn = center.Bench[0].Id;
        await center.SubstituteCommand.ExecuteAsync(null);
        Assert.Equal("Substitution made.", center.Message);
        await center.TimeoutCommand.ExecuteAsync(null);
        center.LiveDefense = Ebm.Domain.Tactics.DefensiveSystem.Zone23;
        await center.ApplyTacticsCommand.ExecuteAsync(null);
        Assert.Contains(center.PlayByPlay, e => e.Text.Contains("2-3", StringComparison.Ordinal) || e.Text.Contains("Zone23", StringComparison.Ordinal) || e.Text.Contains("zone23", StringComparison.OrdinalIgnoreCase));
        await center.ToEndOfQuarterCommand.ExecuteAsync(null);
        Assert.StartsWith("Q2", center.ClockText, StringComparison.Ordinal);
        Assert.NotEmpty(center.HomeBox);
        Assert.NotEmpty(center.TeamStats);
        await center.ToEndOfMatchCommand.ExecuteAsync(null);
        Assert.False(center.IsError, center.Message);
        Assert.StartsWith("Final", center.Message, StringComparison.Ordinal);
        Assert.False(center.InProgress);
        Assert.Contains(center.Report, l => l.Label == "Q4");
        Assert.NotEqual("—", center.Fixtures.Single(f => f.Line.Fixture.Id == game.Line.Fixture.Id).Result); // saved

        // Historical box score: open another fixture, then the played one again.
        center.SelectedFixture = center.Fixtures.First(f => f.Line.Match is null && f.Home != "Aurora Milano" && f.Away != "Aurora Milano" && f.Home != "Fortezza Bologna" && f.Away != "Fortezza Bologna");
        await center.ShowSelectedAsync();
        Assert.StartsWith("Preview", center.Title, StringComparison.Ordinal);
        Assert.True(center.IsError); // three-player squads cannot play
        Assert.Contains("at least 5", center.Message, StringComparison.Ordinal);
        center.SelectedFixture = center.Fixtures.Single(f => f.Line.Fixture.Id == game.Line.Fixture.Id);
        await center.ShowSelectedAsync();
        Assert.StartsWith("Match report", center.Title, StringComparison.Ordinal);
        Assert.NotEmpty(center.HomeBox);
        Assert.Equal(center.ScoreText.Split(" – ").Select(int.Parse).Sum(), center.HomeBox.Sum(r => r.Points) + center.AwayBox.Sum(r => r.Points));
        Assert.NotEmpty(market.AiReport);

        var sizes = RunOnSta(() =>
        {
            // Like App.xaml: the themes are application resources, so each dictionary can use the previous ones.
            var resources = (System.Windows.Application.Current ?? new System.Windows.Application()).Resources;
            foreach (var name in new[] { "Shell", "Editor", "Players", "Clubs", "Contracts", "Transfers", "Tactics", "Matches" })
            {
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Ebm.Desktop;component/Themes/{name}.xaml") });
            }

            return new[] { ("players", (object)editor.Players), ("profile", editor.Profile), ("compare", editor.Comparison), ("club-profile", editor.ClubProfile), ("squad", editor.Squad), ("club-contracts", editor.ClubContracts), ("transfer-market", editor.TransferMarket), ("tactics", editor.Tactics), ("match-report", editor.Matches), ("contracts-editor", editor.Contracts) }
                .Select(screen => Render(screen.Item1, screen.Item2))
                .ToList();
        });

        Assert.All(sizes, s => Assert.True(s > 0, "A screen rendered no content."));
        Assert.Equal(27, editor.Players.Ratings.Count);
        Assert.NotEmpty(editor.Comparison.Rows);
        Assert.NotEmpty(editor.ClubProfile.Details);
        Assert.NotEmpty(editor.Squad.Rows);
        Assert.Equal(5, editor.Squad.Lineup.Count);
    }

    private static int Render(string name, object viewModel)
    {
        var host = new ContentControl { Content = viewModel, Width = 1280, Height = 900, Background = Brushes.White };
        host.Measure(new Size(1280, 900));
        host.Arrange(new Rect(0, 0, 1280, 900));
        host.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1280, 900, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        if (Environment.GetEnvironmentVariable("EBM_SCREENSHOT_DIR") is { Length: > 0 } folder)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, $"{name}.png"));
            encoder.Save(file);
        }

        return VisualTreeHelper.GetChildrenCount(host);
    }

    private static T RunOnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result;
    }

    private static string DataFolder() => typeof(PlayerScreensRenderTests).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false).Cast<System.Reflection.AssemblyMetadataAttribute>()
        .Single(a => a.Key == "RepositoryDataPath").Value!;

    private sealed class NoFiles : IFilePicker
    {
        public IReadOnlyList<string>? PickImportFiles() => null;
    }
}
