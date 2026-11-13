using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;

namespace Ebm.Services.Players;

/// <summary>Read-only player views: the profile screen and the comparison screen. One DI scope per call.</summary>
public interface IPlayerProfileService
{
    /// <summary>The player's profile as of today, or <c>null</c> when the player does not exist.</summary>
    Task<PlayerProfile?> GetProfileAsync(PlayerId playerId, CancellationToken cancellationToken = default);

    /// <summary>Side-by-side comparison of two or more players, in the order given.</summary>
    /// <exception cref="Ebm.Domain.Common.DomainValidationException">Fewer than two distinct players, or one does not exist.</exception>
    Task<PlayerComparison> CompareAsync(IReadOnlyList<PlayerId> playerIds, CancellationToken cancellationToken = default);
}

/// <summary>Everything the profile screen shows about one player.</summary>
/// <param name="CurrentClub">Club of the open club spell, else of the active contract; <c>null</c> for a free agent.</param>
/// <param name="ActiveContract">The contract running today, if any.</param>
/// <param name="SecondNationality">Name of the second citizenship, if any.</param>
/// <param name="MarketValue">Estimated market value in euros (<c>TransferNegotiator.MarketValue</c>); always an estimate.</param>
/// <param name="History">Club spells, most recent first.</param>
public sealed record PlayerProfile(
    Player Player,
    string Nationality,
    int Age,
    string? CurrentClub,
    Contract? ActiveContract,
    string? ContractClub,
    PlayerEvaluation Evaluation,
    string? SecondNationality = null,
    long MarketValue = 0,
    IReadOnlyList<ClubSpell>? History = null);

/// <summary>One club spell of a player; <paramref name="End"/> is <c>null</c> while it is open.</summary>
public sealed record ClubSpell(string Club, DateOnly Start, DateOnly? End);

/// <summary>One line of a comparison. <paramref name="LeaderIndex"/> marks the single best value, when "higher is better" applies.</summary>
public sealed record ComparisonRow(string Group, string Label, IReadOnlyList<int> Values, int? LeaderIndex);

public sealed record PlayerComparison(IReadOnlyList<PlayerProfile> Players, IReadOnlyList<ComparisonRow> Rows);
