using System.Reflection;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Services.Import;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Import;

/// <summary>
/// The Italian league dataset (data/italy, a subset of data/europe) imports on its own: Serie A and Serie A2 clubs,
/// their players and contracts, and past spells at Italian clubs only.
/// </summary>
public sealed class ItalyDatasetTests : IAsyncLifetime
{
    private static readonly string Folder = Path.Combine(
        typeof(ItalyDatasetTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "italy");

    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Italian_league_imports_on_its_own()
    {
        string[] files =
        [
            Path.Combine(Folder, "italy-2026-27.json"),
            Path.Combine(Folder, "players-italy-2026-27.csv"),
            Path.Combine(Folder, "contracts-italy-2026-27.csv"),
            Path.Combine(Folder, "playerHistory-italy-2026-27.csv"),
        ];
        var report = await _db.Services.GetRequiredService<IDataImportService>().ImportAsync(files, new ImportOptions());
        Assert.Empty(report.Problems);
        Assert.Equal(0, report.Rejected);
        Assert.True(report.Committed);

        var clubs = await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync());
        var players = await _db.ReadAsync(s => s.Repo<Player, PlayerId>().ListAsync());
        var contracts = await _db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync());
        Assert.Equal(36, clubs.Count);
        Assert.Equal(16, clubs.Count(c => c.DataStatus == DataStatus.Unverified));
        Assert.Equal(456, players.Count);
        Assert.Equal(players.Count, contracts.Count);
    }
}
