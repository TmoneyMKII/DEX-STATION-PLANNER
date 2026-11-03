using DexStationPlanner.Core.Equipment;
using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Modules;

namespace DexStationPlanner.Core.Rendering;

/// <summary>
/// Builds a 3D model of a module in its local frame: grade at Z = 0, the floor slab underside at
/// <see cref="ModuleDefinition.ElevationAboveGround"/>, walls rising from the top of the slab.
/// </summary>
public static class ModuleMeshBuilder
{
    public const string FloorColor = "#6B7178";
    public const string ShellColor = "#D9DEE3";
    public const string InteriorWallColor = "#AEB6BE";
    public const string StiltColor = "#4A4F55";
    private const double MaxArcStep = Math.PI / 36;
    private const double StiltSize = 0.2;
    private const double StiltSpacing = 3.0;

    public static SceneMesh Build(ModuleDefinition m, EquipmentLookup catalog, double wallOpacity = 1)
    {
        var scene = new SceneMesh();
        var floorBottom = Math.Max(0, m.ElevationAboveGround);
        var floorTop = floorBottom + m.FloorThickness;

        if (ModuleGeometry.HasClosedShell(m))
        {
            var footprint = ModuleGeometry.Footprint(m, MaxArcStep);
            scene.Part("Floor", FloorColor).AddPrism(footprint, floorBottom, floorTop);
            if (floorBottom > 0.05) AddStilts(scene.Part("Stilts", StiltColor), m, floorBottom);
        }

        var shell = scene.Part("Shell", ShellColor, wallOpacity);
        foreach (var edge in ModuleGeometry.ShellEdges(m))
        {
            if (m.Shell.Count < 3 && edge.Index == m.Shell.Count - 1) break; // Open shell: don't close the loop.
            AddWall(shell, edge.Segment, m.ShellThickness, floorTop, m.WallHeight,
                m.Openings.Where(o => o.HostId == edge.Vertex.Id));
        }
        // Square posts at shell corners fill the notches left by centered wall thickness.
        foreach (var v in m.Shell)
            shell.AddPrism(Square(v.Position, m.ShellThickness), floorTop, floorTop + m.WallHeight);

        var interior = scene.Part("Interior walls", InteriorWallColor, wallOpacity);
        foreach (var w in m.InteriorWalls)
            AddWall(interior, w.Segment, w.Thickness, floorTop, m.WallHeight, m.Openings.Where(o => o.HostId == w.Id));

        foreach (var item in m.Equipment)
        {
            var def = catalog.Find(item.DefinitionId);
            if (def is null) continue;
            var part = scene.Part("Equipment", def.Color);
            var z0 = floorTop + item.MountHeight;
            if (def.Shape == EquipmentShape.Cylinder)
            {
                var r = Math.Min(def.Width, def.Depth) / 2;
                var ring = Enumerable.Range(0, 24).Select(i => item.Position + Vec2.FromAngle(2 * Math.PI * i / 24, r)).ToList();
                part.AddPrism(ring, z0, z0 + def.Height);
            }
            else
            {
                part.AddPrism(ModuleGeometry.EquipmentCorners(item, def), z0, z0 + def.Height);
            }
        }

        scene.Parts.RemoveAll(p => p.IsEmpty);
        return scene;
    }

    /// <summary>Adds the solid parts of one wall (straight or curved) between its openings.</summary>
    public static void AddWall(MeshPart part, Segment seg, double thickness, double baseZ, double height, IEnumerable<Opening> openings)
    {
        var len = seg.Length;
        if (len < 1e-6) return;
        foreach (var piece in WallPieces.Split(len, height, openings))
            AddWallPiece(part, seg, thickness, baseZ + piece.Z0, baseZ + piece.Z1, piece.S0, piece.S1);
    }

    private static void AddWallPiece(MeshPart part, Segment seg, double thickness, double z0, double z1, double s0, double s1)
    {
        var steps = seg.IsArc ? Math.Max(1, (int)Math.Ceiling(Math.Abs(seg.Sweep) * (s1 - s0) / seg.Length / MaxArcStep)) : 1;
        var half = thickness / 2;
        var left = new List<Vec2>(steps + 1);
        var right = new List<Vec2>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var s = s0 + (s1 - s0) * i / steps;
            var p = seg.PointAt(s);
            var n = seg.TangentAt(s).LeftNormal * half;
            left.Add(p + n);
            right.Add(p - n);
        }

        for (var i = 0; i < steps; i++)
        {
            // Right face (seen from the right side), left face, top and bottom.
            part.AddQuad(new Vec3(right[i], z0), new Vec3(right[i + 1], z0), new Vec3(right[i + 1], z1), new Vec3(right[i], z1));
            part.AddQuad(new Vec3(left[i + 1], z0), new Vec3(left[i], z0), new Vec3(left[i], z1), new Vec3(left[i + 1], z1));
            part.AddQuad(new Vec3(right[i], z1), new Vec3(right[i + 1], z1), new Vec3(left[i + 1], z1), new Vec3(left[i], z1));
            part.AddQuad(new Vec3(left[i], z0), new Vec3(left[i + 1], z0), new Vec3(right[i + 1], z0), new Vec3(right[i], z0));
        }
        part.AddQuad(new Vec3(left[0], z0), new Vec3(right[0], z0), new Vec3(right[0], z1), new Vec3(left[0], z1));
        part.AddQuad(new Vec3(right[steps], z0), new Vec3(left[steps], z0), new Vec3(left[steps], z1), new Vec3(right[steps], z1));
    }

    private static void AddStilts(MeshPart part, ModuleDefinition m, double height)
    {
        var inset = StiltSize;
        foreach (var edge in ModuleGeometry.ShellEdges(m))
        {
            var len = edge.Segment.Length;
            var count = Math.Max(1, (int)Math.Ceiling(len / StiltSpacing));
            for (var i = 0; i < count; i++)
            {
                var s = len * i / count;
                var p = edge.Segment.PointAt(s);
                // Pull each stilt slightly inside the footprint so it sits under the slab.
                var inward = ModuleGeometry.ShellWinding(m) > 0 ? edge.Segment.TangentAt(s).LeftNormal : edge.Segment.TangentAt(s).RightNormal;
                part.AddPrism(Square(p + inward * inset, StiltSize), 0, height);
            }
        }
    }

    private static List<Vec2> Square(Vec2 c, double size)
    {
        var h = size / 2;
        return [new(c.X - h, c.Y - h), new(c.X + h, c.Y - h), new(c.X + h, c.Y + h), new(c.X - h, c.Y + h)];
    }
}
