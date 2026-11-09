using Ebm.Services.Editing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ebm.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the game services layer.
    /// </summary>
    /// <remarks>
    /// Every service that needs the current real-world time must take the shared <see cref="TimeProvider"/>
    /// from DI so tests can substitute a fake clock. Use cases that write data create their own DI scope
    /// per unit of work, so they are registered as singletons.
    /// </remarks>
    public static IServiceCollection AddGameServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDatabaseEditor, DatabaseEditor>();

        return services;
    }
}
