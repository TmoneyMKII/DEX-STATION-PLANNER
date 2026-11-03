using DexStationPlanner.Core.Geometry;

namespace DexStationPlanner.Core.Rendering;

/// <summary>A 3D point in meters. Z is up.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public Vec3(Vec2 p, double z) : this(p.X, p.Y, z) { }
}

/// <summary>A single-color triangle mesh. Faces are not shared between quads so shading stays flat.</summary>
public sealed class MeshPart(string name, string color, double opacity = 1)
{
    public string Name { get; } = name;
    public string Color { get; } = color;
    public double Opacity { get; } = opacity;
    public List<Vec3> Positions { get; } = [];
    public List<int> Indices { get; } = [];

    public bool IsEmpty => Indices.Count == 0;

    public void AddTriangle(Vec3 a, Vec3 b, Vec3 c)
    {
        var i = Positions.Count;
        Positions.Add(a); Positions.Add(b); Positions.Add(c);
        Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 2);
    }

    /// <summary>Adds a quad a-b-c-d, wound counterclockwise when seen from the front.</summary>
    public void AddQuad(Vec3 a, Vec3 b, Vec3 c, Vec3 d)
    {
        var i = Positions.Count;
        Positions.Add(a); Positions.Add(b); Positions.Add(c); Positions.Add(d);
        Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 2);
        Indices.Add(i); Indices.Add(i + 2); Indices.Add(i + 3);
    }

    /// <summary>Axis-aligned-in-plan box rotated about Z: footprint corners (counterclockwise) extruded from z0 to z1.</summary>
    public void AddPrism(IReadOnlyList<Vec2> ringCcw, double z0, double z1, IReadOnlyList<int>? triangulation = null)
    {
        var n = ringCcw.Count;
        if (n < 3 || z1 <= z0) return;
        var tris = triangulation ?? Polygon.Triangulate(ringCcw);
        for (var t = 0; t < tris.Count; t += 3)
        {
            var a = ringCcw[tris[t]]; var b = ringCcw[tris[t + 1]]; var c = ringCcw[tris[t + 2]];
            AddTriangle(new Vec3(a, z1), new Vec3(b, z1), new Vec3(c, z1));
            AddTriangle(new Vec3(a, z0), new Vec3(c, z0), new Vec3(b, z0));
        }
        for (var i = 0; i < n; i++)
        {
            var p = ringCcw[i];
            var q = ringCcw[(i + 1) % n];
            AddQuad(new Vec3(p, z0), new Vec3(q, z0), new Vec3(q, z1), new Vec3(p, z1));
        }
    }
}

public sealed class SceneMesh
{
    public List<MeshPart> Parts { get; } = [];

    public MeshPart Part(string name, string color, double opacity = 1)
    {
        var existing = Parts.FirstOrDefault(p => p.Name == name && p.Color == color && Math.Abs(p.Opacity - opacity) < 1e-6);
        if (existing is not null) return existing;
        var part = new MeshPart(name, color, opacity);
        Parts.Add(part);
        return part;
    }
}
