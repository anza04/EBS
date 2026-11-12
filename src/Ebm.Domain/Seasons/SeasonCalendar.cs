namespace Ebm.Domain.Seasons;

/// <summary>Season naming and the dates of the following season.</summary>
public static class SeasonCalendar
{
    /// <summary>"2026-27" for a season over two years, "2026" for one inside a calendar year.</summary>
    public static string NameFor(DateOnly start, DateOnly end) =>
        start.Year == end.Year ? $"{start.Year}" : $"{start.Year}-{end.Year % 100:00}";

    /// <summary>The season after <paramref name="season"/>: the same dates one year later.</summary>
    public static Season Next(Season season, SeasonId id)
    {
        ArgumentNullException.ThrowIfNull(season);
        var start = season.StartDate.AddYears(1);
        var end = season.EndDate.AddYears(1);
        return new Season(id, NameFor(start, end), start, end);
    }
}
