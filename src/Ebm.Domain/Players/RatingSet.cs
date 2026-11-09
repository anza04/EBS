using System.Globalization;
using Ebm.Domain.Common;

namespace Ebm.Domain.Players;

/// <summary>
/// A fixed set of ratings from <see cref="MinRating"/> to <see cref="MaxRating"/>, one per value of
/// <typeparamref name="TKey"/>. Subclasses expose one property per rating so each is its own database column.
/// </summary>
public abstract class RatingSet<TKey>
    where TKey : struct, Enum
{
    public const int MinRating = 1;
    public const int MaxRating = 100;

    private static readonly TKey[] Keys = Enum.GetValues<TKey>();

    private readonly int[] _values = new int[Keys.Length];

    protected RatingSet()
    {
    }

    protected RatingSet(IReadOnlyDictionary<TKey, int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var missing = Keys.Where(k => !values.ContainsKey(k)).Select(PlayerCatalog.DisplayName).ToList();
        if (missing.Count > 0)
        {
            throw new DomainValidationException($"Missing ratings: {string.Join(", ", missing)}.");
        }

        foreach (var (key, value) in values)
        {
            _values[Index(key)] = Guard.Range(value, MinRating, MaxRating, PlayerCatalog.DisplayName(key));
        }
    }

    public int this[TKey key] => _values[Index(key)];

    /// <summary>The ratings in catalogue order.</summary>
    public IReadOnlyDictionary<TKey, int> ToDictionary() => Keys.ToDictionary(k => k, k => _values[Index(k)]);

    protected int Get(TKey key) => _values[Index(key)];

    // Only EF Core materialisation writes through here; stored values are protected by a check constraint.
    protected void Set(TKey key, int value) => _values[Index(key)] = value;

    private static int Index(TKey key)
    {
        var index = Convert.ToInt32(key, CultureInfo.InvariantCulture);
        return (uint)index < (uint)Keys.Length ? index : throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown rating.");
    }
}
