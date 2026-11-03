namespace DexStationPlanner.Core.Geometry;

/// <summary>A WGS84 position in degrees and meters above the ellipsoid.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude, double Height = 0);

/// <summary>
/// WGS84 conversions between geodetic coordinates and a local east/north tangent plane at a site origin.
/// Matches CesiumJS's east-north-up frame (including at the poles, where "east" follows the origin's longitude),
/// so points converted here line up with what the globe draws.
/// </summary>
public static class Geodesy
{
    public const double SemiMajorAxis = 6378137.0;
    public const double Flattening = 1 / 298.257223563;
    public static readonly double EccentricitySquared = Flattening * (2 - Flattening);
    public const double MeanEarthRadius = 6371008.8;

    private const double Deg = Math.PI / 180;

    public static (double X, double Y, double Z) ToEcef(GeoPoint p)
    {
        var lat = p.Latitude * Deg;
        var lon = p.Longitude * Deg;
        var sinLat = Math.Sin(lat);
        var n = SemiMajorAxis / Math.Sqrt(1 - EccentricitySquared * sinLat * sinLat);
        return ((n + p.Height) * Math.Cos(lat) * Math.Cos(lon),
                (n + p.Height) * Math.Cos(lat) * Math.Sin(lon),
                (n * (1 - EccentricitySquared) + p.Height) * sinLat);
    }

    public static GeoPoint FromEcef(double x, double y, double z)
    {
        var lon = Math.Atan2(y, x);
        var p = Math.Sqrt(x * x + y * y);
        // Iterative solution; converges to sub-millimeter in a few steps for any terrestrial point.
        var lat = Math.Atan2(z, p * (1 - EccentricitySquared));
        double h = 0;
        for (var i = 0; i < 8; i++)
        {
            var sinLat = Math.Sin(lat);
            var n = SemiMajorAxis / Math.Sqrt(1 - EccentricitySquared * sinLat * sinLat);
            h = Math.Abs(Math.Cos(lat)) > 1e-10 ? p / Math.Cos(lat) - n : Math.Abs(z) - n * (1 - EccentricitySquared);
            lat = Math.Atan2(z, p * (1 - EccentricitySquared * n / (n + h)));
        }
        return new GeoPoint(lat / Deg, lon / Deg, h);
    }

    /// <summary>Unit east and north vectors of the local tangent plane at <paramref name="origin"/> (ECEF).</summary>
    private static ((double X, double Y, double Z) East, (double X, double Y, double Z) North) Frame(GeoPoint origin)
    {
        var lat = origin.Latitude * Deg;
        var lon = origin.Longitude * Deg;
        var east = (-Math.Sin(lon), Math.Cos(lon), 0.0);
        var north = (-Math.Sin(lat) * Math.Cos(lon), -Math.Sin(lat) * Math.Sin(lon), Math.Cos(lat));
        return (east, north);
    }

    /// <summary>East/north meters on the tangent plane at <paramref name="origin"/> to a geodetic point on the ellipsoid surface below it.</summary>
    public static GeoPoint FromLocal(GeoPoint origin, Vec2 eastNorth)
    {
        var o = ToEcef(origin with { Height = 0 });
        var (e, n) = Frame(origin);
        var x = o.X + e.X * eastNorth.X + n.X * eastNorth.Y;
        var y = o.Y + e.Y * eastNorth.X + n.Y * eastNorth.Y;
        var z = o.Z + e.Z * eastNorth.X + n.Z * eastNorth.Y;
        var g = FromEcef(x, y, z);
        return g with { Height = 0 };
    }

    /// <summary>Projects a geodetic point onto the tangent plane at <paramref name="origin"/>, as east/north meters.</summary>
    public static Vec2 ToLocal(GeoPoint origin, GeoPoint point)
    {
        var o = ToEcef(origin with { Height = 0 });
        var p = ToEcef(point with { Height = 0 });
        var (e, n) = Frame(origin);
        var d = (X: p.X - o.X, Y: p.Y - o.Y, Z: p.Z - o.Z);
        return new Vec2(d.X * e.X + d.Y * e.Y + d.Z * e.Z, d.X * n.X + d.Y * n.Y + d.Z * n.Z);
    }

    /// <summary>Great-circle distance in meters (mean-radius sphere; within ~0.5% of the ellipsoid).</summary>
    public static double GreatCircleDistance(GeoPoint a, GeoPoint b)
    {
        var dLat = (b.Latitude - a.Latitude) * Deg;
        var dLon = (b.Longitude - a.Longitude) * Deg;
        var h = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(a.Latitude * Deg) * Math.Cos(b.Latitude * Deg) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * MeanEarthRadius * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>Site-grid coordinates to true east/north (grid north is rotated clockwise from true north by the heading).</summary>
    public static Vec2 GridToEnu(Vec2 grid, double gridHeadingDegrees) => grid.Rotate(-gridHeadingDegrees * Deg);

    public static Vec2 EnuToGrid(Vec2 enu, double gridHeadingDegrees) => enu.Rotate(gridHeadingDegrees * Deg);
}
