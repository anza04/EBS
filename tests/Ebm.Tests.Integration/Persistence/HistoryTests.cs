using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Tests.Integration.TestSupport;

namespace Ebm.Tests.Integration.Persistence;

/// <summary>A player's career and statistics survive a move to another club.</summary>
public sealed class HistoryTests
{
    [Fact]
    public async Task Moving_club_keeps_previous_spell_contract_and_statistics()
    {
        await using var db = await TestDatabase.CreateAsync();
        var world = await SampleWorld.SeedAsync(db);
        var moveDate = SampleWorld.SeasonStart.AddMonths(4);

        // Transfer: close the current spell, open a new one, sign a new contract.
        await db.WriteAsync(s =>
        {
            var history = s.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>();
            history.FindAsync(world.PlayerSpell.Id).GetAwaiter().GetResult()!.Close(moveDate);
            history.Add(new PlayerClubHistoryEntry(PlayerClubHistoryEntryId.New(), world.Player.Id, world.AwayClub.Id, moveDate.AddDays(1)));
            s.Repo<Contract, ContractId>().Add(
                Contract.ForPlayer(ContractId.New(), world.AwayClub.Id, world.Player.Id, moveDate.AddDays(1), moveDate.AddYears(3), 400_000));
        });

        var (career, stats, contracts) = await db.ReadAsync(async s => (
            (await s.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>().ListAsync(h => h.PlayerId == world.Player.Id)).OrderBy(h => h.StartDate).ToList(),
            await s.Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>().ListAsync(x => x.PlayerId == world.Player.Id),
            await s.Repo<Contract, ContractId>().ListAsync(c => c.PlayerId == world.Player.Id)));

        Assert.Collection(
            career,
            first => Assert.Equal((world.HomeClub.Id, (DateOnly?)moveDate), (first.ClubId, first.EndDate)),
            second => Assert.Equal((world.AwayClub.Id, true), (second.ClubId, second.IsCurrent)));

        var line = Assert.Single(stats);
        Assert.Equal(world.HomeClub.Id, line.ClubId);
        Assert.Equal(25, line.BoxScore.Points);

        Assert.Equal(2, contracts.Count);
    }

    [Fact]
    public async Task Player_with_history_cannot_be_deleted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var world = await SampleWorld.SeedAsync(db);

        await Assert.ThrowsAsync<Ebm.Services.Data.DataConstraintException>(() => db.WriteAsync(s =>
            s.Repo<Player, PlayerId>().Remove(s.Repo<Player, PlayerId>().FindAsync(world.Player.Id).GetAwaiter().GetResult()!)));

        var stats = await db.ReadAsync(s => s.Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>().ListAsync(x => x.PlayerId == world.Player.Id));
        Assert.Single(stats);
    }

    [Fact]
    public async Task Club_history_is_queryable_from_the_club_side()
    {
        await using var db = await TestDatabase.CreateAsync();
        var world = await SampleWorld.SeedAsync(db);

        var formerAndCurrent = await db.ReadAsync(s =>
            s.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>().ListAsync(h => h.ClubId == world.HomeClub.Id));

        Assert.Equal(world.Player.Id, Assert.Single(formerAndCurrent).PlayerId);
        Assert.NotNull(await db.ReadAsync(s => s.Repo<Club, ClubId>().FindAsync(world.HomeClub.Id)));
    }
}
