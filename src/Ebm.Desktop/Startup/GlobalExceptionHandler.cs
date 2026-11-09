using System.Windows;
using System.Windows.Threading;
using Ebm.Hosting;
using Ebm.Hosting.ErrorHandling;

namespace Ebm.Desktop.Startup;

/// <summary>
/// Connects WPF and runtime unhandled-exception events to the UI-agnostic <see cref="ExceptionHandlingPolicy"/>.
/// </summary>
public sealed class GlobalExceptionHandler
{
    private readonly ExceptionHandlingPolicy _policy;
    private readonly LaunchOptions _launch;
    private Application? _application;

    public GlobalExceptionHandler(ExceptionHandlingPolicy policy, LaunchOptions launch)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(launch);

        _policy = policy;
        _launch = launch;
    }

    public void Attach(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        _application = application;
        application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public void Detach()
    {
        if (_application is not null)
        {
            _application.DispatcherUnhandledException -= OnDispatcherUnhandledException;
            _application = null;
        }

        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var decision = _policy.Handle(e.Exception, ExceptionSource.UiThread);
        e.Handled = true;
        Notify(decision);

        if (decision.ShouldShutdown)
        {
            _application?.Shutdown(ExitCodes.UnhandledException);
        }
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException($"Non-exception object thrown: {e.ExceptionObject}");

        // The runtime terminates the process after this handler; logging is synchronous so nothing is lost.
        var decision = _policy.Handle(exception, ExceptionSource.BackgroundThread);
        Notify(decision);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _policy.Handle(e.Exception, ExceptionSource.UnobservedTask);
        e.SetObserved();
    }

    private void Notify(ExceptionHandlingDecision decision)
    {
        if (!_launch.Interactive || decision.UserMessage is null)
        {
            return;
        }

        try
        {
            MessageBox.Show(
                decision.UserMessage,
                ApplicationInfo.DefaultProductName,
                MessageBoxButton.OK,
                decision.ShouldShutdown ? MessageBoxImage.Error : MessageBoxImage.Warning);
        }
        catch (InvalidOperationException)
        {
            // A dialog cannot be shown from this thread or during shutdown; the error is already logged.
        }
    }
}
