using Ebm.Domain.Common;

namespace Ebm.Domain.Matches;

/// <summary>Court zones of a field-goal attempt (docs/MATCH_ENGINE.md §9.1). Left/right as drawn on the chart.</summary>
public enum ShotZone
{
    RestrictedArea,
    Paint,
    MidRangeLeft,
    MidRangeCenter,
    MidRangeRight,
    CornerThreeLeft,
    WingThreeLeft,
    TopThree,
    WingThreeRight,
    CornerThreeRight,
}

/// <summary>How closely a shot was defended (Step 29).</summary>
public enum ShotContest
{
    Open,
    Contested,
    Tight,
}

/// <summary>
/// One field-goal attempt counted in the box score (fouled misses are not attempts). Coordinates are decimetres on
/// the attacked half court: <see cref="X"/> −75…75 across, <see cref="Y"/> 0…140 from the baseline; the rim is at
/// (0, 15.75). Zone and value are derived from the coordinates, so a chart always matches the recorded points.
/// </summary>
public readonly record struct ShotRecord(
    int Period,
    int Clock,
    int X,
    int Y,
    bool Made,
    ShotContest Contest,
    bool Assisted,
    bool FastBreak,
    bool Clutch,
    bool Blocked)
{
    public ShotZone Zone => ShotZones.Classify(X, Y);

    public bool IsThree => ShotZones.IsThree(X, Y);

    public int Points => IsThree ? 3 : 2;

    /// <summary>Distance from the rim in metres.</summary>
    public double Distance => ShotZones.Distance(X, Y);
}

/// <summary>FIBA half-court geometry in decimetres.</summary>
public static class ShotZones
{
    public const int HalfWidth = 75;
    public const int Length = 140;
    public const double RimY = 15.75;

    /// <summary>Three-point arc radius (6.75 m) and the straight corner lines (0.9 m from the sidelines).</summary>
    public const double ArcRadius = 67.5;
    public const int CornerX = 66;
    public const double CornerTopY = 29.9;
    public const double RestrictedRadius = 12.5;
    public const int PaintHalfWidth = 24;
    public const int PaintLength = 58;

    public static double Distance(int x, int y) => Math.Sqrt((x * x) + ((y - RimY) * (y - RimY))) / 10.0;

    public static bool IsThree(int x, int y) =>
        y <= CornerTopY ? Math.Abs(x) >= CornerX : Distance(x, y) * 10 >= ArcRadius;

    public static bool IsThree(ShotZone zone) => zone >= ShotZone.CornerThreeLeft;

    public static bool IsInside(ShotZone zone) => zone is ShotZone.RestrictedArea or ShotZone.Paint;

    public static ShotZone Classify(int x, int y)
    {
        // Angle from the rim: 0° straight out to the top of the key.
        var angle = Math.Atan2(x, Math.Max(0.01, y - RimY)) * 180 / Math.PI;
        if (IsThree(x, y))
        {
            return y <= CornerTopY ? (x < 0 ? ShotZone.CornerThreeLeft : ShotZone.CornerThreeRight)
                : Math.Abs(angle) <= 22.5 ? ShotZone.TopThree
                : x < 0 ? ShotZone.WingThreeLeft : ShotZone.WingThreeRight;
        }

        if (Distance(x, y) * 10 <= RestrictedRadius)
        {
            return ShotZone.RestrictedArea;
        }

        if (Math.Abs(x) <= PaintHalfWidth && y <= PaintLength)
        {
            return ShotZone.Paint;
        }

        return y > RimY && Math.Abs(angle) <= 30 ? ShotZone.MidRangeCenter : x < 0 ? ShotZone.MidRangeLeft : ShotZone.MidRangeRight;
    }

    public static bool IsOnCourt(int x, int y) => x is >= -HalfWidth and <= HalfWidth && y is >= 0 and <= Length;

