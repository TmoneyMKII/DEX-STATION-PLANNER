using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;

namespace DexStationPlanner.Core.Modules;

/// <summary>Example modules that show off the editor and give new projects something to place.</summary>
public static class StarterLibrary
{
    public static IEnumerable<ModuleDefinition> Create()
    {
        yield return Berthing20();
        yield return Lab40();
        yield return Galley40();
        yield return Power20();
        yield return Corridor6();
        yield return RecDome();
    }

    private static ModuleDefinition Berthing20()
    {
        var m = ModuleTemplates.Create(ModuleTemplates.Get("iso-20"), "Berthing 20 ft (4 berths)");
        m.Category = ModuleCategory.Berthing;
        m.Color = "#4C7FA8";
        m.Description = "Four-person sleeping module with desks and lockers.";
        Add(m, "berth.double-stack", -1.9, 0.62, 90);
        Add(m, "berth.double-stack", 1.9, 0.62, 90);
        Add(m, "berth.wardrobe", -0.45, 0.82);
        Add(m, "berth.wardrobe", 0.45, 0.82);
        Add(m, "berth.desk", -2.2, -0.82);
        Add(m, "berth.desk", 2.2, -0.82);
        Add(m, "safety.extinguisher", 0, -1.0);
        return m;
    }

    private static ModuleDefinition Lab40()
    {
        var m = ModuleTemplates.Create(ModuleTemplates.Get("iso-40"), "Science Lab 40 ft");
        m.Category = ModuleCategory.Laboratory;
        m.Color = "#8A6BBE";
        m.Description = "Wet/dry lab with fume hood, sample freezer and instrument racks.";
        Add(m, "lab.bench", -4.5, 0.75);
        Add(m, "lab.bench", -2.6, 0.75);
        Add(m, "lab.bench", -0.7, 0.75);
        Add(m, "lab.fume-hood", 1.3, 0.7);
        Add(m, "lab.ult-freezer", 2.7, 0.69);
        Add(m, "lab.instrument-rack", -5.0, -0.84, 90);
        Add(m, "lab.instrument-rack", -3.9, -0.84, 90);
        Add(m, "lab.balance-table", -2.6, -0.84);
        Add(m, "lab.glovebox", 1.0, -0.74);
        Add(m, "office.desk", 3.5, -0.74);
        Add(m, "safety.extinguisher", 5.6, 1.0);
        var north = m.Shell[2];
        m.Openings.Add(Opening.Create(OpeningKind.Window, north.Id, 8.7));
        m.Openings.Add(Opening.Create(OpeningKind.Window, north.Id, 10.6));
        return m;
    }

    private static ModuleDefinition Galley40()
    {
        var m = ModuleTemplates.Create(ModuleTemplates.Get("iso-40"), "Galley & Mess 40 ft");
        m.Category = ModuleCategory.Galley;
        m.Color = "#C27C3A";
        m.Description = "Kitchen and dining for 12.";
        Add(m, "galley.counter", -4.5, 0.79);
        Add(m, "galley.range", -3.1, 0.74);
        Add(m, "galley.dishwasher", -2.3, 0.81);
        Add(m, "galley.fridge", -1.5, 0.74);
        Add(m, "galley.freezer", -0.7, 0.74);
        Add(m, "galley.table-6", 1.5, -0.69);
        Add(m, "galley.table-6", 3.6, -0.69);
        Add(m, "safety.extinguisher", -5.6, -1.0);
        m.InteriorWalls.Add(new InteriorWall { Start = new Vec2(0.2, 1.219), End = new Vec2(0.2, 0.2) });
        return m;
    }

    private static ModuleDefinition Power20()
    {
        var m = ModuleTemplates.Create(ModuleTemplates.Get("iso-20"), "Power Plant 20 ft");
        m.Category = ModuleCategory.Power;
        m.Color = "#E0B23A";
        m.Description = "100 kW prime power with switchgear and day tank.";
        foreach (var c in m.Connectors) c.Kind = ConnectorKind.Utility;
        Add(m, "power.genset-100", -1.0, 0);
        Add(m, "power.switchgear", 1.9, 0.84);
        Add(m, "power.fuel-day-tank", 1.9, -0.69);
        Add(m, "safety.extinguisher", 0.9, 1.0);
        m.Openings.Add(Opening.Create(OpeningKind.Vent, m.Shell[0].Id, 1.5));
        m.Openings.Add(Opening.Create(OpeningKind.Vent, m.Shell[2].Id, 1.5));
        return m;
    }

    private static ModuleDefinition Corridor6()
    {
        var m = ModuleTemplates.Create(ModuleTemplates.Get("rect"), "Corridor 6 m", 6, 2);
        m.Category = ModuleCategory.Corridor;
        m.Color = "#7D8791";
        m.Description = "Enclosed walkway between modules.";
        m.WallHeight = 2.3;
        ModuleTemplates.AddConnectorWithDoor(m, m.Shell[1], 1.0, "East end");
        ModuleTemplates.AddConnectorWithDoor(m, m.Shell[3], 1.0, "West end");
        ModuleTemplates.AddConnectorWithDoor(m, m.Shell[0], 3.0, "South side");
        return m;
    }

    private static ModuleDefinition RecDome()
    {
        var m = ModuleTemplates.Create(ModuleTemplates.Get("round"), "Recreation Dome", 8, 8);
        m.Category = ModuleCategory.Recreation;
        m.Color = "#A35F8E";
        m.Description = "Round lounge and fitness space with curved walls.";
        // Shell[3] is the arc from the south vertex to the east vertex; its midpoint faces south-east.
        var arcLen = new Segment(m.Shell[3].Position, m.Shell[0].Position, m.Shell[3].Bulge).Length;
        ModuleTemplates.AddConnectorWithDoor(m, m.Shell[3], arcLen / 2, "South-east");
        Add(m, "rec.lounge", -1.5, 2.0);
        Add(m, "rec.lounge", 1.5, 2.0);
        Add(m, "rec.gym", -1.8, -1.0, 90);
        Add(m, "rec.gym", 0.0, -1.0, 90);
        Add(m, "office.table-8", 2.0, -0.2, 90);
        m.Openings.Add(Opening.Create(OpeningKind.Window, m.Shell[0].Id, arcLen / 2));
        m.Openings.Add(Opening.Create(OpeningKind.Window, m.Shell[1].Id, arcLen / 2));
        m.Openings.Add(Opening.Create(OpeningKind.Window, m.Shell[2].Id, arcLen / 2));
        return m;
    }

    private static void Add(ModuleDefinition m, string id, double x, double y, double rotation = 0) =>
        m.Equipment.Add(new EquipmentPlacement { DefinitionId = id, Position = new Vec2(x, y), RotationDegrees = rotation });
}
