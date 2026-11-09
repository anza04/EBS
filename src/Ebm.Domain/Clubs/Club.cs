using Ebm.Domain.Common;
using Ebm.Domain.Countries;

namespace Ebm.Domain.Clubs;

/// <summary>What the board expects from the season. Judged at season end (Step 12/27).</summary>
public enum BoardExpectation
{
    WinTitle,
    ReachPlayoffs,
    MidTable,
    AvoidRelegation,
    WinPromotion,
}

/// <summary>A basketball club. The divisions and competitions it plays in are recorded per season as participants.</summary>
public sealed class Club : Entity<ClubId>
{
    public const int NameMaxLength = 100;
    public const int ShortNameMaxLength = 20;
    public const int CityMaxLength = 100;
    public const int MinFoundedYear = 1850;
    public const int MaxFoundedYear = 2100;
    public const int MinReputation = 1;
    public const int MaxReputation = 100;

    /// <summary>Bound for the bank balance in either direction, in euros, to catch data-entry errors.</summary>
    public const long MaxBalance = 10_000_000_000;

    /// <summary>Upper bound for the annual player wage budget, in euros.</summary>
    public const long MaxWageBudget = 1_000_000_000;

    /// <summary>Default values of new clubs (and of clubs stored before Step 06).</summary>
    public const int DefaultReputation = 50;
    public const BoardExpectation DefaultBoardExpectation = BoardExpectation.MidTable;

    public Club(ClubId id, string name, string shortName, CountryId countryId, ArenaId? homeArenaId = null, int? foundedYear = null)
        : base(id)
    {
        UpdateDetails(name, shortName, countryId, homeArenaId, foundedYear);
        UpdateProfile(null, DefaultReputation, 0, 0, DefaultBoardExpectation);
    }

    private Club()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string ShortName { get; private set; } = string.Empty;

    public CountryId CountryId { get; private set; }

    /// <summary>Home city; when <c>null</c> the home arena's city is shown.</summary>
    public string? City { get; private set; }

    public ArenaId? HomeArenaId { get; private set; }

    public int? FoundedYear { get; private set; }

    /// <summary>How well known and respected the club is, 1–100.</summary>
    public int Reputation { get; private set; }

    /// <summary>Bank balance in whole euros; negative when in debt.</summary>
    public long Balance { get; private set; }

    /// <summary>Annual amount the board allows for player salaries, in whole euros.</summary>
    public long WageBudget { get; private set; }

    public BoardExpectation BoardExpectation { get; private set; }

    /// <summary>Changes the club's details. The ID, history and contracts are unaffected.</summary>
    public void UpdateDetails(string name, string shortName, CountryId countryId, ArenaId? homeArenaId, int? foundedYear)
    {
        Name = Guard.Text(name, "Club name", NameMaxLength);
        ShortName = Guard.Text(shortName, "Club short name", ShortNameMaxLength);
        CountryId = Guard.Required(countryId, "Club country");
        ChangeHomeArena(homeArenaId);
        FoundedYear = foundedYear is { } year ? Guard.Range(year, MinFoundedYear, MaxFoundedYear, "Founded year") : null;
    }

    public void ChangeHomeArena(ArenaId? arenaId) => HomeArenaId = Guard.Optional(arenaId, "Home arena");

    /// <summary>
    /// Books a one-off income (positive) or payment (negative), such as a signing bonus or a contract settlement.
    /// The balance may go negative but must stay within ±<see cref="MaxBalance"/>.
    /// </summary>
    public void ChangeBalance(long amount)
    {
        var balance = Balance + amount;
        if (balance is < -MaxBalance or > MaxBalance)
        {
            throw new DomainValidationException($"The club's balance would leave the allowed range of ±{MaxBalance:N0} euros.");
        }

        Balance = balance;
    }

    /// <summary>Changes city, reputation, finances and board expectation.</summary>
    public void UpdateProfile(string? city, int reputation, long balance, long wageBudget, BoardExpectation boardExpectation)
    {
        if (balance is < -MaxBalance or > MaxBalance)
        {
            throw new DomainValidationException($"Balance must be between -{MaxBalance:N0} and {MaxBalance:N0} euros.");
        }

        if (wageBudget is < 0 or > MaxWageBudget)
        {
            throw new DomainValidationException($"Wage budget must be between 0 and {MaxWageBudget:N0} euros.");
        }

        City = string.IsNullOrWhiteSpace(city) ? null : Guard.Text(city, "City", CityMaxLength);
        Reputation = Guard.Range(reputation, MinReputation, MaxReputation, "Club reputation");
        Balance = balance;
        WageBudget = wageBudget;
        BoardExpectation = Guard.Defined(boardExpectation, "Board expectation");
    }
}
