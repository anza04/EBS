using System.Reflection;

namespace Ebm.Hosting;

/// <summary>
/// Product name and version of the running build.
/// </summary>
/// <param name="ProductName">Display name of the product.</param>
/// <param name="Version">Semantic version without build metadata, e.g. <c>0.1.0</c>.</param>
/// <param name="InformationalVersion">Full informational version, possibly with <c>+commit</c> metadata.</param>
public sealed record ApplicationInfo(string ProductName, string Version, string InformationalVersion)
{
    public const string DefaultProductName = "European Basketball Manager";

    public static ApplicationInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        var metadataStart = informational.IndexOf('+', StringComparison.Ordinal);
        var version = metadataStart >= 0 ? informational[..metadataStart] : informational;

        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        if (string.IsNullOrWhiteSpace(product))
        {
            product = DefaultProductName;
        }

        return new ApplicationInfo(product, version, informational);
    }
}
