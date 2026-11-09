using Ebm.Desktop.ViewModels;
using Ebm.Desktop.ViewModels.Editor;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Desktop.Startup;

public static class DesktopServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopUi(this IServiceCollection services, LaunchOptions launch)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(launch);

        services.AddSingleton(launch);
        services.AddSingleton<GlobalExceptionHandler>();

        services.AddSingleton<IFilePicker, FilePicker>();
        services.AddSingleton<DatabaseEditorViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
