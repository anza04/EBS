using System.Diagnostics;
using System.Reflection;
using Ebm.Desktop.Startup;
using Ebm.Tests.Integration.TestSupport;

namespace Ebm.Tests.Integration;

/// <summary>
/// Launches the real executable. Needs an interactive Windows desktop session; filter it out on
/// headless agents with <c>--filter "Category!=Launch"</c>.
/// </summary>
[Trait("Category", "Launch")]
public sealed class DesktopLaunchTests
{
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Application_launches_renders_main_window_and_exits_cleanly()
    {
        using var data = new TempDataRoot();
        var exe = DesktopExecutablePath();
        Assert.True(File.Exists(exe), $"Desktop executable not found at {exe}. Build the solution first.");

        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        startInfo.ArgumentList.Add(LaunchOptions.SmokeTestSwitch);
        startInfo.ArgumentList.Add(data.HostArgument);

        using var process = Process.Start(startInfo)!;
        using var timeout = new CancellationTokenSource(LaunchTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"Application did not exit within {LaunchTimeout}. Logs:{Environment.NewLine}{data.ReadAllLogs()}");
        }

        var logs = data.ReadAllLogs();
        Assert.True(process.ExitCode == ExitCodes.Success, $"Exit code {process.ExitCode}. Logs:{Environment.NewLine}{logs}");
        Assert.Contains("European Basketball Manager", logs, StringComparison.Ordinal);
        Assert.Contains("Smoke test passed: main window rendered", logs, StringComparison.Ordinal);
        Assert.Contains("Application exiting with code 0", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("[FTL]", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("[ERR]", logs, StringComparison.Ordinal);
    }

    private static string DesktopExecutablePath() =>
        typeof(DesktopLaunchTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "DesktopExecutablePath")
            .Value!;
}
