using System.Reflection;
using Ebm.Services.Import;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.TestSupport;

/// <summary>
/// The real-leagues dataset (data/real-leagues, build/generate-real-leagues-dataset.py): the full world of the slow suite.
/// </summary>
internal static class RealLeagues
{
    public static readonly string Folder = Path.Combine(
        typeof(RealLeagues).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "real-leagues");

    /// <summary>Countries, leagues, competitions, clubs and entries.</summary>
    public static readonly string World = Path.Combine(Folder, "real-leagues-2026-27.json");

    public static readonly string[] Files =
    [
        World,
        Path.Combine(Folder, "players-real-leagues-2026-27.csv"),
        Path.Combine(Folder, "contracts-real-leagues-2026-27.csv"),
        Path.Combine(Folder, "playerHistory-real-leagues-2026-27.csv"),
    ];

    /// <summary>Imports the whole dataset into a new game.</summary>
    public static async Task ImportAsync(TestDatabase db)
    {
        var report = await db.Services.GetRequiredService<IDataImportService>().ImportAsync(Files, new ImportOptions());
        Assert.True(report.Committed);
    }
}
