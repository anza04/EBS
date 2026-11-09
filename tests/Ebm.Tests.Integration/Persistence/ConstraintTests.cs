using Ebm.Domain.Clubs;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Services.Data;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Data.Sqlite;

namespace Ebm.Tests.Integration.Persistence;

/// <summary>The schema itself rejects invalid data, even when domain validation is bypassed.</summary>
public sealed class ConstraintTests
{
    [Fact]
    public async Task Duplicate_country_code_is_rejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SampleWorld.SeedAsync(db);

        await Assert.ThrowsAsync<DataConstraintException>(() =>
            db.WriteAsync(s => s.Repo<Country, CountryId>().Add(new Country(CountryId.New(), "ITA", "Italy again"))));
    }

    [Fact]
    public async Task Reference_to_missing_record_is_rejected()
    {
        await using var db = await TestDatabase.CreateAsync();

        await Assert.ThrowsAsync<DataConstraintException>(() =>
            db.WriteAsync(s => s.Repo<Club, ClubId>().Add(new Club(ClubId.New(), "Orphan", "ORP", CountryId.New()))));
    }

    [Fact]
    public async Task Deleting_a_referenced_record_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var world = await SampleWorld.SeedAsync(db);

        await Assert.ThrowsAsync<DataConstraintException>(() => db.WriteAsync(s =>
            s.Repo<Club, ClubId>().Remove(s.Repo<Club, ClubId>().FindAsync(world.HomeClub.Id).GetAwaiter().GetResult()!)));

        Assert.NotNull(await db.ReadAsync(s => s.Repo<Club, ClubId>().FindAsync(world.HomeClub.Id)));
    }

    [Fact]
    public async Task Player_cannot_have_two_current_club_spells()
    {
        await using var db = await TestDatabase.CreateAsync();
        var world = await SampleWorld.SeedAsync(db);

        await Assert.ThrowsAsync<DataConstraintException>(() => db.WriteAsync(s =>
            s.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>().Add(
                new PlayerClubHistoryEntry(PlayerClubHistoryEntryId.New(), world.Player.Id, world.AwayClub.Id, SampleWorld.SeasonStart.AddDays(10)))));
    }

    [Theory]
    [InlineData("UPDATE Matches SET AwayScore = HomeScore")]
    [InlineData("UPDATE Fixtures SET AwayClubId = HomeClubId")]
    [InlineData("UPDATE Competitions SET RulesJson = '[1,2]'")]
    [InlineData("UPDATE Competitions SET RulesJson = 'not json'")]
    [InlineData("UPDATE PlayerAttributes SET Shooting = 101")]
    [InlineData("UPDATE PlayerMatchStatistics SET TwoPointMade = TwoPointAttempted + 1")]
    [InlineData("UPDATE Contracts SET PlayerId = NULL, StaffMemberId = NULL")]
    [InlineData("UPDATE Seasons SET EndDate = StartDate")]
    [InlineData("UPDATE Players SET HeightCm = 400")]
    public async Task Check_constraints_reject_invalid_rows(string sql)
    {
        await using var db = await TestDatabase.CreateAsync();
        await SampleWorld.SeedAsync(db);

        var ex = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteSqlAsync(sql));

        Assert.Contains("constraint failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
