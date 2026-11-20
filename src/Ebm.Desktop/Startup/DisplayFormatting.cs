using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using Ebm.Domain.Players;
using Ebm.Services.Careers;

namespace Ebm.Desktop.Startup;

/// <summary>
/// One place for how values look on screen: WPF bindings use the user's culture (dates, numbers), enum values read as
/// words ("ShootingGuard" → "Shooting guard") and <see cref="DateOnly"/> uses the culture's short date instead of ISO.
/// Converters are attached through <see cref="TypeDescriptor"/>, so every binding, grid column and combo box picks them
/// up without per-screen converters.
/// </summary>
public static class DisplayFormatting
{
    private static bool _applied;

    public static void Apply()
    {
        if (_applied)
        {
            return;
        }

        _applied = true;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        foreach (var type in new[] { typeof(Position), typeof(ICareerService), typeof(DisplayFormatting) }
                     .SelectMany(t => t.Assembly.GetTypes())
                     .Where(t => t.IsEnum && t.IsPublic | t.IsNestedPublic))
        {
            TypeDescriptor.AddAttributes(type, new TypeConverterAttribute(typeof(HumanEnumConverter)));
        }

        TypeDescriptor.AddAttributes(typeof(DateOnly), new TypeConverterAttribute(typeof(CultureDateOnlyConverter)));
    }

    /// <summary>"ShootingGuard" → "Shooting guard"; "EUCitizen" → "EU citizen"; "Zone23" → "2-3 zone"; flags keep their ", " separator.</summary>
    public static string Humanize(string name)
    {
        if (ZoneDigits(name) is { } digits)
        {
            return string.Join('-', digits.ToCharArray()) + ZoneSuffix;
        }

        var text = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var startsWord = i > 0 && char.IsUpper(c) && name[i - 1] != ' '
                && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
            if (startsWord)
            {
                text.Append(' ');
            }

            // Lower-case a capital that starts a new word, unless it belongs to an acronym ("EU").
            var inAcronym = (i + 1 < name.Length && char.IsUpper(name[i + 1])) || (i > 0 && char.IsUpper(name[i - 1]) && !startsWord);
            text.Append(startsWord && !inAcronym ? char.ToLowerInvariant(c) : c);
        }

        return text.ToString();
    }

    private const string ZoneSuffix = " zone";

    /// <summary>The digits of a zone defence name ("Zone23" → "23"), else <c>null</c>.</summary>
    private static string? ZoneDigits(string name) =>
        name.Length > 4 && name.StartsWith("Zone", StringComparison.Ordinal) && name[4..].All(char.IsAsciiDigit) ? name[4..] : null;

    /// <summary>Inverse of <see cref="Humanize"/> for parsing: "2-3 zone" → "Zone23", "Shooting guard" → "Shootingguard" (enum parsing ignores case).</summary>
    internal static string Dehumanize(string text) =>
        text.EndsWith(ZoneSuffix, StringComparison.Ordinal) && text[..^ZoneSuffix.Length].Replace("-", string.Empty, StringComparison.Ordinal) is { Length: > 0 } digits && digits.All(char.IsAsciiDigit)
            ? "Zone" + digits
            : text.Replace(" ", string.Empty, StringComparison.Ordinal);

    private sealed class HumanEnumConverter(Type type) : EnumConverter(type)
    {
        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) && value is Enum
                ? string.Join(", ", value.ToString()!.Split(", ").Select(Humanize))
                : base.ConvertTo(context, culture, value, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is string text ? base.ConvertFrom(context, culture, Dehumanize(text)) : base.ConvertFrom(context, culture, value);
    }

    private sealed class CultureDateOnlyConverter : DateOnlyConverter
    {
        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) && value is DateOnly date
                ? date.ToString("d", culture ?? CultureInfo.CurrentCulture)
                : base.ConvertTo(context, culture, value, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is string text && DateOnly.TryParse(text, culture ?? CultureInfo.CurrentCulture, out var date)
                ? date
                : base.ConvertFrom(context, culture, value);
    }
}
