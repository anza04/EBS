using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Matches.Visualization;
using Ebm.Domain.Players;

namespace Ebm.Desktop.Controls;

/// <summary>
/// The 2D court (Step 30): draws a <see cref="CourtPlayback"/> — FIBA full court, the ten players (home blue, away
/// orange, labelled by initials), the ball with its arc height, and the current play-by-play line. While loaded it
/// ticks the playback on the render loop; it only reads the playback, never the simulation.
/// </summary>
public sealed class CourtView : FrameworkElement
{
    public static readonly DependencyProperty PlaybackProperty = DependencyProperty.Register(
        nameof(Playback),
        typeof(CourtPlayback),
        typeof(CourtView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NamesProperty = DependencyProperty.Register(
        nameof(Names),
        typeof(IReadOnlyDictionary<PlayerId, string>),
        typeof(CourtView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush HomeBrush = Frozen(Color.FromRgb(0x2F, 0x6F, 0xD6));
    private static readonly Brush AwayBrush = Frozen(Color.FromRgb(0xE0, 0x7A, 0x1F));
    private static readonly Brush BallBrush = Frozen(Color.FromRgb(0xC8, 0x5A, 0x12));
    private static readonly Brush ShadowBrush = Frozen(Color.FromArgb(0x50, 0, 0, 0));
    private static readonly Brush FloorBrush = Frozen(Color.FromRgb(0xD9, 0xB3, 0x82));
    private static readonly Brush PaintBrush = Frozen(Color.FromArgb(0x40, 0x8B, 0x4A, 0x1C));
    private static readonly Pen LinePen = FrozenPen(Brushes.White, 1.5);
    private static readonly Pen HolderPen = FrozenPen(Brushes.Gold, 2.5);
    private static readonly Pen EnteringPen = FrozenPen(Brushes.LimeGreen, 2.5);
    private static readonly Pen BallPen = FrozenPen(Brushes.Black, 0.8);

    private readonly Stopwatch _clock = new();
    private int _drawn = -1;

    public CourtView()
    {
        Loaded += (_, _) =>
        {
            _clock.Restart();
            CompositionTarget.Rendering += OnFrame;
        };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
    }

    public CourtPlayback? Playback
    {
        get => (CourtPlayback?)GetValue(PlaybackProperty);
        set => SetValue(PlaybackProperty, value);
    }

    /// <summary>Display names of the players, for their initials.</summary>
    public IReadOnlyDictionary<PlayerId, string>? Names
    {
        get => (IReadOnlyDictionary<PlayerId, string>?)GetValue(NamesProperty);
        set => SetValue(NamesProperty, value);
    }

    /// <summary>Two letters for a name: "Marco Belinelli" → "MB".</summary>
    public static string Initials(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[^1][0])),
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 560 : availableSize.Width;
        var height = (width * CourtTracker.Width / CourtTracker.Length) + 26;
        if (!double.IsInfinity(availableSize.Height) && height > availableSize.Height)
        {
            height = availableSize.Height;
            width = (height - 26) * CourtTracker.Length / CourtTracker.Width;
        }

        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        ArgumentNullException.ThrowIfNull(dc);
        var scale = Math.Min(ActualWidth / CourtTracker.Length, (ActualHeight - 26) / CourtTracker.Width);
        if (scale <= 0)
        {
            return;
        }

        var left = (ActualWidth - (CourtTracker.Length * scale)) / 2;
        Point P(CourtPoint c) => new(left + (c.X * scale), c.Y * scale);
        DrawCourt(dc, scale, left);

        var playback = Playback;
        _drawn = playback?.Version ?? -1;
        if (playback?.Current is not { } frame)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var radius = Math.Max(6, 0.42 * scale);
        foreach (var drawn in playback.Players())
        {
            var centre = P(drawn.Position);
            var pen = drawn.Entering || (frame.Action == CourtAction.Substitution && frame.Event.Player == drawn.Player.Player)
                ? EnteringPen
                : frame.BallHolder == drawn.Player.Player ? HolderPen : null;
            dc.DrawEllipse(drawn.Player.Side == TeamSide.Home ? HomeBrush : AwayBrush, pen, centre, radius, radius);
            var label = Text(Initials(Names?.GetValueOrDefault(drawn.Player.Player)), radius * 0.95, Brushes.White, dpi);
            dc.DrawText(label, new Point(centre.X - (label.Width / 2), centre.Y - (label.Height / 2)));
        }

        var (ball, height) = playback.Ball();
        var ground = P(ball);
        var ballRadius = Math.Max(3, 0.18 * scale) * (1 + (height * 0.06));
        dc.DrawEllipse(ShadowBrush, null, ground, ballRadius, ballRadius * 0.5);
        dc.DrawEllipse(BallBrush, BallPen, new Point(ground.X, ground.Y - (height * scale * 0.35)), ballRadius, ballRadius);

        var caption = Text($"{frame.Event.Time}  {frame.Event.HomeScore}–{frame.Event.AwayScore}   {frame.Event.Text}", 12, TryFindResource("Brush.Text") as Brush ?? Brushes.Black, dpi);
        caption.MaxTextWidth = Math.Max(1, ActualWidth - 8);
        caption.MaxLineCount = 1;
        caption.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(caption, new Point(4, (CourtTracker.Width * scale) + 5));
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var seconds = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (Playback is { } playback && (playback.Tick(Math.Min(seconds, 0.25)) || playback.Version != _drawn))
        {
            InvalidateVisual();
        }
    }

    private static void DrawCourt(DrawingContext dc, double scale, double left)
    {
        const double L = CourtTracker.Length;
        const double W = CourtTracker.Width;
        Point P(double x, double y) => new(left + (x * scale), y * scale);
        dc.DrawRectangle(FloorBrush, LinePen, new Rect(P(0, 0), P(L, W)));
        dc.DrawLine(LinePen, P(L / 2, 0), P(L / 2, W));
        dc.DrawEllipse(null, LinePen, P(L / 2, W / 2), 1.8 * scale, 1.8 * scale);
        foreach (var right in new[] { false, true })
        {
            double X(double depth) => right ? L - depth : depth;
            var y = W / 2;
            dc.DrawRectangle(PaintBrush, LinePen, new Rect(P(X(0), y - 2.45), P(X(5.8), y + 2.45)));
            dc.DrawEllipse(null, LinePen, P(X(5.8), y), 1.8 * scale, 1.8 * scale);
            dc.DrawLine(LinePen, P(X(1.2), y - 0.9), P(X(1.2), y + 0.9));
            dc.DrawEllipse(null, LinePen, P(X(CourtTracker.RimDistance), y), 0.225 * scale, 0.225 * scale);

            // Three-point line: straight corners 0.9 m from the sidelines, then the 6.75 m arc around the rim.
            var cornerDepth = CourtTracker.RimDistance + Math.Sqrt((6.75 * 6.75) - (6.6 * 6.6));
            dc.DrawLine(LinePen, P(X(0), 0.9), P(X(cornerDepth), 0.9));
            dc.DrawLine(LinePen, P(X(0), W - 0.9), P(X(cornerDepth), W - 0.9));
            var figure = new PathFigure { StartPoint = P(X(cornerDepth), 0.9), IsClosed = false };
            figure.Segments.Add(new ArcSegment(P(X(cornerDepth), W - 0.9), new Size(6.75 * scale, 6.75 * scale), 0, false, right ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true));
            var arc = new PathGeometry([figure]);
            arc.Freeze();
            dc.DrawGeometry(null, LinePen, arc);
        }
    }

    private static FormattedText Text(string text, double size, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), Math.Max(6, size), brush, dpi);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
