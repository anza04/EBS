using Ebm.Hosting.ErrorHandling;

namespace Ebm.Tests.Unit.Hosting;

public sealed class StartupFailureLogTests
{
    [Fact]
    public void Writes_exception_details_to_the_given_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ebm-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = StartupFailureLog.TryWrite(new InvalidOperationException("bad config"), dir);

            Assert.NotNull(path);
            Assert.StartsWith(dir, path, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("bad config", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
