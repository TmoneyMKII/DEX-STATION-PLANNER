using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;

namespace DexStationPlanner.Core.Modules;

public enum ModuleShapeKind { Blank, IsoContainer, Rectangle, Round, Capsule, RegularPolygon }

/// <summary>A starting point for a new module design.</summary>
public sealed record ModuleTemplate(
    string Key,
    string Name,
    string Description,
    ModuleShapeKind Shape,
    double Length,
    double Width,
    double WallHeight,
    double StructureMassKg,
    int Sides = 0,
    bool AddEndConnectors = false);

public static class ModuleTemplates
{
    // ISO 668 external dimensions; typical tare masses for insulated/modified units.
    public static readonly IReadOnlyList<ModuleTemplate> All =
    [
        new("blank", "Blank", "Empty canvas. Draw the shell yourself.", ModuleShapeKind.Blank, 0, 0, 2.4, 0),
        new("iso-10", "ISO 10 ft Container", "2.99 × 2.44 m prefab unit.", ModuleShapeKind.IsoContainer, 2.991, 2.438, 2.35, 1300, AddEndConnectors: true),
        new("iso-20", "ISO 20 ft Container", "6.06 × 2.44 m prefab unit.", ModuleShapeKind.IsoContainer, 6.058, 2.438, 2.39, 2300, AddEndConnectors: true),
        new("iso-40", "ISO 40 ft Container", "12.19 × 2.44 m prefab unit.", ModuleShapeKind.IsoContainer, 12.192, 2.438, 2.39, 3750, AddEndConnectors: true),
        new("iso-40hc", "ISO 40 ft High Cube", "12.19 × 2.44 m, 2.9 m tall prefab unit.", ModuleShapeKind.IsoContainer, 12.192, 2.438, 2.69, 3900, AddEndConnectors: true),
        new("rect", "Rectangle", "Custom rectangular building.", ModuleShapeKind.Rectangle, 10, 6, 2.7, 0),
        new("round", "Round / Dome", "Circular footprint made of four curved walls.", ModuleShapeKind.Round, 8, 8, 3.0, 0),
        new("capsule", "Capsule", "Straight sides with semicircular ends.", ModuleShapeKind.Capsule, 12, 4, 2.6, 0),
        new("hex", "Hexagon", "Six-sided pod.", ModuleShapeKind.RegularPolygon, 7, 7, 2.7, 0, Sides: 6),
        new("oct", "Octagon", "Eight-sided pod.", ModuleShapeKind.RegularPolygon, 8, 8, 2.7, 0, Sides: 8),
    ];

    public static ModuleTemplate Get(string key) => All.First(t => t.Key == key);

    /// <summary>Builds a module from a template. <paramref name="length"/>/<paramref name="width"/> override the template size.</summary>
    public static ModuleDefinition Create(ModuleTemplate t, string? name = null, double? length = null, double? width = null)
    {
        var l = length ?? t.Length;
        var w = width ?? t.Width;
        var m = new ModuleDefinition
        {
            Name = name ?? t.Name,
            WallHeight = t.WallHeight,
            StructureMassKg = t.StructureMassKg > 0 ? t.StructureMassKg : EstimateStructureMass(t.Shape, l, w),
        };

        switch (t.Shape)
        {
            case ModuleShapeKind.IsoContainer:
            case ModuleShapeKind.Rectangle:
                AddRectangle(m, l, w);
                break;
            case ModuleShapeKind.Round:
                AddRound(m, l / 2);
                break;
            case ModuleShapeKind.Capsule:
                AddCapsule(m, l, w);
                break;
            case ModuleShapeKind.RegularPolygon:
                AddRegularPolygon(m, t.Sides, l / 2);
                break;
        }

        if (t.AddEndConnectors && m.Shell.Count == 4)
        {
            // Rectangle vertices run counterclockwise from the bottom-left, so edges 1 and 3 are the short ends.
            AddConnectorWithDoor(m, m.Shell[1], w / 2, "East end");
            AddConnectorWithDoor(m, m.Shell[3], w / 2, "West end");
        }
        return m;
    }

    public static void AddConnectorWithDoor(ModuleDefinition m, ShellVertex edgeStart, double offset, string name)
    {
        m.Connectors.Add(new ModuleConnector { Name = name, EdgeId = edgeStart.Id, Offset = offset, Width = 1.2 });
        m.Openings.Add(Opening.Create(OpeningKind.Door, edgeStart.Id, offset));
    }

    private static void AddRectangle(ModuleDefinition m, double l, double w)
    {
        m.Shell.Add(new ShellVertex(-l / 2, -w / 2));
        m.Shell.Add(new ShellVertex(l / 2, -w / 2));
        m.Shell.Add(new ShellVertex(l / 2, w / 2));
        m.Shell.Add(new ShellVertex(-l / 2, w / 2));
    }

    private static void AddRound(ModuleDefinition m, double r)
    {
        var quarter = Math.Tan(Math.PI / 8); // bulge for a 90° counterclockwise arc
        m.Shell.Add(new ShellVertex(r, 0, quarter));
        m.Shell.Add(new ShellVertex(0, r, quarter));
        m.Shell.Add(new ShellVertex(-r, 0, quarter));
        m.Shell.Add(new ShellVertex(0, -r, quarter));
    }

    private static void AddCapsule(ModuleDefinition m, double l, double w)
    {
        var r = w / 2;
        var a = Math.Max(0, l / 2 - r);
        m.Shell.Add(new ShellVertex(-a, -r));
        m.Shell.Add(new ShellVertex(a, -r, 1));
        m.Shell.Add(new ShellVertex(a, r));
        m.Shell.Add(new ShellVertex(-a, r, 1));
    }

    private static void AddRegularPolygon(ModuleDefinition m, int sides, double circumRadius)
    {
        sides = Math.Max(3, sides);
        // Rotate so one flat side faces south, which reads naturally on the plan.
        var offset = -Math.PI / 2 - Math.PI / sides;
        for (var i = 0; i < sides; i++)
        {
            var p = Vec2.FromAngle(offset + 2 * Math.PI * i / sides, circumRadius);
            m.Shell.Add(new ShellVertex(p.X, p.Y));
        }
    }

    /// <summary>Rough insulated-panel structure estimate (~120 kg per m² of floor) for non-container shapes.</summary>
    private static double EstimateStructureMass(ModuleShapeKind shape, double l, double w)
    {
        var area = shape switch
        {
            ModuleShapeKind.Round => Math.PI * l * l / 4,
            ModuleShapeKind.Capsule => (l - w) * w + Math.PI * w * w / 4,
            ModuleShapeKind.RegularPolygon => 0.8 * l * w,
            ModuleShapeKind.Blank => 0,
            _ => l * w,
        };
        return Math.Round(area * 120 / 10) * 10;
    }
}
