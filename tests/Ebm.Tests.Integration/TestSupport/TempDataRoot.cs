namespace Ebm.Tests.Integration.TestSupport;

/// <summary>Isolated data folder for one test; deleted on dispose.</summary>
public sealed class TempDataRoot : IDisposable
{
    public TempDataRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ebm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string LogsDirectory => System.IO.Path.Combine(Path, "logs");

    public string HostArgument => $"--Ebm:Paths:DataRoot={Path}";

    /// <summary>Concatenated contents of all rolling log files, read without locking them.</summary>
    public string ReadAllLogs()
    {
        if (!Directory.Exists(LogsDirectory))
        {
            return string.Empty;
        }

        return string.Concat(Directory.GetFiles(LogsDirectory, "ebm-*.log").Select(ReadShared));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A log file may still be held briefly by a terminating process; temp cleanup is best effort.
        }
    }

    private static string ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
