using Ebm.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Unit.Services;

public sealed class GameServicesRegistrationTests
{
    [Fact]
    public void Registers_system_time_provider()
    {
        using var provider = new ServiceCollection().AddGameServices().BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Does_not_replace_a_time_provider_registered_earlier()
    {
        var fake = new FixedTimeProvider();
        using var provider = new ServiceCollection()
            .AddSingleton<TimeProvider>(fake)
            .AddGameServices()
            .BuildServiceProvider();

        Assert.Same(fake, provider.GetRequiredService<TimeProvider>());
    }

    private sealed class FixedTimeProvider : TimeProvider;
}
