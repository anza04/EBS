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
            db.Services.GetRequiredService<Ebm.Services.Contracts.IContractService>());
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

        var sizes = RunOnSta(() =>
        {
            // Like App.xaml: the themes are application resources, so each dictionary can use the previous ones.
            var resources = (System.Windows.Application.Current ?? new System.Windows.Application()).Resources;
            foreach (var name in new[] { "Shell", "Editor", "Players", "Clubs", "Contracts" })
            {
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Ebm.Desktop;component/Themes/{name}.xaml") });
            }

            return new[] { ("players", (object)editor.Players), ("profile", editor.Profile), ("compare", editor.Comparison), ("club-profile", editor.ClubProfile), ("squad", editor.Squad), ("club-contracts", editor.ClubContracts), ("contracts-editor", editor.Contracts) }
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
