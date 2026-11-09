using Ebm.Hosting;

namespace Ebm.Tests.Unit.Hosting;

public sealed class ApplicationInfoTests
{
    [Fact]
    public void Reads_product_and_version_from_build_properties()
    {
        var info = ApplicationInfo.FromAssembly(typeof(ApplicationInfo).Assembly);

        Assert.Equal(ApplicationInfo.DefaultProductName, info.ProductName);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", info.Version);
        Assert.StartsWith(info.Version, info.InformationalVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void Version_never_contains_build_metadata()
    {
        var info = ApplicationInfo.FromAssembly(typeof(ApplicationInfo).Assembly);

        Assert.DoesNotContain("+", info.Version, StringComparison.Ordinal);
    }
}
