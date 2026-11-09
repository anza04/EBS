using Ebm.Domain.Common;

namespace Ebm.Domain.Competitions;

/// <summary>One tier of a league. <see cref="Level"/> 1 is the top division.</summary>
public sealed class Division : Entity<DivisionId>
{
    public const int NameMaxLength = 100;
    public const int MaxLevel = 10;

    public Division(DivisionId id, LeagueId leagueId, string name, int level)
        : base(id)
    {
        LeagueId = Guard.Required(leagueId, "League");
        UpdateDetails(name, level);
    }

    private Division()
    {
    }

    /// <summary>The owning league. A division never moves to another league.</summary>
    public LeagueId LeagueId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Level { get; private set; }

    public void UpdateDetails(string name, int level)
    {
        Name = Guard.Text(name, "Division name", NameMaxLength);
        Level = Guard.Range(level, 1, MaxLevel, "Division level");
    }
}
