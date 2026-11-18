using System.IO;
using Ebm.Desktop.ViewModels;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Hosting;
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
        services.AddSingleton(sp => new ClubLockStore(Path.Combine(sp.GetRequiredService<AppPaths>().DataRoot, "locked-club.txt")));
        services.AddSingleton<DatabaseEditorViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
