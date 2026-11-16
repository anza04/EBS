using Ebm.Domain.Competitions.Rules;

namespace Ebm.Domain.Transfers;

/// <summary>Whether a club may register new first-team players today (docs/TRANSFERS.md §9).</summary>
/// <param name="Deadline">The earliest registration deadline of the club's competitions this season, if any.</param>
/// <param name="Competition">The competition that set <paramref name="Deadline"/>.</param>
/// <param name="ReopensOn">While closed: the season's last day, when registrations open again.</param>
public sealed record RegistrationWindow(bool IsOpen, DateOnly? Deadline, string? Competition, DateOnly? ReopensOn)
{
    public static RegistrationWindow Open { get; } = new(true, null, null, null);

    public string Message => IsOpen
        ? Deadline is { } deadline ? $"Registrations are open until {deadline:yyyy-MM-dd} ({Competition})." : "Registrations are open."
        : $"The registration deadline of {Competition} passed on {Deadline:yyyy-MM-dd}; new players can be registered again from {ReopensOn:yyyy-MM-dd}.";

    /// <summary>
    /// The window on <paramref name="today"/> for a club playing <paramref name="competitions"/> in the season from
    /// <paramref name="seasonStart"/> to <paramref name="seasonEnd"/>: closed after the earliest deadline until the
    /// season's last day, when the end-of-season window opens. Pure.
    /// </summary>
    public static RegistrationWindow On(DateOnly today, DateOnly seasonStart, DateOnly seasonEnd, IEnumerable<(string Name, RegistrationRules Rules)> competitions)
    {
        ArgumentNullException.ThrowIfNull(competitions);
        var earliest = competitions
            .Select(c => (c.Name, Date: c.Rules.DeadlineIn(seasonStart, seasonEnd)))
            .Where(c => c.Date is not null)
            .OrderBy(c => c.Date)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (earliest.Date is not { } deadline)
        {
            return Open;
        }

        var closed = today > deadline && today < seasonEnd;
        return new RegistrationWindow(!closed, deadline, earliest.Name, closed ? seasonEnd : null);
    }

    /// <summary>
    /// A club below its minimum squad may replace players after the deadline (an injury or emergency signing), so the
    /// calendar never blocks: only signings that keep the first team at or below <paramref name="minPlayers"/> pass.
    /// </summary>
    public bool Allows(int firstTeamAfterSigning, int minPlayers) => IsOpen || firstTeamAfterSigning <= minPlayers;
}
