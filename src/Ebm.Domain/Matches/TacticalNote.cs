using Ebm.Domain.Common;

namespace Ebm.Domain.Matches;

/// <summary>
/// A tactical decision or situational summary of a match (Step 27), kept with the stored result so match reports
/// show how the coaches managed the game. <see cref="Clock"/> is the game clock in seconds.
/// </summary>
public sealed class TacticalNote
{
    public const int TextMaxLength = 200;

    public TacticalNote(int period, int clock, bool home, string text)
    {
        Period = Guard.Range(period, 1, 4 + Match.MaxOvertimes, nameof(period));
        Clock = Guard.Range(clock, 0, 3600, nameof(clock));
        Home = home;
        Text = Guard.Text(text, "Tactical note", TextMaxLength);
    }

    private TacticalNote()
    {
    }

    public int Period { get; private set; }

    public int Clock { get; private set; }

    /// <summary>Whether the home team made the decision.</summary>
    public bool Home { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>"Q4 01:58".</summary>
    public string Time => $"{(Period <= 4 ? $"Q{Period}" : $"OT{Period - 4}")} {Clock / 60:00}:{Clock % 60:00}";
}
