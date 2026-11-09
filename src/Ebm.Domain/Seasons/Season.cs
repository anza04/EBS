using Ebm.Domain.Common;

namespace Ebm.Domain.Seasons;

/// <summary>A sporting season (e.g. "2026-27") shared by all competitions.</summary>
public sealed class Season : Entity<SeasonId>
{
    public const int NameMaxLength = 20;

    public Season(SeasonId id, string name, DateOnly startDate, DateOnly endDate)
        : base(id)
    {
        UpdateDetails(name, startDate, endDate);
    }

    public void UpdateDetails(string name, DateOnly startDate, DateOnly endDate)
    {
        Name = Guard.Text(name, "Season name", NameMaxLength);
        EndDate = Guard.After(endDate, startDate, "Season end date");
        StartDate = startDate;
    }

    private Season()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }
}
