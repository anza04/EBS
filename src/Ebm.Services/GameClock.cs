namespace Ebm.Services;

/// <summary>
/// The game's "today". While a career is loaded it is the career date, moved on by the calendar
/// (<see cref="Careers.ICareerService"/>); otherwise the date of the shared <see cref="TimeProvider"/>.
/// Every game service reads its date from here, never from the system clock.
/// </summary>
public sealed class GameClock
{
    private readonly TimeProvider _time;
    private DateOnly? _careerDate;

    public GameClock(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    public DateOnly Today => _careerDate ?? DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    /// <summary>True while the date comes from a career.</summary>
    public bool IsCareerDate => _careerDate is not null;

    /// <summary>Sets the career date, or <c>null</c> to go back to the real date.</summary>
    internal void Set(DateOnly? careerDate) => _careerDate = careerDate;
}
