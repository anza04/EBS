using Ebm.Domain.Common;

namespace Ebm.Domain.Seasons;

/// <summary>A sporting season (e.g. "2026-27") shared by all competitions.</summary>
public sealed class Season : Entity<SeasonId>
{
    public const int NameMaxLength = 20;

    public Season(SeasonId id, string name, DateOnly startDate, DateOnly endDate)
        : base(id)
    {
        Name = Guard.Text(name, "Season name", NameMaxLength);
        StartDate = startDate;
        EndDate = Guard.After(endDate, startDate, "Season end date");
    }

    private Season()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }
}
