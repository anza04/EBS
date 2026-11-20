using Ebm.Domain.Matches;

namespace Ebm.Desktop.ViewModels.Matches;

/// <summary>One zone of a shot chart: made-attempted, percentage and points (Step 29).</summary>
public sealed record ShotZoneRow(string Zone, string Shots, double Percentage, int Points)
{
    public static IReadOnlyList<ShotZoneRow> From(IEnumerable<ShotRecord> shots) =>
        [.. ShotChart.Summarize(shots).Select(z => new ShotZoneRow(ShotZones.Label(z.Zone), $"{z.Made}-{z.Attempted}", z.Percentage, z.Points))];
}
