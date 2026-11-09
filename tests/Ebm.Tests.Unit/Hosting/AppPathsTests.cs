using Ebm.Hosting;
using Ebm.Hosting.Configuration;

namespace Ebm.Tests.Unit.Hosting;

public sealed class AppPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_data_root_uses_local_app_data(string? dataRoot)
    {
        var paths = AppPaths.Resolve(new PathsOptions { DataRoot = dataRoot });

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppPaths.DefaultFolderName);
        Assert.Equal(expected, paths.DataRoot);
        Assert.Equal(Path.Combine(expected, "logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(expected, "databases", AppPaths.DatabaseFileName), paths.DatabaseFile);
    }

    [Fact]
    public void Relative_data_root_is_made_absolute()
    {
        var paths = AppPaths.Resolve(new PathsOptions { DataRoot = "some-data" });

        Assert.True(Path.IsPathFullyQualified(paths.DataRoot));
        Assert.EndsWith("some-data", paths.DataRoot, StringComparison.Ordinal);
    }

    [Fact]
    public void Environment_variables_are_expanded()
    {
        var paths = AppPaths.Resolve(new PathsOptions { DataRoot = Path.Combine("%TEMP%", "ebm-test") });

        Assert.DoesNotContain("%", paths.DataRoot, StringComparison.Ordinal);
        Assert.StartsWith(Path.GetFullPath(Environment.GetEnvironmentVariable("TEMP")!), paths.DataRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureCreated_creates_missing_folders_and_is_idempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ebm-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Resolve(new PathsOptions { DataRoot = root });

            paths.EnsureCreated();
            paths.EnsureCreated();

            Assert.True(Directory.Exists(paths.DataRoot));
            Assert.True(Directory.Exists(paths.LogsDirectory));
            Assert.True(Directory.Exists(paths.DatabasesDirectory));
            Assert.True(Directory.Exists(paths.ImportLogsDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
