using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;
using Ebm.Domain.Staff;

namespace Ebm.Domain.Scouting;

/// <summary>
/// What a club's scout believes about one player on <see cref="Date"/> (docs/SCOUTING.md §4): estimated attributes,
/// ratings, value and salary expectation, each within ±<see cref="Margin"/> (potential: ±<see cref="PotentialMargin"/>).
/// A snapshot: it does not follow the player's later changes. Never deleted; the newest report of a club is its view.
/// </summary>
public sealed class ScoutReport : Entity<ScoutReportId>
{
    public ScoutReport(
        ScoutReportId id,
        ClubId clubId,
        PlayerId playerId,
        StaffMemberId? scoutId,
        ScoutingAssignmentId? assignmentId,
        DateOnly date,
        ScoutEstimate estimate,
        long estimatedValue,
        long estimatedSalary)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        ClubId = Guard.Required(clubId, nameof(clubId));
        PlayerId = Guard.Required(playerId, nameof(playerId));
        ScoutId = Guard.Optional(scoutId, nameof(scoutId));
        AssignmentId = Guard.Optional(assignmentId, nameof(assignmentId));
        Date = date;
        Accuracy = Guard.Range(estimate.Accuracy, ScoutingRules.MinAccuracy, ScoutingRules.MaxAccuracy, "Accuracy");
        Margin = Guard.Range(estimate.Margin, 0, 50, "Margin");
        PotentialMargin = Guard.Range(estimate.PotentialMargin, 0, 50, "Potential margin");
        Attributes = estimate.Attributes;
        EstimatedOverall = Guard.Range(estimate.Overall, Player.MinRating, Player.MaxRating, "Estimated overall");
        EstimatedPotential = Guard.Range(estimate.Potential, Player.MinRating, Player.MaxRating, "Estimated potential");
        EstimatedValue = Math.Max(0, estimatedValue);
        EstimatedSalary = Math.Max(0, estimatedSalary);
    }

    private ScoutReport()
    {
    }

    /// <summary>The club the report belongs to.</summary>
    public ClubId ClubId { get; private set; }

    public PlayerId PlayerId { get; private set; }

    /// <summary>The scout who wrote it; <c>null</c> for background knowledge.</summary>
    public StaffMemberId? ScoutId { get; private set; }

    public ScoutingAssignmentId? AssignmentId { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>How well the player is known (5–98).</summary>
    public int Accuracy { get; private set; }

    /// <summary>Largest error of an attribute or the overall rating, in rating points.</summary>
    public int Margin { get; private set; }

    /// <summary>Largest error of the potential rating.</summary>
    public int PotentialMargin { get; private set; }

    /// <summary>Estimated attributes; each true value lies within ±<see cref="Margin"/>.</summary>
    public PlayerAttributes Attributes { get; private set; } = null!;

    public int EstimatedOverall { get; private set; }

    public int EstimatedPotential { get; private set; }

    /// <summary>Market value priced on the estimated ratings, in euros.</summary>
    public long EstimatedValue { get; private set; }

    /// <summary>Annual salary the player is expected to ask the club, priced on the estimated ratings.</summary>
    public long EstimatedSalary { get; private set; }

    public (int Low, int High) OverallRange => ScoutingRules.Range(EstimatedOverall, Margin);

    public (int Low, int High) PotentialRange => ScoutingRules.Range(EstimatedPotential, PotentialMargin);

    public (int Low, int High) RangeOf(PlayerAttribute attribute) => ScoutingRules.Range(Attributes[attribute], Margin);
}
