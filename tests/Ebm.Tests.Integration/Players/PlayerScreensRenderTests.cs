using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ebm.Desktop.Startup;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Services.Editing;
using Ebm.Services.Import;
using Ebm.Services.Players;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Players;

/// <summary>
/// Renders the player screens off-screen with the real themes, so missing resources or broken templates fail here.
/// Set EBM_SCREENSHOT_DIR to also save the renders as PNG files for visual review.
/// </summary>
public sealed class PlayerScreensRenderTests
{
    [Fact]
    public async Task Player_editor_profile_and_comparison_render_with_the_application_themes()
    {
        await using var db = await TestDatabase.CreateAsync(new FixedTimeProvider(new DateOnly(2026, 11, 9)));
        await db.Services.GetRequiredService<IDataImportService>()
            .ImportAsync([Path.Combine(DataFolder(), "sample", "italy-sample.json")], new ImportOptions());
        var editor = new DatabaseEditorViewModel(
            db.Services.GetRequiredService<IDatabaseEditor>(),
            db.Services.GetRequiredService<IDataImportService>(),
            new NoFiles(),
            db.Services.GetRequiredService<IPlayerProfileService>());
        await editor.ReloadAsync();
        var players = editor.Lookups.Players;
        editor.Players.SelectedItem = editor.Players.Items[0];
        editor.Profile.PlayerId = players[0].Id;
        await editor.Profile.RefreshAsync();
        (editor.Comparison.PlayerA, editor.Comparison.PlayerB) = (players[0].Id, players[1].Id);
        await editor.Comparison.RefreshAsync();

        var sizes = RunOnSta(() =>
        {
            // Like App.xaml: the themes are application resources, so each dictionary can use the previous ones.
            var resources = (System.Windows.Application.Current ?? new System.Windows.Application()).Resources;
            foreach (var name in new[] { "Shell", "Editor", "Players" })
            {
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Ebm.Desktop;component/Themes/{name}.xaml") });
            }

            return new[] { ("players", (object)editor.Players), ("profile", editor.Profile), ("compare", editor.Comparison) }
                .Select(screen => Render(screen.Item1, screen.Item2))
                .ToList();
        });

        Assert.All(sizes, s => Assert.True(s > 0, "A screen rendered no content."));
        Assert.Equal(27, editor.Players.Ratings.Count);
        Assert.NotEmpty(editor.Comparison.Rows);
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
