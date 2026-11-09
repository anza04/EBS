using System.Globalization;
using Ebm.Domain.Common;

namespace Ebm.Domain.Contracts;

/// <summary>Achievements a performance bonus can be tied to. Bonuses are paid by the season review (Steps 12 and 27).</summary>
public enum BonusCondition
{
    /// <summary>Paid for every game the player appears in.</summary>
    PerGamePlayed,
    ReachPlayoffs,
    LeagueTitle,
    CupTitle,
    Promotion,
    AvoidRelegation,
}

/// <summary>
/// The performance bonuses of a contract: at most one amount (whole euros) per <see cref="BonusCondition"/>.
/// Immutable, compared by value. Stored as <c>LeagueTitle:50000;ReachPlayoffs:20000</c>.
/// </summary>
public sealed class PerformanceBonuses : IEquatable<PerformanceBonuses>
{
    /// <summary>Upper bound for a single bonus, in euros.</summary>
    public const long MaxAmount = Contract.MaxAnnualSalary;

    private readonly SortedDictionary<BonusCondition, long> _amounts;

    private PerformanceBonuses(SortedDictionary<BonusCondition, long> amounts) => _amounts = amounts;

    public static PerformanceBonuses None { get; } = new([]);

    public IReadOnlyDictionary<BonusCondition, long> Amounts => _amounts;

    public bool IsEmpty => _amounts.Count == 0;

    /// <summary>Sum of the season bonuses (every bonus except <see cref="BonusCondition.PerGamePlayed"/>).</summary>
    public long SeasonTotal => _amounts.Where(a => a.Key != BonusCondition.PerGamePlayed).Sum(a => a.Value);

    public long this[BonusCondition condition] => _amounts.GetValueOrDefault(condition);

    public static PerformanceBonuses Of(IEnumerable<KeyValuePair<BonusCondition, long>> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        var result = new SortedDictionary<BonusCondition, long>();
        foreach (var (condition, amount) in amounts)
        {
            Guard.Defined(condition, "Bonus condition");
            if (amount is <= 0 or > MaxAmount)
            {
                throw new DomainValidationException($"The {PlayerCatalogName(condition)} bonus must be between 1 and {MaxAmount:N0} euros.");
            }

            if (!result.TryAdd(condition, amount))
            {
                throw new DomainValidationException($"The {PlayerCatalogName(condition)} bonus is listed twice.");
            }
        }

        return result.Count == 0 ? None : new PerformanceBonuses(result);
    }

    public static PerformanceBonuses Of(params (BonusCondition Condition, long Amount)[] amounts) =>
        Of(amounts.Select(a => KeyValuePair.Create(a.Condition, a.Amount)));

    /// <summary>
    /// Parses <c>Condition:amount</c> pairs separated by <c>;</c>, <c>,</c> or <c>|</c>. Condition names are matched
    /// case-insensitively; amounts are whole euros (no separators).
    /// </summary>
    public static PerformanceBonuses Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        var pairs = new List<KeyValuePair<BonusCondition, long>>();
        foreach (var part in text.Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2
                || pieces[0].All(char.IsAsciiDigit)
                || !Enum.TryParse<BonusCondition>(pieces[0], ignoreCase: true, out var condition)
                || !Enum.IsDefined(condition))
            {
                throw new DomainValidationException(
                    $"'{part}' is not a bonus. Write Condition:amount, where Condition is one of {string.Join(", ", Enum.GetNames<BonusCondition>())}.");
            }

            if (!long.TryParse(pieces[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            {
                throw new DomainValidationException($"The amount of the {pieces[0]} bonus must be a whole number of euros (got '{pieces[1]}').");
            }

            pairs.Add(KeyValuePair.Create(condition, amount));
        }

        return Of(pairs);
    }

    public override string ToString() =>
        string.Join(';', _amounts.Select(a => $"{a.Key}:{a.Value.ToString(CultureInfo.InvariantCulture)}"));

    public bool Equals(PerformanceBonuses? other) => other is not null && ToString() == other.ToString();

    public override bool Equals(object? obj) => Equals(obj as PerformanceBonuses);

    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);

    private static string PlayerCatalogName(BonusCondition condition) => Players.PlayerCatalog.DisplayName(condition).ToLowerInvariant();
}
