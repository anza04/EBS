using Ebm.Domain.Common;

namespace Ebm.Domain.Players;

/// <summary>A playing position. The numeric order (guards to center) is used for display and tie-breaks.</summary>
public enum Position
{
    PointGuard,
    ShootingGuard,
    SmallForward,
    PowerForward,
    Center,
}

/// <summary>A set of positions, used for a player's secondary positions. Stored as text: <c>ShootingGuard,SmallForward</c>.</summary>
public readonly record struct PositionSet
{
    private readonly int _bits;

    private PositionSet(int bits) => _bits = bits;

    public static PositionSet Empty => default;

    public bool IsEmpty => _bits == 0;

    /// <summary>The positions in position order.</summary>
    public IReadOnlyList<Position> Positions => [.. Enum.GetValues<Position>().Where(Contains)];

    public static PositionSet Of(IEnumerable<Position> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var bits = 0;
        foreach (var position in positions)
        {
            bits |= 1 << (int)Guard.Defined(position, "Position");
        }

        return new PositionSet(bits);
    }

    public static PositionSet Of(params Position[] positions) => Of((IEnumerable<Position>)positions);

    /// <summary>Parses the stored form; names are matched case-insensitively.</summary>
    public static PositionSet Parse(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Empty
            : Of(text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => Enum.TryParse<Position>(p, ignoreCase: true, out var value) && Enum.IsDefined(value)
                    ? value
                    : throw new DomainValidationException($"'{p}' is not a position.")));

    public bool Contains(Position position) => (_bits & (1 << (int)position)) != 0;

    public PositionSet Without(Position position) => new(_bits & ~(1 << (int)position));

    public override string ToString() => string.Join(',', Positions);
}
