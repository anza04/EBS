namespace Ebm.Desktop.Startup;

/// <summary>
/// Desktop-specific command-line switches. Everything else is passed to the host as configuration.
/// </summary>
/// <param name="SmokeTest">
/// <c>--smoke-test</c>: start normally, close as soon as the main window has rendered, exit with 0,
/// and never show dialogs. Used by automated launch tests.
/// </param>
/// <param name="HostArgs">Remaining arguments, e.g. <c>--Ebm:Paths:DataRoot=C:\EbmData</c>.</param>
public sealed record LaunchOptions(bool SmokeTest, string[] HostArgs)
{
    public const string SmokeTestSwitch = "--smoke-test";

    /// <summary>Whether modal dialogs may be shown to a user.</summary>
    public bool Interactive => !SmokeTest;

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var smoke = args.Any(a => string.Equals(a, SmokeTestSwitch, StringComparison.OrdinalIgnoreCase));
        var rest = args.Where(a => !string.Equals(a, SmokeTestSwitch, StringComparison.OrdinalIgnoreCase)).ToArray();

        return new LaunchOptions(smoke, rest);
    }
}
