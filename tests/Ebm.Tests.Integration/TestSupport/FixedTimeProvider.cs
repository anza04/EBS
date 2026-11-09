namespace Ebm.Tests.Integration.TestSupport;

/// <summary>A clock stopped at one UTC instant.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public FixedTimeProvider(DateOnly today)
        : this(new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero))
    {
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => now;
}
