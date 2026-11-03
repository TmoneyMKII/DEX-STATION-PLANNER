using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DexStationPlanner.App.Converters;

/// <summary>True when the bound enum equals the parameter (by name). ConvertBack returns the parameter when checked.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}

/// <summary>Visible when the bound enum equals the parameter (by name), otherwise Collapsed.</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Collapsed for null (or Visible for null when the parameter is "invert").</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is not null && (value is not string s || s.Length > 0);
        if (parameter as string == "invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (parameter as string == "invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Friendly text for enum values: "FuelFarm" becomes "Fuel farm", with a few domain-specific overrides.</summary>
public sealed class HumanizeConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Overrides = new()
    {
        ["Runway"] = "Runway / skiway",
        ["Port"] = "Port / pier",
        ["Road"] = "Road / track",
        ["Snow"] = "Groomed snow",
        ["FixedWing"] = "Fixed-wing aircraft",
        ["Rotorcraft"] = "Helicopters",
        ["Ship"] = "Ships",
        ["Overland"] = "Overland vehicles",
    };

    public static string Humanize(object? value)
    {
        var s = value?.ToString() ?? "";
        if (Overrides.TryGetValue(s, out var o)) return o;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i])) sb.Append(' ').Append(char.ToLowerInvariant(s[i]));
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Humanize(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>"#RRGGBB" to a brush, for swatches.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex) return new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
        }
        catch (FormatException)
        {
        }
        return System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
