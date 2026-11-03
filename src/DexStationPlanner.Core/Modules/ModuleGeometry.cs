using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;

namespace DexStationPlanner.Core.Modules;

/// <summary>A shell edge: the segment from a shell vertex to the next one.</summary>
public readonly record struct ShellEdge(ShellVertex Vertex, int Index, Segment Segment);

/// <summary>A point on a module boundary with its outward-facing unit direction.</summary>
public readonly record struct ConnectorFrame(Vec2 Position, Vec2 Outward);

/// <summary>Derived geometry for a <see cref="ModuleDefinition"/>.</summary>
public static class ModuleGeometry
{
    public static bool HasClosedShell(ModuleDefinition m) => m.Shell.Count >= 3;

    public static List<ShellEdge> ShellEdges(ModuleDefinition m)
    {
        var edges = new List<ShellEdge>(m.Shell.Count);
        if (m.Shell.Count < 2) return edges;
        for (var i = 0; i < m.Shell.Count; i++)
        {
            var v = m.Shell[i];
            var next = m.Shell[(i + 1) % m.Shell.Count];
            edges.Add(new ShellEdge(v, i, new Segment(v.Position, next.Position, v.Bulge)));
        }
        return edges;
    }

    public static List<Segment> ShellLoop(ModuleDefinition m) => ShellEdges(m).Select(e => e.Segment).ToList();

    /// <summary>The shell as a point ring, counterclockwise regardless of how it was drawn.</summary>
    public static List<Vec2> Footprint(ModuleDefinition m, double maxStepRadians = Math.PI / 24)
    {
        var ring = Polygon.Tessellate(ShellLoop(m), maxStepRadians);
        if (Polygon.SignedArea(ring) < 0) ring.Reverse();
        return ring;
    }

    /// <summary>+1 when the shell runs counterclockwise, -1 when clockwise.</summary>
    public static int ShellWinding(ModuleDefinition m) => Polygon.SignedArea(ShellLoop(m)) >= 0 ? 1 : -1;

    public static ShellEdge? FindEdge(ModuleDefinition m, Guid vertexId)
    {
        foreach (var e in ShellEdges(m))
            if (e.Vertex.Id == vertexId) return e;
        return null;
    }

    /// <summary>Resolves an opening host (shell edge or interior wall) to its segment.</summary>
    public static Segment? FindHostSegment(ModuleDefinition m, Guid hostId)
    {
        if (FindEdge(m, hostId) is { } edge) return edge.Segment;
        var wall = m.InteriorWalls.FirstOrDefault(w => w.Id == hostId);
        return wall?.Segment;
    }

    public static ConnectorFrame? ConnectorFrame(ModuleDefinition m, ModuleConnector c)
    {
        if (FindEdge(m, c.EdgeId) is not { } edge) return null;
        var seg = edge.Segment;
        var pos = seg.PointAt(c.Offset);
        var tangent = seg.TangentAt(c.Offset);
        // Interior is on the left of travel for a counterclockwise loop, so outward is to the right.
        var outward = ShellWinding(m) > 0 ? tangent.RightNormal : tangent.LeftNormal;
        return new ConnectorFrame(pos, outward);
    }

    /// <summary>Corners of an equipment footprint in module coordinates.</summary>
    public static Vec2[] EquipmentCorners(EquipmentPlacement p, EquipmentDefinition d)
    {
        var hx = d.Width / 2;
        var hy = d.Depth / 2;
        var pose = p.Pose;
        return
        [
            pose.Apply(new Vec2(-hx, -hy)), pose.Apply(new Vec2(hx, -hy)),
            pose.Apply(new Vec2(hx, hy)), pose.Apply(new Vec2(-hx, hy)),
        ];
    }

    /// <summary>
    /// Moves openings and connectors so they stay on their host after the host changed length,
    /// and removes ones whose host no longer exists.
    /// </summary>
    public static void ClampAttachments(ModuleDefinition m)
    {
        foreach (var o in m.Openings.ToList())
        {
            if (FindHostSegment(m, o.HostId) is not { } seg) { m.Openings.Remove(o); continue; }
            var half = Math.Min(o.Width / 2, seg.Length / 2);
            var clamped = Math.Clamp(o.Offset, half, seg.Length - half);
            if (Math.Abs(clamped - o.Offset) > 1e-9) o.Offset = clamped;
        }
        foreach (var c in m.Connectors.ToList())
        {
            if (FindEdge(m, c.EdgeId) is not { } edge) { m.Connectors.Remove(c); continue; }
            var len = edge.Segment.Length;
            var half = Math.Min(c.Width / 2, len / 2);
            var clamped = Math.Clamp(c.Offset, half, len - half);
            if (Math.Abs(clamped - c.Offset) > 1e-9) c.Offset = clamped;
        }
    }
}
