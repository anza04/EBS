using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Transfers;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Players;

/// <summary>Builds player profiles and comparisons from the repositories and the <see cref="PlayerRatingCalculator"/>.</summary>
internal sealed class PlayerProfileService : IPlayerProfileService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PlayerRatingCalculator _calculator;
    private readonly GameClock _time;
    private readonly TransferNegotiator _market;

    public PlayerProfileService(IServiceScopeFactory scopes, PlayerRatingCalculator calculator, GameClock time, TransferNegotiator market)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(market);
        _scopes = scopes;
        _calculator = calculator;
        _time = time;
        _market = market;
    }

    public async Task<PlayerProfile?> GetProfileAsync(PlayerId playerId, CancellationToken cancellationToken = default) =>
        (await LoadAsync([playerId], cancellationToken).ConfigureAwait(false))[0];

    public async Task<PlayerComparison> CompareAsync(IReadOnlyList<PlayerId> playerIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerIds);
        if (playerIds.Distinct().Count() != playerIds.Count || playerIds.Count < 2)
        {
            throw new DomainValidationException("Choose at least two different players to compare.");
        }

        var loaded = await LoadAsync(playerIds, cancellationToken).ConfigureAwait(false);
        if (loaded.Any(p => p is null))
        {
            throw new DomainValidationException("A player to compare no longer exists.");
        }

        var profiles = loaded.Select(p => p!).ToList();
        return new PlayerComparison(profiles, BuildRows(profiles));
    }

    private static List<ComparisonRow> BuildRows(IReadOnlyList<PlayerProfile> players)
    {
        var rows = new List<ComparisonRow>();
        void Add(string group, string label, Func<PlayerProfile, int> value, bool higherIsBetter = true)
        {
            var values = players.Select(value).ToList();
            var max = values.Max();
            int? leader = higherIsBetter && values.Count(v => v == max) == 1 ? values.IndexOf(max) : null;
            rows.Add(new ComparisonRow(group, label, values, leader));
        }

        Add("Profile", "Age", p => p.Age, higherIsBetter: false);
        Add("Profile", "Height (cm)", p => p.Player.HeightCm, higherIsBetter: false);
        Add("Profile", "Wingspan (cm)", p => p.Player.WingspanCm, higherIsBetter: false);
        Add("Profile", "Weight (kg)", p => p.Player.WeightKg, higherIsBetter: false);
        Add("Profile", "Experience", p => p.Player.Experience);
        Add("Profile", "Reputation", p => p.Player.Reputation);
        Add("Ratings", "Overall", p => p.Evaluation.Overall);
        Add("Ratings", "Potential", p => p.Evaluation.PotentialRating);
        foreach (var category in PlayerCatalog.Categories)
        {
            Add("Categories", PlayerCatalog.DisplayName(category), p => p.Evaluation.Categories[category]);
        }

        foreach (var position in Enum.GetValues<Position>())
        {
            Add("Positions", PlayerCatalog.DisplayName(position), p => p.Evaluation.At(position).EffectiveRating);
        }

        foreach (var attribute in PlayerCatalog.Attributes)
        {
            Add(PlayerCatalog.DisplayName(PlayerCatalog.CategoryOf(attribute)), PlayerCatalog.DisplayName(attribute), p => p.Player.Attributes[attribute]);
        }

        // Tendencies describe style, not quality: no leader.
        foreach (var tendency in PlayerCatalog.Tendencies)
        {
            Add("Tendencies", PlayerCatalog.DisplayName(tendency), p => p.Player.Tendencies[tendency], higherIsBetter: false);
        }

        Add("Condition", "Fitness", p => p.Player.Fitness);
        Add("Condition", "Morale", p => p.Player.Morale);
        return rows;
    }

    private async Task<PlayerProfile?[]> LoadAsync(IReadOnlyList<PlayerId> ids, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        T Repo<T>() where T : notnull => services.GetRequiredService<T>();
        var today = _time.Today;
        var players = Repo<IRepository<Player, PlayerId>>();
        var spells = Repo<IRepository<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>>();
        var contracts = Repo<IRepository<Contract, ContractId>>();
        var clubs = Repo<IRepository<Club, ClubId>>();
        var countries = Repo<IRepository<Country, CountryId>>();

        var result = new PlayerProfile?[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            if (await players.FindAsync(id, cancellationToken).ConfigureAwait(false) is not { } player)
            {
                continue;
            }

            var playerSpells = await spells.ListAsync(s => s.PlayerId == id, cancellationToken).ConfigureAwait(false);
            var spell = playerSpells.FirstOrDefault(s => s.IsCurrent);
            var contract = (await contracts.ListAsync(c => c.PlayerId == id, cancellationToken).ConfigureAwait(false))
                .Where(c => c.IsRunningOn(today))
                .OrderByDescending(c => c.StartDate)
                .FirstOrDefault();
            async Task<string?> ClubName(ClubId? clubId) =>
                clubId is { } cid ? (await clubs.FindAsync(cid, cancellationToken).ConfigureAwait(false))?.Name : null;
            var contractClub = await ClubName(contract?.ClubId).ConfigureAwait(false);
            var country = await countries.FindAsync(player.NationalityId, cancellationToken).ConfigureAwait(false);
            var second = player.SecondNationalityId is { } sid ? await countries.FindAsync(sid, cancellationToken).ConfigureAwait(false) : null;
            var evaluation = _calculator.Evaluate(player);
            var yearsLeft = contract is null ? 0 : Math.Max(0, (contract.EndDate.DayNumber - today.DayNumber) / 365.25);
            var value = _market.MarketValue(new ValuationContext(evaluation.Overall, player.Potential, player.Reputation, player.AgeOn(today), yearsLeft));
            var history = new List<ClubSpell>();
            foreach (var s in playerSpells.OrderByDescending(s => s.StartDate))
            {
                history.Add(new ClubSpell(await ClubName(s.ClubId).ConfigureAwait(false) ?? string.Empty, s.StartDate, s.EndDate));
            }

            result[i] = new PlayerProfile(
                player,
                country?.Name ?? string.Empty,
                player.AgeOn(today),
                await ClubName(spell?.ClubId).ConfigureAwait(false) ?? contractClub,
                contract,
                contractClub,
                evaluation,
                second?.Name,
                value,
                history);
        }

        return result;
    }
}
