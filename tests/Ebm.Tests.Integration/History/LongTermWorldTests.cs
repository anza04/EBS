using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.History;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Services.Careers;
using Ebm.Services.History;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.History;

/// <summary>Step 32 acceptance: the sample world over three seasons keeps its history and stays balanced (docs/HISTORY.md).</summary>
[Trait("Category", "Slow")]
public sealed class LongTermWorldTests : IAsyncLifetime
{
    private const int Seasons = 3;

    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task The_world_progresses_over_many_seasons_and_keeps_its_history()
    {
        var careers = _db.Services.GetRequiredService<ICareerService>();
        var history = _db.Services.GetRequiredService<IHistoryService>();
        await HistoryServiceTests.StartSampleCareerAsync(_db);
        var startReputation = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync())).Average(c => c.Reputation);
        var startPlayers = (await _db.ReadAsync(s => s.Repo<Player, PlayerId>().ListAsync())).Count;
        var seasonsPlayed = (await _db.ReadAsync(s => s.Repo<Ebm.Domain.Careers.Career, Ebm.Domain.Careers.CareerId>().ListAsync())).Single().SeasonsPlayed;
        var played = new List<SeasonId>();
        for (var i = 0; i < Seasons; i++)
        {
            played.Add((await careers.LoadAsync())!.Season.Id);
            await HistoryServiceTests.PlaySeasonAsync(careers);
            await careers.StartNextSeasonAsync();
            await careers.ContinueAsync();
        }

        Assert.Equal(seasonsPlayed + Seasons, (await _db.ReadAsync(s => s.Repo<Ebm.Domain.Careers.Career, Ebm.Domain.Careers.CareerId>().ListAsync())).Single().SeasonsPlayed);

        // Every season has its awards, winners and snapshot in every league.
        var leagues = (await _db.ReadAsync(s => s.Repo<Competition, CompetitionId>().ListAsync(c => c.Type == CompetitionType.League))).Where(l => l.Name.StartsWith("Serie", StringComparison.Ordinal)).ToList();
        foreach (var league in leagues)
        {
            Assert.Equal(Seasons, (await history.GetWinnersAsync(league.Id)).Count);
            Assert.Equal(Seasons, (await history.GetLeagueEvolutionAsync(league.Id)).Count);
            var awards = await history.GetAwardsAsync(league.Id);
            Assert.All(played, season => Assert.Equal(12, awards.Count(a => a.SeasonId == season)));
            Assert.NotEmpty(await history.GetRecordsAsync(league.Id));
        }

        // New players entered every year; retired players are kept with their careers.
        var players = await _db.ReadAsync(s => s.Repo<Player, PlayerId>().ListAsync());
        var academy = players.Where(p => p.YouthClubId is not null).ToList();
        Assert.True(players.Count > startPlayers);
        var clubIds = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync())).Select(c => c.Id).ToList();
        Assert.All(clubIds, c => Assert.InRange(academy.Count(p => p.YouthClubId == c), Seasons, 2 * Seasons)); // one intake a year
        var retired = players.Where(p => p.IsRetired).ToList();
        Assert.NotEmpty(retired);
        foreach (var player in retired)
        {
            Assert.NotNull(await history.GetPlayerCareerAsync(player.Id));
        }

        // An award winner's career lists his award and his seasons.
        var mvp = (await history.GetAwardsAsync(leagues[0].Id)).First(a => a.Kind == AwardKind.MostValuablePlayer);
        var career = (await history.GetPlayerCareerAsync(mvp.PlayerId!.Value))!;
        Assert.Contains(career.Awards, a => a.Kind == AwardKind.MostValuablePlayer && a.SeasonId == mvp.SeasonId);
        Assert.Contains(career.Seasons, l => l.Season == mvp.Season);

        // Long-term balance: reputations stay in range and the world's mean does not run away.
        var clubs = await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync());
        Assert.All(clubs, c => Assert.InRange(c.Reputation, Club.MinReputation, Club.MaxReputation));
        Assert.InRange(clubs.Average(c => c.Reputation), startReputation - 10, startReputation + 10);
    }
}
