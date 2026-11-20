using System.Windows;
using System.Windows.Media;
using Ebm.Domain.Matches;

namespace Ebm.Desktop.Controls;

/// <summary>
/// Half-court shot chart (Step 29): FIBA lines with the basket at the top, made shots as filled dots, misses as
/// crosses. Coordinates are the engine's decimetres (<see cref="ShotRecord"/>).
/// </summary>
public sealed class ShotChartView : FrameworkElement
{
    public static readonly DependencyProperty ShotsProperty = DependencyProperty.Register(
        nameof(Shots),
        typeof(IEnumerable<ShotRecord>),
        typeof(ShotChartView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush MadeBrush = Frozen(Color.FromRgb(0x2E, 0x9E, 0x5B));
    private static readonly Brush MissedBrush = Frozen(Color.FromRgb(0xD6, 0x45, 0x45));

    public IEnumerable<ShotRecord>? Shots
    {
        get => (IEnumerable<ShotRecord>?)GetValue(ShotsProperty);
        set => SetValue(ShotsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width;
        return new Size(width, width * ShotZones.Length / (2.0 * ShotZones.HalfWidth));
    }

    protected override void OnRender(DrawingContext dc)
    {
        ArgumentNullException.ThrowIfNull(dc);
        var scale = Math.Min(ActualWidth / (2.0 * ShotZones.HalfWidth), ActualHeight / ShotZones.Length);
        if (scale <= 0)
        {
            return;
        }

        Point P(double x, double y) => new((x + ShotZones.HalfWidth) * scale, y * scale);
        var lineBrush = TryFindResource("Brush.TextMuted") as Brush ?? Brushes.Gray;
        var floor = TryFindResource("Brush.Surface") as Brush ?? Brushes.Transparent;
        var pen = new Pen(lineBrush, 1);
        pen.Freeze();

        dc.DrawRectangle(floor, pen, new Rect(P(-ShotZones.HalfWidth, 0), P(ShotZones.HalfWidth, ShotZones.Length)));
        dc.DrawRectangle(null, pen, new Rect(P(-24.5, 0), P(24.5, 58)));
        dc.DrawEllipse(null, pen, P(0, 58), 18 * scale, 18 * scale);
        dc.DrawLine(pen, P(-9, 12), P(9, 12));
        dc.DrawEllipse(null, pen, P(0, ShotZones.RimY), 2.25 * scale, 2.25 * scale);
        dc.DrawEllipse(null, pen, P(0, ShotZones.Length), 18 * scale, 18 * scale);
        Arc(dc, pen, P(-ShotZones.RestrictedRadius, ShotZones.RimY), P(ShotZones.RestrictedRadius, ShotZones.RimY), ShotZones.RestrictedRadius * scale);

        // Three-point line: straight corners, then the 6.75 m arc.
        dc.DrawLine(pen, P(-ShotZones.CornerX, 0), P(-ShotZones.CornerX, ShotZones.CornerTopY));
        dc.DrawLine(pen, P(ShotZones.CornerX, 0), P(ShotZones.CornerX, ShotZones.CornerTopY));
        Arc(dc, pen, P(-ShotZones.CornerX, ShotZones.CornerTopY), P(ShotZones.CornerX, ShotZones.CornerTopY), ShotZones.ArcRadius * scale);

        var dot = Math.Max(2.5, 2.2 * scale);
        var missPen = new Pen(MissedBrush, 1.5);
        missPen.Freeze();
        foreach (var shot in Shots ?? [])
        {
            var c = P(shot.X, shot.Y);
            if (shot.Made)
            {
                dc.DrawEllipse(MadeBrush, null, c, dot, dot);
            }
            else
            {
                dc.DrawLine(missPen, new Point(c.X - dot, c.Y - dot), new Point(c.X + dot, c.Y + dot));
                dc.DrawLine(missPen, new Point(c.X - dot, c.Y + dot), new Point(c.X + dot, c.Y - dot));
            }
        }
    }

    /// <summary>An arc bulging away from the baseline between two points.</summary>
    private static void Arc(DrawingContext dc, Pen pen, Point from, Point to, double radius)
    {
        var figure = new PathFigure { StartPoint = from, IsClosed = false };
        figure.Segments.Add(new ArcSegment(to, new Size(radius, radius), 0, isLargeArc: false, SweepDirection.Counterclockwise, isStroked: true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
