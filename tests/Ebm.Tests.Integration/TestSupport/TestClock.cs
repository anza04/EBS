namespace Ebm.Tests.Integration.TestSupport;

/// <summary>A clock tests can move forward, to let contracts start and expire. Noon UTC of <see cref="Today"/>.</summary>
public sealed class TestClock(DateOnly today) : TimeProvider
{
    public DateOnly Today { get; set; } = today;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
}