    public static string Label(ShotZone zone) => zone switch
    {
        ShotZone.RestrictedArea => "Restricted area",
        ShotZone.Paint => "Paint",
        ShotZone.MidRangeLeft => "Mid-range left",
        ShotZone.MidRangeCenter => "Mid-range centre",
        ShotZone.MidRangeRight => "Mid-range right",
        ShotZone.CornerThreeLeft => "Left corner three",
        ShotZone.WingThreeLeft => "Left wing three",
        ShotZone.TopThree => "Top of the key three",
        ShotZone.WingThreeRight => "Right wing three",
        _ => "Right corner three",
    };
}

/// <summary>Made/attempted field goals of one zone.</summary>
public sealed record ShotZoneLine(ShotZone Zone, int Made, int Attempted)
{
    public double Percentage => Attempted == 0 ? 0 : (double)Made / Attempted;

    public int Points => Made * (ShotZones.IsThree(Zone) ? 3 : 2);
}

/// <summary>Compact storage of shot records (base64, 6 bytes a shot, version byte first) and zone summaries.</summary>
public static class ShotChart
{
    private const byte Version = 1;
    private const int Size = 6;

    public static string Encode(IEnumerable<ShotRecord> shots)
    {
        ArgumentNullException.ThrowIfNull(shots);
        var list = shots.ToList();
        var bytes = new byte[1 + (list.Count * Size)];
        bytes[0] = Version;
        for (var i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (!ShotZones.IsOnCourt(s.X, s.Y) || s.Period is < 1 or > 255 || s.Clock is < 0 or > ushort.MaxValue)
            {
                throw new DomainValidationException("A shot lies outside the half court or the match clock.");
            }

            var o = 1 + (i * Size);
            bytes[o] = (byte)s.Period;
            bytes[o + 1] = (byte)(s.Clock & 0xFF);
            bytes[o + 2] = (byte)(s.Clock >> 8);
            bytes[o + 3] = (byte)(s.X + ShotZones.HalfWidth);
            bytes[o + 4] = (byte)s.Y;
            bytes[o + 5] = (byte)((s.Made ? 1 : 0) | (s.Assisted ? 2 : 0) | (s.FastBreak ? 4 : 0) | (s.Clutch ? 8 : 0)
                | (s.Blocked ? 16 : 0) | ((int)s.Contest << 5));
        }

        return Convert.ToBase64String(bytes);
    }

    /// <summary>Decodes stored shots; <c>null</c> (box scores before Step 29) gives no shots.</summary>
    public static IReadOnlyList<ShotRecord> Decode(string? data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return [];
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            throw new DomainValidationException("The shot chart data is corrupt.");
        }

        if (bytes.Length == 0 || bytes[0] != Version || (bytes.Length - 1) % Size != 0)
        {
            throw new DomainValidationException("The shot chart data is corrupt.");
        }

        var shots = new ShotRecord[(bytes.Length - 1) / Size];
        for (var i = 0; i < shots.Length; i++)
        {
            var o = 1 + (i * Size);
            var flags = bytes[o + 5];
            var contest = (flags >> 5) & 3;
            if (contest > (int)ShotContest.Tight || bytes[o + 3] > 2 * ShotZones.HalfWidth || bytes[o + 4] > ShotZones.Length)
            {
                throw new DomainValidationException("The shot chart data is corrupt.");
            }

            shots[i] = new ShotRecord(
                bytes[o],
                bytes[o + 1] | (bytes[o + 2] << 8),
                bytes[o + 3] - ShotZones.HalfWidth,
                bytes[o + 4],
                (flags & 1) != 0,
                (ShotContest)contest,
                (flags & 2) != 0,
                (flags & 4) != 0,
                (flags & 8) != 0,
                (flags & 16) != 0);
        }

        return shots;
    }

    /// <summary>Made and attempted shots per zone, every zone listed in order.</summary>
    public static IReadOnlyList<ShotZoneLine> Summarize(IEnumerable<ShotRecord> shots)
    {
        ArgumentNullException.ThrowIfNull(shots);
        var byZone = shots.GroupBy(s => s.Zone).ToDictionary(g => g.Key, g => (Made: g.Count(s => s.Made), Attempted: g.Count()));
        return [.. Enum.GetValues<ShotZone>().Select(z => byZone.TryGetValue(z, out var v) ? new ShotZoneLine(z, v.Made, v.Attempted) : new ShotZoneLine(z, 0, 0))];
    }
}
