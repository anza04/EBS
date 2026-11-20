using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Ebm.Desktop.Controls;

/// <summary>Attached properties used by the shared templates in Themes/Shell.xaml.</summary>
public static class Ui
{
    /// <summary>Icon glyph (Segoe Fluent Icons / Segoe MDL2 Assets code point) shown by the navigation item template.</summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Ui), new PropertyMetadata(null));

    public static string? GetIcon(DependencyObject element) => (string?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, string? value) => element.SetValue(IconProperty, value);
}

/// <summary>
/// Colours a 1–100 rating as a badge: background by band (elite, good, average, low), white text.
/// ConverterParameter "fg" returns the text colour. Values that are not numbers keep the normal text colours.
/// </summary>
public sealed class RatingBrushConverter : IValueConverter
{
    private static readonly Brush Elite = Freeze(Color.FromRgb(0x1F, 0x8A, 0x4C));
    private static readonly Brush Good = Freeze(Color.FromRgb(0x3E, 0x7C, 0xB1));
    private static readonly Brush Average = Freeze(Color.FromRgb(0x7A, 0x86, 0x99));
    private static readonly Brush Low = Freeze(Color.FromRgb(0xB0, 0xB7, 0xC3));

    public static Brush? BandOf(object? value) =>
        value switch
        {
            int rating => rating >= 75 ? Elite : rating >= 65 ? Good : rating >= 50 ? Average : Low,
            double rating => BandOf((int)Math.Round(rating)),
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rating) => BandOf(rating),
            _ => null,
        };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var band = BandOf(value);
        return parameter as string == "fg"
            ? band is null ? DependencyProperty.UnsetValue : Brushes.White
            : band ?? (object)Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
