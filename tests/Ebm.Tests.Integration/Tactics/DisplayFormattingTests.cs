using System.ComponentModel;
using System.Globalization;
using Ebm.Desktop.Startup;
using Ebm.Domain.Tactics;

namespace Ebm.Tests.Integration.Tactics;

/// <summary>Enum values on screen read as words, zone defences as "2-3 zone" (Step 34), and parse back.</summary>
public sealed class DisplayFormattingTests
{
    [Theory]
    [InlineData(OffensiveSystem.PickAndRoll, "Pick and roll")]
    [InlineData(OffensiveSystem.FourOutOneIn, "Four out one in")]
    [InlineData(DefensiveSystem.ManToMan, "Man to man")]
    [InlineData(DefensiveSystem.Zone23, "2-3 zone")]
    [InlineData(DefensiveSystem.Zone131, "1-3-1 zone")]
    public void Tactic_values_read_as_words_and_parse_back(object value, string text)
    {
        DisplayFormatting.Apply();
        var converter = TypeDescriptor.GetConverter(value.GetType());

        Assert.Equal(text, converter.ConvertToString(null, CultureInfo.InvariantCulture, value));
        Assert.Equal(value, converter.ConvertFromString(null, CultureInfo.InvariantCulture, text));
    }
}
