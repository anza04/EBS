using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Seasons;

namespace Ebm.Domain.Careers;

/// <summary>
/// The user's career: the managed club, the season being played and the game date. A game database holds at most one
/// career; its date is the game's "today" while it is loaded. The date only moves forward.
/// </summary>
public sealed class Career : Entity<CareerId>
{
    public const int ManagerNameMaxLength = 60;

    public Career(CareerId id, string managerName, ClubId clubId, SeasonId seasonId, DateOnly startDate)
        : base(id)
    {
        ManagerName = Guard.Text(managerName, "Manager name", ManagerNameMaxLength);
        ClubId = Guard.Required(clubId, nameof(clubId));
        SeasonId = Guard.Required(seasonId, nameof(seasonId));
        StartDate = startDate;
        CurrentDate = startDate;
    }

    private Career()
    {
    }

    public string ManagerName { get; private set; } = string.Empty;

    /// <summary>The club the user manages.</summary>
    public ClubId ClubId { get; private set; }

    /// <summary>The season being played (or, after the season is closed, the next one).</summary>
    public SeasonId SeasonId { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>The game date.</summary>
    public DateOnly CurrentDate { get; private set; }

    /// <summary>Number of seasons started, the first one included.</summary>
    public int SeasonsPlayed { get; private set; } = 1;

    /// <summary>
    /// "Holiday": the assistant manages the user's club like an AI club (line-ups, tactics, contracts, transfers) and
    /// plays its matches, so the calendar no longer stops for them.
    /// </summary>
    public bool AssistantManages { get; private set; }

    public void SetAssistant(bool manages) => AssistantManages = manages;

    public void MoveTo(DateOnly date)
    {
        if (date < CurrentDate)
        {
            throw new DomainValidationException("The career date cannot go back in time.");
        }

        CurrentDate = date;
    }

    /// <summary>Moves the career on to the next season.</summary>
    public void StartSeason(SeasonId seasonId)
    {
        SeasonId = Guard.Required(seasonId, nameof(seasonId));
        SeasonsPlayed++;
    }
}
