using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Players;
using Ebm.Domain.Common;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Services.Editing;
using Ebm.Services.Players;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Players;

public sealed class PlayerProfileTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 11, 9);
    private TestDatabase _db = null!;
    private SampleWorld _world = null!;

    private IPlayerProfileService Profiles => _db.Services.GetRequiredService<IPlayerProfileService>();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(new FixedTimeProvider(Today));
        _world = await SampleWorld.SeedAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task All_player_fields_are_persisted()
    {
        var back = Today.AddDays(20);
        var tendencies = PlayerTendencies.Suggest(_world.Player.Attributes).With(PlayerTendency.PostUp, 3);
        await _db.WriteAsync(s =>
        {
            var player = s.Repo<Player, PlayerId>().FindAsync(_world.Player.Id).GetAwaiter().GetResult()!;
            player.UpdateProfile(214, PositionSet.Of(Position.ShootingGuard, Position.PowerForward), 44, 61, 83);
            player.UpdateCondition(72, 35, InjuryStatus.Injured, back);
            player.UpdateAttributes(player.Attributes.With(PlayerAttribute.DefensivePositioning, 99));
            player.UpdateTendencies(tendencies);
        });

        var stored = await _db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(_world.Player.Id));

        Assert.NotNull(stored);
        Assert.Equal((214, 44, 61, 83), (stored.WingspanCm, stored.Experience, stored.Reputation, stored.Potential));
        Assert.Equal([Position.ShootingGuard, Position.PowerForward], stored.SecondaryPositions.Positions);
        Assert.Equal((72, 35, InjuryStatus.Injured, (DateOnly?)back), (stored.Fitness, stored.Morale, stored.InjuryStatus, stored.InjuryReturnDate));
        Assert.Equal(99, stored.Attributes.DefensivePositioning);
        Assert.Equal(tendencies.ToDictionary(), stored.Tendencies.ToDictionary());
        Assert.Equal(["ShootingGuard,PowerForward|Injured"], await _db.QueryStringsAsync("SELECT SecondaryPositions || '|' || InjuryStatus FROM Players"));
    }

    [Fact]
    public async Task Profile_shows_club_contract_age_and_evaluation()
    {
        var profile = await Profiles.GetProfileAsync(_world.Player.Id);

        Assert.NotNull(profile);
        Assert.Equal("Italy", profile.Nationality);
        Assert.Equal(26, profile.Age);
        Assert.Equal(_world.HomeClub.Name, profile.CurrentClub);
        Assert.Equal(_world.HomeClub.Name, profile.ContractClub);
        Assert.Equal(300_000, profile.ActiveContract!.AnnualSalary);
        Assert.Equal(PlayerRatingCalculator.Default.Evaluate(profile.Player).Overall, profile.Evaluation.Overall);
        Assert.Equal(5, profile.Evaluation.Positions.Count);
        Assert.Null(await Profiles.GetProfileAsync(PlayerId.New()));
    }

    [Fact]
    public async Task Player_without_club_or_running_contract_is_a_free_agent()
    {
        await using var later = await TestDatabase.CreateAsync(new FixedTimeProvider(SampleWorld.SeasonStart.AddYears(3)));
        var world = await SampleWorld.SeedAsync(later);
        await later.ExecuteSqlAsync("UPDATE PlayerClubHistory SET EndDate = '2027-06-30'");

        var profile = await later.Services.GetRequiredService<IPlayerProfileService>().GetProfileAsync(world.Player.Id);

        Assert.Null(profile!.CurrentClub);
        Assert.Null(profile.ActiveContract);
    }

    [Fact]
    public async Task Comparison_lists_every_value_and_marks_the_single_leader()
    {
        var rival = await AddRivalAsync();

        var comparison = await Profiles.CompareAsync([_world.Player.Id, rival]);

        Assert.Equal(6 + 2 + 7 + 5 + 27 + 8 + 2, comparison.Rows.Count);
        var threePoint = comparison.Rows.Single(r => r.Label == "Three point");
        Assert.Equal([72, 95], threePoint.Values);
        Assert.Equal(1, threePoint.LeaderIndex);
        var closeShot = comparison.Rows.Single(r => r.Label == "Close shot");
        Assert.Null(closeShot.LeaderIndex); // 65 vs 65
        Assert.All(comparison.Rows.Where(r => r.Group is "Tendencies" or "Profile" && r.Label is not ("Experience" or "Reputation")), r => Assert.Null(r.LeaderIndex));
        await Assert.ThrowsAsync<DomainValidationException>(() => Profiles.CompareAsync([rival, rival]));
        await Assert.ThrowsAsync<DomainValidationException>(() => Profiles.CompareAsync([rival, PlayerId.New()]));
    }

    [Fact]
    public async Task Profile_and_comparison_screens_follow_the_selection()
    {
        var rival = await AddRivalAsync();
        var lookups = new EditorLookups();
        lookups.Refresh(await _db.Services.GetRequiredService<IDatabaseEditor>().LoadAsync());
        var profile = new PlayerProfileViewModel(Profiles, lookups);
        var comparison = new PlayerComparisonViewModel(Profiles, lookups);

        profile.PlayerId = _world.Player.Id;
        await profile.RefreshAsync();
        comparison.PlayerA = _world.Player.Id;
        await comparison.RefreshAsync();
        Assert.Equal("Choose two players.", comparison.Message);
        comparison.PlayerB = rival;
        await comparison.RefreshAsync();

        Assert.Equal("Mario Mock · Small forward", profile.Title);
        Assert.Contains(profile.Details, l => l.Label == "Current club" && l.Value == _world.HomeClub.Name);
        Assert.Equal(5, profile.Positions.Count);
        Assert.Equal(8, profile.Groups.Count); // 7 attribute categories + tendencies
        Assert.Null(comparison.Message);
        Assert.Equal("Rival Shooter", comparison.Rows.Single(r => r.Label == "Three point").Better);
    }

    private async Task<PlayerId> AddRivalAsync()
    {
        var rival = new Player(PlayerId.New(), "Rival", "Shooter", new DateOnly(1998, 1, 1), _world.Italy.Id, 193, 88, Position.ShootingGuard,
            PlayerAttributes.Uniform(65).With(PlayerAttribute.ThreePoint, 95));
        await _db.WriteAsync(s => s.Repo<Player, PlayerId>().Add(rival));
        return rival.Id;
    }
}
