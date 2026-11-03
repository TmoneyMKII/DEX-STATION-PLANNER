namespace DexStationPlanner.Core.Geometry;

/// <summary>
/// A straight or circular-arc segment from <see cref="Start"/> to <see cref="End"/>.
/// Curvature uses the CAD/DXF "bulge" convention: bulge = tan(sweep / 4).
/// 0 is straight, positive sweeps counterclockwise (bows to the right of the chord direction),
/// and ±1 is a semicircle.
/// </summary>
public readonly record struct Segment(Vec2 Start, Vec2 End, double Bulge = 0)
{
    public const double StraightTolerance = 1e-9;
    public const double MaxBulge = 1.0;

    public bool IsArc => Math.Abs(Bulge) > StraightTolerance && ChordLength > 1e-9;
    public double ChordLength => Vec2.Distance(Start, End);

    /// <summary>Signed sweep angle in radians (positive = counterclockwise).</summary>
    public double Sweep => IsArc ? 4 * Math.Atan(Bulge) : 0;

    public double Radius => IsArc ? ChordLength * (1 + Bulge * Bulge) / (4 * Math.Abs(Bulge)) : double.PositiveInfinity;

    public Vec2 Center
    {
        get
        {
            if (!IsArc) return Vec2.Lerp(Start, End, 0.5);
            var c = ChordLength;
            var dir = (End - Start) / c;
            var mid = Vec2.Lerp(Start, End, 0.5);
            return mid + dir.LeftNormal * (c * (1 - Bulge * Bulge) / (4 * Bulge));
        }
    }

    public double Length => IsArc ? Radius * Math.Abs(Sweep) : ChordLength;

    /// <summary>Point at arc-length distance <paramref name="s"/> from the start (clamped).</summary>
    public Vec2 PointAt(double s)
    {
        var len = Length;
        if (len < 1e-12) return Start;
        var t = Math.Clamp(s / len, 0, 1);
        if (!IsArc) return Vec2.Lerp(Start, End, t);
        var center = Center;
        var a0 = (Start - center).Angle;
        return center + Vec2.FromAngle(a0 + Sweep * t, Radius);
    }

    /// <summary>Unit tangent (direction of travel) at arc-length distance <paramref name="s"/>.</summary>
    public Vec2 TangentAt(double s)
    {
        if (!IsArc) return (End - Start).Normalized();
        var len = Length;
        var t = len < 1e-12 ? 0 : Math.Clamp(s / len, 0, 1);
        var a = (Start - Center).Angle + Sweep * t;
        var tangent = new Vec2(-Math.Sin(a), Math.Cos(a));
        return Sweep > 0 ? tangent : -tangent;
    }

    /// <summary>Arc-length position of the point on the segment closest to <paramref name="p"/>.</summary>
    public double ProjectDistance(Vec2 p)
    {
        if (!IsArc)
        {
            var d = End - Start;
            var lenSq = d.LengthSquared;
            if (lenSq < 1e-18) return 0;
            var t = Math.Clamp(Vec2.Dot(p - Start, d) / lenSq, 0, 1);
            return t * Math.Sqrt(lenSq);
        }

        var center = Center;
        var a0 = (Start - center).Angle;
        var rel = NormalizeAngle((p - center).Angle - a0, Sweep);
        var frac = rel / Sweep;
        if (frac is >= 0 and <= 1) return frac * Length;
        // Outside the arc's span: snap to the nearer endpoint.
        return Vec2.Distance(p, Start) <= Vec2.Distance(p, End) ? 0 : Length;
    }

    public double DistanceTo(Vec2 p) => Vec2.Distance(p, PointAt(ProjectDistance(p)));

    /// <summary>
    /// Points along the segment including both endpoints. Arcs are subdivided so no step
    /// exceeds <paramref name="maxStepRadians"/> of sweep.
    /// </summary>
    public IReadOnlyList<Vec2> Tessellate(double maxStepRadians = Math.PI / 24)
    {
        if (!IsArc) return [Start, End];
        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(Sweep) / maxStepRadians));
        var center = Center;
        var r = Radius;
        var a0 = (Start - center).Angle;
        var pts = new Vec2[steps + 1];
        pts[0] = Start;
        for (var i = 1; i < steps; i++) pts[i] = center + Vec2.FromAngle(a0 + Sweep * i / steps, r);
        pts[steps] = End;
        return pts;
    }

    /// <summary>
    /// Signed area between the chord and the arc (positive when the arc sweeps counterclockwise).
    /// Adding this to the shoelace area of the chord polygon gives the exact area of a bulged polygon.
    /// </summary>
    public double SegmentArea
    {
        get
        {
            if (!IsArc) return 0;
            var r = Radius;
            var theta = Sweep;
            return r * r / 2 * (theta - Math.Sin(theta));
        }
    }

    /// <summary>
    /// Bulge that makes the arc pass through a point at the given signed perpendicular offset
    /// from the chord midpoint (positive = right of the chord direction). Clamped to a semicircle.
    /// </summary>
    public static double BulgeFromSagitta(Vec2 start, Vec2 end, Vec2 through)
    {
        var c = Vec2.Distance(start, end);
        if (c < 1e-9) return 0;
        var dir = (end - start) / c;
        var mid = Vec2.Lerp(start, end, 0.5);
        var sagitta = Vec2.Dot(through - mid, dir.RightNormal);
        return Math.Clamp(2 * sagitta / c, -MaxBulge, MaxBulge);
    }

    /// <summary>Maps an angle into the same direction range as <paramref name="sweep"/> ([0, 2π) or (-2π, 0]).</summary>
    private static double NormalizeAngle(double angle, double sweep)
    {
        const double tau = 2 * Math.PI;
        angle %= tau;
        if (sweep >= 0)
        {
            if (angle < 0) angle += tau;
        }
        else if (angle > 0)
        {
            angle -= tau;
        }
        return angle;
    }
}
