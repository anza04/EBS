using System.Runtime.InteropServices;
using System.Windows;
using Ebm.Desktop.Startup;
using Ebm.Hosting;
using Ebm.Hosting.ErrorHandling;
using Ebm.Services.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ebm.Desktop;

public partial class App : Application
{
    private IHost? _host;
    private GlobalExceptionHandler? _exceptionHandler;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var launch = LaunchOptions.Parse(e.Args);

        try
        {
            var builder = EbmHost.CreateBuilder(launch.HostArgs);
            builder.Services.AddDesktopUi(launch);
            _host = builder.Build();
            _host.Start();
        }
        catch (Exception ex)
        {
            ReportStartupFailure(ex, launch);
            return;
        }

        var services = _host.Services;
        _logger = services.GetRequiredService<ILogger<App>>();
        _exceptionHandler = services.GetRequiredService<GlobalExceptionHandler>();
        _exceptionHandler.Attach(this);

        var info = services.GetRequiredService<ApplicationInfo>();
        var paths = services.GetRequiredService<AppPaths>();
        _logger.LogInformation(
            "{Product} {Version} starting (runtime {Runtime}, OS {OS}, data root {DataRoot}, smoke test {SmokeTest})",
            info.ProductName,
            info.InformationalVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            paths.DataRoot,
            launch.SmokeTest);

        // Contracts that ran out or started since the last session take effect before any screen loads (docs/CONTRACTS.md §5).
        var changes = services.GetRequiredService<IContractService>().ProcessContractDatesAsync().GetAwaiter().GetResult();
        _logger.LogInformation(
            "Contract dates applied: {Expired} expired, {Started} started, {FreeAgents} new free agents",
            changes.Expired,
            changes.Started,
            changes.NewFreeAgents.Count);

        var window = services.GetRequiredService<MainWindow>();
        if (launch.SmokeTest)
        {
            window.ContentRendered += (_, _) =>
            {
                _logger.LogInformation("Smoke test passed: main window rendered");
                Dispatcher.BeginInvoke(() => Shutdown(0));
            };
        }

        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Application exiting with code {ExitCode}", e.ApplicationExitCode);
        _exceptionHandler?.Detach();

        if (_host is not null)
        {
            try
            {
                _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            finally
            {
                // Disposing the host flushes and closes the log files.
                _host.Dispose();
            }
        }

        base.OnExit(e);
    }

    private void ReportStartupFailure(Exception exception, LaunchOptions launch)
    {
        var logPath = StartupFailureLog.TryWrite(exception);

        if (launch.Interactive)
        {
            var details = logPath is null ? string.Empty : $"{Environment.NewLine}{Environment.NewLine}Details: {logPath}";
            MessageBox.Show(
                $"The game could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}{details}",
                ApplicationInfo.DefaultProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        _host?.Dispose();
        _host = null;
        Shutdown(ExitCodes.StartupFailure);
    }
}
