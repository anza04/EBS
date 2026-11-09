using Ebm.Domain.Common;
using Ebm.Hosting;
using Ebm.Hosting.Configuration;
using Ebm.Hosting.ErrorHandling;
using Ebm.Tests.Unit.TestDoubles;
using Microsoft.Extensions.Logging;

namespace Ebm.Tests.Unit.Hosting;

public sealed class ExceptionHandlingPolicyTests
{
    private readonly RecordingLogger<ExceptionHandlingPolicy> _logger = new();
    private readonly AppPaths _paths = AppPaths.Resolve(new PathsOptions { DataRoot = @"C:\EbmTestData" });

    private ExceptionHandlingPolicy CreatePolicy() => new(_logger, _paths);

    [Fact]
    public void Domain_exception_on_ui_thread_is_shown_and_application_continues()
    {
        var decision = CreatePolicy().Handle(new DomainException("Roster is full."), ExceptionSource.UiThread);

        Assert.False(decision.ShouldShutdown);
        Assert.Equal("Roster is full.", decision.UserMessage);
        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public void Unexpected_exception_on_ui_thread_shuts_down_and_points_to_logs()
    {
        var exception = new InvalidOperationException("boom");

        var decision = CreatePolicy().Handle(exception, ExceptionSource.UiThread);

        Assert.True(decision.ShouldShutdown);
        Assert.Contains(_paths.LogsDirectory, decision.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("boom", decision.UserMessage, StringComparison.Ordinal);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Critical, entry.Level);
        Assert.Same(exception, entry.Exception);
    }

    [Fact]
    public void Background_thread_exception_is_critical_even_for_domain_exceptions()
    {
        var decision = CreatePolicy().Handle(new DomainException("rule"), ExceptionSource.BackgroundThread);

        Assert.True(decision.ShouldShutdown);
        Assert.Equal(LogLevel.Critical, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public void Unobserved_task_exception_is_logged_silently()
    {
        var decision = CreatePolicy().Handle(new AggregateException(new TimeoutException()), ExceptionSource.UnobservedTask);

        Assert.False(decision.ShouldShutdown);
        Assert.Null(decision.UserMessage);
        Assert.Equal(LogLevel.Error, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public void Null_exception_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => CreatePolicy().Handle(null!, ExceptionSource.UiThread));
    }
}
