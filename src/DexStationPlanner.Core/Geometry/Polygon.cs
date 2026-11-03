namespace DexStationPlanner.Core.Geometry;

/// <summary>Helpers for simple (non-self-intersecting) polygons given as point lists.</summary>
public static class Polygon
{
    /// <summary>Shoelace signed area; positive for counterclockwise winding.</summary>
    public static double SignedArea(IReadOnlyList<Vec2> pts)
    {
        double sum = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += Vec2.Cross(a, b);
        }
        return sum / 2;
    }

    /// <summary>Exact signed area of a closed loop of segments (straight or arc).</summary>
    public static double SignedArea(IReadOnlyList<Segment> loop)
    {
        double sum = 0;
        foreach (var seg in loop) sum += Vec2.Cross(seg.Start, seg.End) / 2 + seg.SegmentArea;
        return sum;
    }

    public static double Perimeter(IReadOnlyList<Segment> loop) => loop.Sum(s => s.Length);

    /// <summary>Flattens a closed loop of segments into a point ring (no duplicated closing point).</summary>
    public static List<Vec2> Tessellate(IReadOnlyList<Segment> loop, double maxStepRadians = Math.PI / 24)
    {
        var ring = new List<Vec2>();
        foreach (var seg in loop)
        {
            var pts = seg.Tessellate(maxStepRadians);
            for (var i = 0; i < pts.Count - 1; i++) ring.Add(pts[i]);
        }
        return ring;
    }

    public static bool Contains(IReadOnlyList<Vec2> ring, Vec2 p)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    public static (Vec2 Min, Vec2 Max) Bounds(IEnumerable<Vec2> pts)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var p in pts)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        return double.IsInfinity(minX) ? (Vec2.Zero, Vec2.Zero) : (new Vec2(minX, minY), new Vec2(maxX, maxY));
    }

    /// <summary>
    /// Ear-clipping triangulation of a simple polygon. Returns index triples into
    /// <paramref name="ring"/>, wound counterclockwise.
    /// </summary>
    public static List<int> Triangulate(IReadOnlyList<Vec2> ring)
    {
        var result = new List<int>();
        var n = ring.Count;
        if (n < 3) return result;

        var idx = Enumerable.Range(0, n).ToList();
        if (SignedArea(ring) < 0) idx.Reverse();

        var guard = 0;
        while (idx.Count > 3 && guard++ < n * n)
        {
            var clipped = false;
            for (var i = 0; i < idx.Count; i++)
            {
                var ia = idx[(i + idx.Count - 1) % idx.Count];
                var ib = idx[i];
                var ic = idx[(i + 1) % idx.Count];
                var a = ring[ia]; var b = ring[ib]; var c = ring[ic];

                var cross = Vec2.Cross(b - a, c - b);
                if (cross <= 1e-12)
                {
                    // Reflex or collinear vertex. Drop exact collinear points so they don't stall clipping.
                    if (Math.Abs(cross) <= 1e-12)
                    {
                        idx.RemoveAt(i);
                        clipped = true;
                        break;
                    }
                    continue;
                }

                var containsOther = false;
                foreach (var k in idx)
                {
                    if (k == ia || k == ib || k == ic) continue;
                    if (PointInTriangle(ring[k], a, b, c)) { containsOther = true; break; }
                }
                if (containsOther) continue;

                result.Add(ia); result.Add(ib); result.Add(ic);
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break; // Degenerate input; return what we have.
        }

        if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]); }
        return result;
    }

    private static bool PointInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
    {
        var d1 = Vec2.Cross(b - a, p - a);
        var d2 = Vec2.Cross(c - b, p - b);
        var d3 = Vec2.Cross(a - c, p - c);
        return d1 >= -1e-12 && d2 >= -1e-12 && d3 >= -1e-12;
    }
}
