using System.Reflection;
using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Services.Import;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Import;

/// <summary>
/// The real-leagues dataset (data/real-leagues, build/generate-real-leagues-dataset.py) imports on its own: eight real
/// divisions with 138 real clubs, the real players with contracts and the rest as free agents.
/// </summary>
public sealed class RealLeaguesDatasetTests : IAsyncLifetime
{
    private static readonly string Folder = Path.Combine(
        typeof(RealLeaguesDatasetTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "real-leagues");

    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Real_leagues_import_on_their_own()
    {
        string[] files =
        [
            Path.Combine(Folder, "real-leagues-2026-27.json"),
            Path.Combine(Folder, "players-real-leagues-2026-27.csv"),
            Path.Combine(Folder, "contracts-real-leagues-2026-27.csv"),
            Path.Combine(Folder, "playerHistory-real-leagues-2026-27.csv"),
        ];
        var report = await _db.Services.GetRequiredService<IDataImportService>().ImportAsync(files, new ImportOptions());
        Assert.Empty(report.Problems);
        Assert.Empty(report.Records.Where(r => r.Outcome == ImportOutcome.Rejected).Select(r => $"{r.Location} {r.Label}: {string.Join(" | ", r.Messages)}"));
        Assert.True(report.Committed);

        var clubs = await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync());
        var competitions = await _db.ReadAsync(s => s.Repo<Competition, CompetitionId>().ListAsync());
        var players = await _db.ReadAsync(s => s.Repo<Player, PlayerId>().ListAsync());
        var contracts = await _db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync());
        Assert.Equal(138, clubs.Count);
        Assert.Equal(8, competitions.Count);
        Assert.Equal(File.ReadLines(files[1]).Count() - 1, players.Count);
        var contracted = contracts.Select(c => c.PlayerId).ToHashSet();
        Assert.InRange(players.Count(p => !contracted.Contains(p.Id)), 400, 700); // free agents
        Assert.All(clubs, c => Assert.InRange(contracts.Count(k => k.ClubId == c.Id), 12, 30));
    }
}

