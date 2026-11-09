using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Services.Clubs;
using Ebm.Services.Data;
using Ebm.Services.Editing;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Clubs;

public sealed class ClubServiceTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 11, 9);
    private static readonly Position[] Positions = Enum.GetValues<Position>();

    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private IClubService Clubs => _db.Services.GetRequiredService<IClubService>();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(new FixedTimeProvider(Today));
        _world = await SampleWorld.SeedAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    /// <summary>Adds players with a contract at the home club; the sample player is already there.</summary>
    private async Task<List<Player>> SignAsync(int count, CountryId? nationality = null)
    {
        var players = Enumerable.Range(0, count).Select(i => new Player(
                PlayerId.New(), $"Squad{i}", $"Player{Guid.NewGuid():N}"[..20], new DateOnly(1996 + (i % 8), 3, 1), nationality ?? _world.Italy.Id,
                185 + (i % 5 * 5), 90, Positions[i % 5], PlayerAttributes.Uniform(50 + i)))
            .ToList();
        await _db.WriteAsync(s =>
        {
            foreach (var p in players)
            {
                s.Repo<Player, PlayerId>().Add(p);
                s.Repo<Contract, ContractId>().Add(Contract.ForPlayer(ContractId.New(), _world.HomeClub.Id, p.Id, SampleWorld.SeasonStart, SampleWorld.SeasonStart.AddYears(2), 100_000));
            }
        });
        return players;
    }

    /// <summary>Five starters at their primary positions, five in the rotation, the rest on the bench.</summary>
    private static List<RosterAssignment> CompleteLineup(ClubRoster roster)
    {
        var assignments = new List<RosterAssignment>();
        var open = Positions.ToList();
        foreach (var player in roster.Players)
        {
            if (open.Remove(player.Player.Position))
            {
                assignments.Add(new RosterAssignment(player.Player.Id, RosterRole.Starter, player.Player.Position));
            }
            else
            {
                var rotation = assignments.Count(a => a.Role == RosterRole.Rotation) < 5;
                assignments.Add(new RosterAssignment(player.Player.Id, rotation ? RosterRole.Rotation : RosterRole.Bench));
            }
        }

        return assignments;
    }

    [Fact]
    public async Task A_club_can_have_a_complete_valid_roster()
    {
        await SignAsync(11);
        var roster = (await Clubs.GetRosterAsync(_world.HomeClub.Id))!;
        Assert.Equal(12, roster.Players.Count);
        Assert.All(roster.Players, p => Assert.Equal(RosterRole.Bench, p.Role)); // no stored spot yet
        Assert.True(roster.Validation.Has(RosterIssueCode.StartingFiveIncomplete));

        var lineup = CompleteLineup(roster);
        var saved = await Clubs.SaveRosterAsync(_world.HomeClub.Id, lineup);

        Assert.True(saved.IsValid, string.Join(" | ", saved.Validation.Issues.Select(i => i.Message)));
        Assert.Empty(saved.Validation.Issues);
        var reloaded = (await Clubs.GetRosterAsync(_world.HomeClub.Id))!;
        Assert.Equal(Positions, reloaded.Starters.Select(p => p.LineupPosition!.Value));
        Assert.All(reloaded.Starters, s => Assert.Equal(s.Player.Position, s.LineupPosition));
        var rotation = lineup.Where(a => a.Role == RosterRole.Rotation).Select(a => a.PlayerId).ToList();
        Assert.Equal(rotation, reloaded.Rotation.Select(p => p.Player.Id));
        Assert.Equal([1, 2, 3, 4, 5], reloaded.Rotation.Select(p => p.RotationOrder!.Value));
        Assert.Equal([_world.Championship.Id, _world.Cup.Id], reloaded.Registrations.Select(r => r.Competition.Id));
        Assert.All(reloaded.Registrations, r => Assert.True(r.Validation.IsValid));
    }

    [Fact]
    public async Task Rotation_order_follows_the_order_of_the_saved_list()
    {
        await SignAsync(11);
        var lineup = CompleteLineup((await Clubs.GetRosterAsync(_world.HomeClub.Id))!);
        var reversed = lineup.Where(a => a.Role == RosterRole.Rotation).Reverse().Concat(lineup.Where(a => a.Role != RosterRole.Rotation)).ToList();

        var saved = await Clubs.SaveRosterAsync(_world.HomeClub.Id, reversed);

        Assert.Equal(reversed.Where(a => a.Role == RosterRole.Rotation).Select(a => a.PlayerId), saved.Rotation.Select(p => p.Player.Id));
    }

    [Fact]
    public async Task Invalid_configurations_are_saved_and_identified()
    {
        await SignAsync(11);
        var lineup = CompleteLineup((await Clubs.GetRosterAsync(_world.HomeClub.Id))!);
        var starters = lineup.Where(a => a.Role == RosterRole.Starter).ToList();
        lineup[lineup.IndexOf(starters[0])] = starters[0] with { Role = RosterRole.Inactive, LineupPosition = null };
        lineup[lineup.IndexOf(starters[1])] = starters[1] with { LineupPosition = starters[2].LineupPosition };

        var saved = await Clubs.SaveRosterAsync(_world.HomeClub.Id, lineup);

        Assert.False(saved.IsValid);
        Assert.True(saved.Validation.Has(RosterIssueCode.StartingFiveIncomplete));
        Assert.True(saved.Validation.Has(RosterIssueCode.StartingPositionTaken));
        Assert.Equal(4, (await Clubs.GetRosterAsync(_world.HomeClub.Id))!.Starters.Count());
    }

    [Fact]
    public async Task Injured_starter_is_reported()
    {
        var players = await SignAsync(11);
        var roster = (await Clubs.GetRosterAsync(_world.HomeClub.Id))!;
        var lineup = CompleteLineup(roster);
        var starter = lineup.First(a => a.Role == RosterRole.Starter).PlayerId;
        await _db.ReadAsync(async s =>
        {
            var player = (await s.Repo<Player, PlayerId>().FindAsync(starter))!;
            player.UpdateCondition(100, 50, InjuryStatus.Injured, Today.AddDays(20));
            return await s.Services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        });

        var saved = await Clubs.SaveRosterAsync(_world.HomeClub.Id, lineup);

        var issue = Assert.Single(saved.Validation.Errors);
        Assert.Equal((RosterIssueCode.UnavailableStarter, (PlayerId?)starter), (issue.Code, issue.PlayerId));
        Assert.Equal(PlayerAvailability.Unavailable, saved.Players.Single(p => p.Player.Id == starter).Availability);
    }

    [Fact]
    public async Task Competition_specific_registration_rules_are_enforced()
    {
        var spain = new Country(CountryId.New(), "ESP", "Spain");
        await _db.WriteAsync(s => s.Repo<Country, CountryId>().Add(spain));
        await SignAsync(9);
        await SignAsync(2, spain.Id);
        var c = _world.Championship;
        await _db.Services.GetRequiredService<IDatabaseEditor>().SaveCompetitionAsync(new CompetitionDraft(
            c.Id, c.Name, c.ShortName, c.Type, c.CountryId, c.DivisionId, """{"registration":{"squad":{"maxForeignPlayers":1}}}"""));

        var saved = await Clubs.SaveRosterAsync(_world.HomeClub.Id, CompleteLineup((await Clubs.GetRosterAsync(_world.HomeClub.Id))!));

        Assert.True(saved.Validation.IsValid); // the default club rules have no foreign limit
        var league = saved.Registrations.Single(r => r.Competition.Id == c.Id);
        Assert.Equal(RosterIssueCode.TooManyForeignPlayers, Assert.Single(league.Validation.Errors).Code);
        Assert.True(saved.Registrations.Single(r => r.Competition.Id == _world.Cup.Id).Validation.IsValid);
        Assert.False(saved.IsValid);
        Assert.Equal(2, saved.Players.Count(p => p.IsForeign));
    }

    [Fact]
    public async Task Saving_requires_every_club_player_exactly_once()
    {
        await SignAsync(2);
        var lineup = CompleteLineup((await Clubs.GetRosterAsync(_world.HomeClub.Id))!);

        await Assert.ThrowsAsync<DomainException>(() => Clubs.SaveRosterAsync(_world.HomeClub.Id, lineup[1..]));
        await Assert.ThrowsAsync<DomainValidationException>(() => Clubs.SaveRosterAsync(_world.HomeClub.Id, [.. lineup, lineup[0]]));
        await Assert.ThrowsAsync<DomainException>(() => Clubs.SaveRosterAsync(_world.HomeClub.Id, [.. lineup, new RosterAssignment(PlayerId.New(), RosterRole.Bench)]));
        await Assert.ThrowsAsync<DomainValidationException>(() => Clubs.SaveRosterAsync(_world.HomeClub.Id, [lineup[0] with { Role = RosterRole.Starter, LineupPosition = null }, .. lineup[1..]]));
    }

    [Fact]
    public async Task Assigning_a_player_moves_him_and_his_squad_spot_to_the_new_club()
    {
        var mario = _world.Player.Id;
        await Clubs.SaveRosterAsync(_world.HomeClub.Id, [new RosterAssignment(mario, RosterRole.Starter, Position.SmallForward)]);

        await Clubs.AssignPlayerAsync(mario, _world.AwayClub.Id, RosterRole.Youth);

        Assert.Empty((await Clubs.GetRosterAsync(_world.HomeClub.Id))!.Players);
        var away = Assert.Single((await Clubs.GetRosterAsync(_world.AwayClub.Id))!.Players);
        Assert.Equal((mario, RosterRole.Youth, false), (away.Player.Id, away.Role, away.HasContract)); // the old contract is still at the home club
        var spells = await _db.ReadAsync(s => s.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>().ListAsync(e => e.PlayerId == mario));
        Assert.Equal(Today, spells.Single(e => e.ClubId == _world.HomeClub.Id).EndDate);
        Assert.True(spells.Single(e => e.ClubId == _world.AwayClub.Id).IsCurrent);
        Assert.Single(await _db.ReadAsync(s => s.Repo<RosterSpot, RosterSpotId>().ListAsync()));

        await Clubs.AssignPlayerAsync(mario, _world.HomeClub.Id);
        Assert.Equal(RosterRole.Bench, Assert.Single((await Clubs.GetRosterAsync(_world.HomeClub.Id))!.Players).Role);
        await Assert.ThrowsAsync<DomainValidationException>(() => Clubs.AssignPlayerAsync(mario, _world.HomeClub.Id));
        await Assert.ThrowsAsync<DomainValidationException>(() => Clubs.AssignPlayerAsync(mario, _world.AwayClub.Id, RosterRole.Starter));
        await Assert.ThrowsAsync<DomainException>(() => Clubs.AssignPlayerAsync(PlayerId.New(), _world.AwayClub.Id));
    }

    [Fact]
    public async Task Club_profile_shows_details_finances_and_current_competitions()
    {
        var c = _world.HomeClub;
        await _db.Services.GetRequiredService<IDatabaseEditor>().SaveClubAsync(new ClubDraft(
            c.Id, c.Name, c.ShortName, c.CountryId, c.HomeArenaId, c.FoundedYear, null, 77, 2_000_000, 1_000_000, BoardExpectation.WinTitle)); // WinTitle is the enum's CLR default

        var profile = (await Clubs.GetProfileAsync(c.Id))!;

        Assert.Equal(("Italy", "Milan", "Mock Arena", "2026-27"), (profile.Country, profile.City, profile.Arena!.Name, profile.Season!.Name));
        Assert.Equal((77, 2_000_000L, 1_000_000L, BoardExpectation.WinTitle), (profile.Club.Reputation, profile.Club.Balance, profile.Club.WageBudget, profile.Club.BoardExpectation));
        Assert.Equal([_world.Championship.Id, _world.Cup.Id], profile.Competitions.Select(x => x.Id));
        Assert.Equal(300_000, profile.WageBill); // player contracts only; the coach's contract is staff
        Assert.Equal(700_000, profile.WageBudgetLeft);
        Assert.Single(profile.Roster.Players);
        Assert.Null(await Clubs.GetProfileAsync(ClubId.New()));

        var away = (await Clubs.GetProfileAsync(_world.AwayClub.Id))!;
        Assert.Equal(((string?)null, (Arena?)null), (away.City, away.Arena));
        Assert.Equal([_world.Championship.Id], away.Competitions.Select(x => x.Id));
    }

    /// <summary>GUIDs as EF Core stores them in SQLite (upper-case text).</summary>
    private static string Sql(Guid id) => id.ToString().ToUpperInvariant();

    [Fact]
    public async Task Roster_spot_rules_are_enforced_by_the_database()
    {
        await _db.ExecuteSqlAsync($"""
            INSERT INTO RosterSpots (Id, PlayerId, ClubId, Role, LineupPosition, RotationOrder)
            VALUES ('77777777-7777-7777-7777-777777777777', '{Sql(_world.Player.Id.Value)}', '{Sql(_world.HomeClub.Id.Value)}', 'Bench', NULL, NULL);
            """);

        await Assert.ThrowsAnyAsync<Exception>(() => _db.ExecuteSqlAsync($"""
            UPDATE RosterSpots SET Role = 'Starter' WHERE PlayerId = '{Sql(_world.Player.Id.Value)}';
            """));
        await Assert.ThrowsAnyAsync<Exception>(() => _db.ExecuteSqlAsync($"""
            INSERT INTO RosterSpots (Id, PlayerId, ClubId, Role) VALUES ('88888888-8888-8888-8888-888888888888', '{Sql(_world.Player.Id.Value)}', '{Sql(_world.AwayClub.Id.Value)}', 'Bench');
            """));
    }
}
