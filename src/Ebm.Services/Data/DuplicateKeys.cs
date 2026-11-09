using System.Globalization;
using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;

namespace Ebm.Services.Data;

/// <summary>
/// Natural keys that identify the same real-world record even when IDs differ. The importer and the
/// database editor both use them, so a duplicate is detected the same way everywhere.
/// </summary>
/// <remarks>Text is compared trimmed, case-insensitively and with runs of spaces collapsed.</remarks>
public static class DuplicateKeys
{
    /// <summary>Same first name, last name and date of birth.</summary>
    public static string Of(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return Player(player.FirstName, player.LastName, player.DateOfBirth);
    }

    public static string Player(string firstName, string lastName, DateOnly dateOfBirth) =>
        Join(Normalise(firstName), Normalise(lastName), dateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>Same name in the same country.</summary>
    public static string Of(Club club)
    {
        ArgumentNullException.ThrowIfNull(club);
        return Join(club.CountryId.ToString(), Normalise(club.Name));
    }

    /// <summary>Same name in the same city.</summary>
    public static string Of(Arena arena)
    {
        ArgumentNullException.ThrowIfNull(arena);
        return Join(Normalise(arena.Name), Normalise(arena.City));
    }

    /// <summary>Same name.</summary>
    public static string Of(League league)
    {
        ArgumentNullException.ThrowIfNull(league);
        return Normalise(league.Name);
    }

    /// <summary>Same level in the same league (also enforced by the database).</summary>
    public static string Of(Division division)
    {
        ArgumentNullException.ThrowIfNull(division);
        return Join(division.LeagueId.ToString(), division.Level.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Same name in the same country (or both international).</summary>
    public static string Of(Competition competition)
    {
        ArgumentNullException.ThrowIfNull(competition);
        return Join(competition.CountryId?.ToString() ?? "-", Normalise(competition.Name));
    }

    /// <summary>Same person, club and start date.</summary>
    public static string Of(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return Join(
            contract.ClubId.ToString(),
            contract.PlayerId?.ToString() ?? contract.StaffMemberId?.ToString() ?? "-",
            contract.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static string Normalise(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();

    private static string Join(params string[] parts) => string.Join('|', parts);
}
