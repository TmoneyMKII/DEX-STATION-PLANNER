using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;

namespace DexStationPlanner.Core.Stations;

/// <summary>Plan shapes of site features in site-grid meters.</summary>
public static class FeatureGeometry
{
    /// <summary>True for features drawn as circles (diameter = <see cref="SiteFeature.Length"/>).</summary>
    public static bool IsRound(SiteFeatureKind kind) => kind is SiteFeatureKind.Helipad or SiteFeatureKind.ScienceSite;

    /// <summary>
    /// Closed outline of an area feature (counterclockwise), or null for roads. Rectangles are
    /// <see cref="SiteFeature.Length"/> along the feature's rotation by <see cref="SiteFeature.Width"/> across.
    /// </summary>
    public static List<Vec2>? Outline(SiteFeature f, int circleSegments = 48)
    {
        if (f.Kind == SiteFeatureKind.Road) return null;
        if (IsRound(f.Kind))
        {
            var r = Math.Max(0.5, f.Length / 2);
            return Enumerable.Range(0, circleSegments).Select(i => f.Position + Vec2.FromAngle(2 * Math.PI * i / circleSegments, r)).ToList();
        }
        var hx = Math.Max(0.5, f.Length / 2);
        var hy = Math.Max(0.5, f.Width / 2);
        var pose = f.Pose;
        return [pose.Apply(new(-hx, -hy)), pose.Apply(new(hx, -hy)), pose.Apply(new(hx, hy)), pose.Apply(new(-hx, hy))];
    }

    /// <summary>The two ends of a runway or pier along its long axis.</summary>
    public static (Vec2 A, Vec2 B) Ends(SiteFeature f)
    {
        var half = f.Pose.ApplyDirection(new Vec2(f.Length / 2, 0));
        return (f.Position - half, f.Position + half);
    }

    public static IEnumerable<Vec2> Points(SiteFeature f) =>
        f.Kind == SiteFeatureKind.Road ? f.Path : Outline(f) ?? [];

    public static double RoadLength(SiteFeature f)
    {
        double len = 0;
        for (var i = 1; i < f.Path.Count; i++) len += Vec2.Distance(f.Path[i - 1], f.Path[i]);
        return len;
    }

    /// <summary>Distance from a point to the feature (0 inside an area feature).</summary>
    public static double DistanceTo(SiteFeature f, Vec2 p)
    {
        if (f.Kind == SiteFeatureKind.Road)
        {
            var best = double.MaxValue;
            for (var i = 1; i < f.Path.Count; i++)
                best = Math.Min(best, new Segment(f.Path[i - 1], f.Path[i]).DistanceTo(p));
            return Math.Max(0, best - f.Width / 2);
        }
        var outline = Outline(f)!;
        if (Polygon.Contains(outline, p)) return 0;
        var d = double.MaxValue;
        for (var i = 0; i < outline.Count; i++)
            d = Math.Min(d, new Segment(outline[i], outline[(i + 1) % outline.Count]).DistanceTo(p));
        return d;
    }

    /// <summary>Moves a feature (all road points, or the center of an area feature).</summary>
    public static void Translate(SiteFeature f, Vec2 delta)
    {
        if (f.Kind == SiteFeatureKind.Road)
        {
            for (var i = 0; i < f.Path.Count; i++) f.Path[i] += delta;
        }
        else
        {
            f.Position += delta;
        }
    }

    /// <summary>Runway/helipad heading as the aviation runway number (e.g. 07/25), from true north.</summary>
    public static string RunwayDesignation(SiteFeature f, double gridHeadingDegrees)
    {
        // RotationDegrees is counterclockwise from grid east; compass bearings run clockwise from true north.
        var bearing = Pose2.NormalizeDegrees(90 - f.RotationDegrees + gridHeadingDegrees);
        int Number(double b)
        {
            var n = (int)Math.Round(Pose2.NormalizeDegrees(b) / 10);
            return n == 0 ? 36 : n;
        }
        var a = Number(bearing);
        var b = Number(bearing + 180);
        return a < b ? $"{a:00}/{b:00}" : $"{b:00}/{a:00}";
    }
}
