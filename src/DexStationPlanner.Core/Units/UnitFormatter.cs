using System.Globalization;
using DexStationPlanner.Core.Model;

namespace DexStationPlanner.Core.Units;

/// <summary>Converts SI values for display. Everything is stored in meters, kilograms and watts.</summary>
public static class UnitFormatter
{
    public const double MetersPerFoot = 0.3048;
    public const double KgPerPound = 0.45359237;
    private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;

    public static string LengthUnit(UnitSystem u) => u == UnitSystem.Metric ? "m" : "ft";

    public static double ToDisplayLength(double meters, UnitSystem u) => u == UnitSystem.Metric ? meters : meters / MetersPerFoot;
    public static double FromDisplayLength(double value, UnitSystem u) => u == UnitSystem.Metric ? value : value * MetersPerFoot;

    public static string Length(double meters, UnitSystem u, int decimals = 2) =>
        u == UnitSystem.Metric
            ? meters.ToString("N" + decimals, Culture) + " m"
            : (meters / MetersPerFoot).ToString("N" + decimals, Culture) + " ft";

    /// <summary>Architectural-style length for imperial (12' 6"), plain meters for metric.</summary>
    public static string Dimension(double meters, UnitSystem u)
    {
        if (u == UnitSystem.Metric) return meters.ToString("N2", Culture) + " m";
        var totalInches = Math.Round(meters / MetersPerFoot * 12 * 2) / 2; // nearest half inch
        var feet = (int)Math.Floor(totalInches / 12);
        var inches = totalInches - feet * 12;
        return $"{feet}' {inches.ToString("0.#", Culture)}\"";
    }

    public static string Area(double m2, UnitSystem u) =>
        u == UnitSystem.Metric
            ? m2.ToString("N1", Culture) + " m²"
            : (m2 / (MetersPerFoot * MetersPerFoot)).ToString("N0", Culture) + " ft²";

    public static string Volume(double m3, UnitSystem u) =>
        u == UnitSystem.Metric
            ? m3.ToString("N1", Culture) + " m³"
            : (m3 / Math.Pow(MetersPerFoot, 3)).ToString("N0", Culture) + " ft³";

    public static string Mass(double kg, UnitSystem u)
    {
        if (u == UnitSystem.Imperial)
        {
            var lb = kg / KgPerPound;
            return lb >= 10000 ? (lb / 2000).ToString("N1", Culture) + " short tons" : lb.ToString("N0", Culture) + " lb";
        }
        return kg >= 10000 ? (kg / 1000).ToString("N1", Culture) + " t" : kg.ToString("N0", Culture) + " kg";
    }

    /// <summary>Power is shown in SI watts in both systems, as is usual for engineering documents.</summary>
    public static string Power(double watts) =>
        Math.Abs(watts) >= 1000 ? (watts / 1000).ToString("N1", Culture) + " kW" : watts.ToString("N0", Culture) + " W";
}
