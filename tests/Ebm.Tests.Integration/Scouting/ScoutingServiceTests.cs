using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Scouting;
using Ebm.Domain.Staff;
using Ebm.Services.Scouting;
using Ebm.Services.Transfers;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Scouting;

/// <summary>Step 20: scouts, assignments, reports and AI clubs using scouting information.</summary>
public sealed class ScoutingServiceTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private readonly TestClock _clock = new(Today);
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private IScoutingService Scouting => _db.Services.GetRequiredService<IScoutingService>();

    private ClubId Home => _world.HomeClub.Id;

    private ClubId Away => _world.AwayClub.Id;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(_clock);
        _world = await SampleWorld.SeedAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Clubs_hire_the_scouts_their_reputation_allows_once()
    {
        await SetReputationAsync(Home, 90);
        var hired = await Scouting.HireMissingScoutsAsync();

        var scouts = await Scouting.GetScoutsAsync(Home);
        Assert.Equal(ScoutingRules.ScoutsFor(90), scouts.Count);
        Assert.Equal(hired, scouts.Count + (await Scouting.GetScoutsAsync(Away)).Count);
        Assert.All(scouts, s => Assert.Equal((StaffRole.Scout, true, Today), (s.Member.Role, s.IsAvailable, s.Contract.StartDate)));
        Assert.Equal(0, await Scouting.HireMissingScoutsAsync());
    }

    [Fact]
    public async Task A_player_report_becomes_available_when_the_assignment_is_processed()
    {
        var target = await ContractedAsync(Away, 70);
        var scout = await ScoutAsync(Home, 60);
        var line = await Scouting.AssignAsync(Home, new AssignmentRequest(scout, ScoutingFocus.Player, target.Id));
        Assert.Equal((Today.AddDays(7), "Player: " + target.FullName), (line.DueDate, line.Target));
        Assert.False((await Scouting.GetScoutsAsync(Home)).Single(s => s.Member.Id == scout).IsAvailable);

        _clock.Today = Today.AddDays(6);
        Assert.Equal(0, (await Scouting.ProcessDayAsync()).ReportsWritten);
        Assert.Empty(await Scouting.GetPlayerReportsAsync(Home, target.Id));

        _clock.Today = Today.AddDays(7);
        var day = await Scouting.ProcessDayAsync();
        Assert.Equal((1, 1), (day.AssignmentsCompleted, day.ReportsWritten));
        var report = Assert.Single(await Scouting.GetPlayerReportsAsync(Home, target.Id));
        Assert.Equal((Home, _world.AwayClub.Name), (report.Report.ClubId, report.Club));
        Assert.Equal(Today.AddDays(7), report.Report.Date);
        var truth = PlayerRatingCalculator.Default.Evaluate(target);
        Assert.InRange(truth.Overall, report.Report.OverallRange.Low, report.Report.OverallRange.High);
        Assert.True(report.Report.EstimatedValue > 0 && report.Report.EstimatedSalary > 0);
        Assert.Equal(AssignmentStatus.Completed, (await Scouting.GetAssignmentsAsync(Home)).Single().Status);
        Assert.True((await Scouting.GetScoutsAsync(Home)).Single(s => s.Member.Id == scout).IsAvailable);
        Assert.Equal(0, (await Scouting.ProcessDayAsync()).ReportsWritten); // idempotent
    }

    [Fact]
    public async Task Better_scouts_write_more_accurate_reports()
    {
        var good = await ScoutAsync(Home, 95);
        var poor = await ScoutAsync(Away, 5);
        var other = await FreeAgentAsync(60);
        await Scouting.AssignAsync(Home, new AssignmentRequest(good, ScoutingFocus.Player, other.Id));
        await Scouting.AssignAsync(Away, new AssignmentRequest(poor, ScoutingFocus.Player, other.Id));
        _clock.Today = Today.AddDays(7);
        await Scouting.ProcessDayAsync();

        var accurate = Assert.Single(await Scouting.GetPlayerReportsAsync(Home, other.Id)).Report;
        var rough = Assert.Single(await Scouting.GetPlayerReportsAsync(Away, other.Id)).Report;
        Assert.True(accurate.Accuracy > rough.Accuracy);
        Assert.True(accurate.Margin < rough.Margin);
        Assert.Contains(PlayerCatalog.Attributes, a => rough.Attributes[a] != other.Attributes[a]); // estimates differ from the truth
    }

    [Fact]
    public async Task League_assignments_report_on_the_most_visible_players_of_the_entered_clubs()
    {
        var awayPlayers = new List<Player>();
        for (var i = 0; i < 3; i++)
        {
            awayPlayers.Add(await ContractedAsync(Away, 60 + i));
        }

        var outsider = await FreeAgentAsync(80);
        var scout = await ScoutAsync(Home, 50);
        var line = await Scouting.AssignAsync(Home, new AssignmentRequest(scout, ScoutingFocus.League, CompetitionId: _world.Championship.Id));
        Assert.Equal(Today.AddDays(21), line.DueDate);
        _clock.Today = line.DueDate;
        Assert.Equal(3, (await Scouting.ProcessDayAsync()).ReportsWritten);

        var found = await Scouting.SearchReportsAsync(Home, ReportSearch.Everyone);
        Assert.Equal(awayPlayers.Select(p => p.Id.Value).Order(), found.Select(r => r.Player.Id.Value).Order()); // not his own player, not the free agent
        Assert.DoesNotContain(found, r => r.Player.Id == outsider.Id || r.Player.Id == _world.Player.Id);
        Assert.Equal(3, (await Scouting.GetAssignmentsAsync(Home)).Single().Reports);
        Assert.Empty(await Scouting.SearchReportsAsync(Home, new ReportSearch(Position: Position.Center)));
    }

    [Fact]
    public async Task Assignments_are_refused_for_busy_or_foreign_scouts_and_own_players()
    {
        var target = await FreeAgentAsync(60);
        var scout = await ScoutAsync(Home, 50);
        var awayScout = await ScoutAsync(Away, 50);
        await Assert.ThrowsAsync<DomainValidationException>(() => Scouting.AssignAsync(Home, new AssignmentRequest(awayScout, ScoutingFocus.Player, target.Id)));
        await Assert.ThrowsAsync<DomainValidationException>(() => Scouting.AssignAsync(Home, new AssignmentRequest(scout, ScoutingFocus.Player, _world.Player.Id)));
        await Assert.ThrowsAsync<DomainValidationException>(() => Scouting.AssignAsync(Home, new AssignmentRequest(scout, ScoutingFocus.Region)));

        var line = await Scouting.AssignAsync(Home, new AssignmentRequest(scout, ScoutingFocus.Youth, CountryId: _world.Italy.Id));
        Assert.Equal("Youth: Italy (age ≤ 21)", line.Target);
        var busy = await Assert.ThrowsAsync<DomainValidationException>(() => Scouting.AssignAsync(Home, new AssignmentRequest(scout, ScoutingFocus.Player, target.Id)));
        Assert.Contains("busy", busy.Message, StringComparison.Ordinal);

        await Scouting.CancelAsync(line.Assignment.Id);
        _clock.Today = Today.AddDays(30);
        Assert.Equal(0, (await Scouting.ProcessDayAsync()).ReportsWritten);
        Assert.Equal(AssignmentStatus.Cancelled, (await Scouting.GetAssignmentsAsync(Home)).Single().Status);
        await Scouting.AssignAsync(Home, new AssignmentRequest(scout, ScoutingFocus.Player, target.Id));
    }

    [Fact]
    public async Task AI_clubs_recruit_on_what_their_scouts_report()
    {
        // Three small forwards: a weak one the club's scouts overrate, and two better ones it knows only from background scouting.
        var overrated = await FreeAgentAsync(40);
        var better = await FreeAgentAsync(80);
        var decent = await FreeAgentAsync(50);
        await ScoutAsync(Away, 100);
        await _db.WriteAsync(s =>
        {
            var club = s.Repo<Club, ClubId>().FindAsync(Away).GetAwaiter().GetResult()!;
            club.UpdateProfile(club.City, 60, 50_000_000, 50_000_000, club.BoardExpectation);
            s.Repo<ScoutReport, ScoutReportId>().Add(new ScoutReport(
                ScoutReportId.New(), Away, overrated.Id, null, null, Today, new ScoutEstimate(30, 18, 27, PlayerAttributes.Uniform(90), 90, 92), 1_000_000, 300_000));
        });

        var window = await _db.Services.GetRequiredService<ITransferService>().RunAiTransferWindowAsync([Home]);
        var actions = window.Clubs.Single(c => c.Club.Id == Away).Actions;
        bool SignedFor(Player player) => actions.Any(a => a.StartsWith($"Signed {player.FullName}", StringComparison.Ordinal) && a.Contains("Small forward", StringComparison.Ordinal));
        var log = $"overrated={overrated.FullName} better={better.FullName} decent={decent.FullName} :: {string.Join(" | ", actions)}";
        Assert.True(SignedFor(overrated) && SignedFor(better) && !SignedFor(decent), log);
    }

    private async Task<StaffMemberId> ScoutAsync(ClubId clubId, int ability)
    {
        var scout = new StaffMember(StaffMemberId.New(), "Scout", $"S{Guid.NewGuid():N}"[..12], new DateOnly(1975, 1, 1), _world.Italy.Id, StaffRole.Scout, ability);
        await _db.WriteAsync(s =>
        {
            s.Repo<StaffMember, StaffMemberId>().Add(scout);
            s.Repo<Contract, ContractId>().Add(Contract.ForStaff(ContractId.New(), clubId, scout.Id, SampleWorld.SeasonStart, SampleWorld.SeasonStart.AddYears(2), 50_000));
        });
        return scout.Id;
    }

    private async Task SetReputationAsync(ClubId clubId, int reputation) =>
        await _db.WriteAsync(s =>
        {
            var club = s.Repo<Club, ClubId>().FindAsync(clubId).GetAwaiter().GetResult()!;
            club.UpdateProfile(club.City, reputation, club.Balance, club.WageBudget, club.BoardExpectation);
        });

    private async Task<Player> FreeAgentAsync(int quality)
    {
        var player = new Player(PlayerId.New(), "Free", $"Agent{Guid.NewGuid():N}"[..14], new DateOnly(1999, 2, 1), _world.Italy.Id, 200, 95, Position.SmallForward, PlayerAttributes.Uniform(quality));
        await _db.WriteAsync(s => s.Repo<Player, PlayerId>().Add(player));
        return player;
    }

    private async Task<Player> ContractedAsync(ClubId clubId, int quality)
    {
        var player = await FreeAgentAsync(quality);
        await _db.WriteAsync(s => s.Repo<Contract, ContractId>().Add(Contract.ForPlayer(ContractId.New(), clubId, player.Id, SampleWorld.SeasonStart, SampleWorld.SeasonStart.AddYears(2), 80_000)));
        return player;
    }
}
