using Ebm.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Ebm.Hosting.ErrorHandling;

public enum ExceptionSource
{
    /// <summary>Unhandled on the UI thread; the UI layer can mark it handled and keep running.</summary>
    UiThread,

    /// <summary>Unhandled on a background thread; the runtime terminates the process.</summary>
    BackgroundThread,

    /// <summary>A faulted task nobody awaited; can be observed and ignored.</summary>
    UnobservedTask,
}

/// <param name="ShouldShutdown">The application must close because its state can no longer be trusted.</param>
/// <param name="UserMessage">Text to show to the player, or <see langword="null"/> to stay silent.</param>
public sealed record ExceptionHandlingDecision(bool ShouldShutdown, string? UserMessage);

/// <summary>
/// UI-agnostic rules deciding how an unhandled exception is logged and what happens next.
/// The UI layer subscribes to the platform's unhandled-exception events and applies the decision.
/// </summary>
/// <remarks>
/// Unexpected exceptions close the application rather than letting it continue in an unknown
/// state, to protect databases and save files from being written with corrupted data.
/// </remarks>
public sealed class ExceptionHandlingPolicy
{
    private readonly ILogger<ExceptionHandlingPolicy> _logger;
    private readonly AppPaths _paths;

    public ExceptionHandlingPolicy(ILogger<ExceptionHandlingPolicy> logger, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(paths);

        _logger = logger;
        _paths = paths;
    }

    public ExceptionHandlingDecision Handle(Exception exception, ExceptionSource source)
    {
        ArgumentNullException.ThrowIfNull(exception);

        switch (source)
        {
            case ExceptionSource.UnobservedTask:
                _logger.LogError(exception, "Unobserved task exception");
                return new ExceptionHandlingDecision(ShouldShutdown: false, UserMessage: null);

            case ExceptionSource.UiThread when exception is DomainException:
                _logger.LogWarning(exception, "Game rule violation reached the UI unhandled");
                return new ExceptionHandlingDecision(ShouldShutdown: false, UserMessage: exception.Message);

            default:
                _logger.LogCritical(exception, "Unhandled exception on {Source}; the application will close", source);
                return new ExceptionHandlingDecision(
                    ShouldShutdown: true,
                    UserMessage:
                        "An unexpected error occurred and the game must close to protect your data." +
                        Environment.NewLine + Environment.NewLine +
                        "Details were written to the log folder:" + Environment.NewLine +
                        _paths.LogsDirectory);
        }
    }
}
