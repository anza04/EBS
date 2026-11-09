using Ebm.Hosting;
using Ebm.Hosting.ErrorHandling;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ebm.Tests.Integration;

public sealed class HostCompositionTests
{
    [Fact]
    public async Task Host_builds_starts_and_resolves_core_services()
    {
        using var data = new TempDataRoot();

        using var host = EbmHost.CreateBuilder([data.HostArgument]).Build();
        await host.StartAsync();

        Assert.Equal(data.Path, host.Services.GetRequiredService<AppPaths>().DataRoot);
        Assert.NotNull(host.Services.GetRequiredService<ApplicationInfo>());
        Assert.NotNull(host.Services.GetRequiredService<ExceptionHandlingPolicy>());
        Assert.NotNull(host.Services.GetRequiredService<TimeProvider>());

        await host.StopAsync();
    }

    [Fact]
    public async Task Logging_writes_to_a_daily_file_in_the_data_root()
    {
        using var data = new TempDataRoot();
        var marker = Guid.NewGuid().ToString("N");

        using (var host = EbmHost.CreateBuilder([data.HostArgument]).Build())
        {
            await host.StartAsync();
            host.Services.GetRequiredService<ILogger<HostCompositionTests>>()
                .LogInformation("integration marker {Marker}", marker);
            await host.StopAsync();
        }

        var file = Assert.Single(Directory.GetFiles(data.LogsDirectory, "ebm-*.log"));
        Assert.Matches(@"ebm-\d{8}\.log$", file);
        var logs = data.ReadAllLogs();
        Assert.Contains($"integration marker {marker}", logs, StringComparison.Ordinal);
        Assert.Contains("[INF]", logs, StringComparison.Ordinal);
        Assert.Contains(typeof(HostCompositionTests).FullName!, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Minimum_log_level_is_configurable()
    {
        using var data = new TempDataRoot();

        using (var host = EbmHost.CreateBuilder([data.HostArgument, "--Ebm:Logging:MinimumLevel=Warning"]).Build())
        {
            await host.StartAsync();
            var logger = host.Services.GetRequiredService<ILogger<HostCompositionTests>>();
            logger.LogInformation("should-be-filtered");
            logger.LogWarning("should-be-written");
            await host.StopAsync();
        }

        var logs = data.ReadAllLogs();
        Assert.DoesNotContain("should-be-filtered", logs, StringComparison.Ordinal);
        Assert.Contains("should-be-written", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exception_policy_logs_with_stack_trace()
    {
        using var data = new TempDataRoot();

        using (var host = EbmHost.CreateBuilder([data.HostArgument]).Build())
        {
            await host.StartAsync();
            host.Services.GetRequiredService<ExceptionHandlingPolicy>()
                .Handle(new InvalidOperationException("policy-test-failure"), ExceptionSource.UiThread);
            await host.StopAsync();
        }

        var logs = data.ReadAllLogs();
        Assert.Contains("[FTL]", logs, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: policy-test-failure", logs, StringComparison.Ordinal);
    }
}
