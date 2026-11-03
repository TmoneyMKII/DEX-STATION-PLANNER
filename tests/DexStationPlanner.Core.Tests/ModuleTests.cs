using DexStationPlanner.Core.Equipment;
using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Modules;
using DexStationPlanner.Core.Rendering;

namespace DexStationPlanner.Core.Tests;

public class ModuleTests
{
    [Fact]
    public void Iso20_template_has_iso_footprint_and_end_connectors_facing_out()
    {
        var m = ModuleTemplates.Create(ModuleTemplates.Get("iso-20"));
        var stats = ModuleStats.Compute(m, new EquipmentLookup());
        Assert.Equal(6.058 * 2.438, stats.FloorAreaM2, 1e-9);
        Assert.Equal(2, m.Connectors.Count);

        var east = ModuleGeometry.ConnectorFrame(m, m.Connectors[0])!.Value;
        Assert.Equal(6.058 / 2, east.Position.X, 1e-9);
        Assert.Equal(0, east.Position.Y, 1e-9);
        Assert.Equal(1, east.Outward.X, 1e-9);

        var west = ModuleGeometry.ConnectorFrame(m, m.Connectors[1])!.Value;
        Assert.Equal(-1, west.Outward.X, 1e-9);
    }

    [Fact]
    public void Connector_faces_outward_even_when_shell_drawn_clockwise()
    {
        var m = new ModuleDefinition();
        m.Shell.Add(new ShellVertex(0, 0));
        m.Shell.Add(new ShellVertex(0, 2));
        m.Shell.Add(new ShellVertex(4, 2));
        m.Shell.Add(new ShellVertex(4, 0));
        // Edge 0 is the west wall (x = 0), so outward is -X.
        var c = new ModuleConnector { EdgeId = m.Shell[0].Id, Offset = 1 };
        m.Connectors.Add(c);
        var f = ModuleGeometry.ConnectorFrame(m, c)!.Value;
        Assert.Equal(-1, f.Outward.X, 1e-9);
        Assert.Equal(-1, ModuleGeometry.ShellWinding(m));
        Assert.True(Polygon.SignedArea(ModuleGeometry.Footprint(m)) > 0, "Footprint is normalized to CCW");
    }

    [Fact]
    public void Round_template_area_matches_circle()
    {
        var m = ModuleTemplates.Create(ModuleTemplates.Get("round"), length: 8);
        var stats = ModuleStats.Compute(m, new EquipmentLookup());
        Assert.Equal(Math.PI * 16, stats.FloorAreaM2, 1e-9);
    }

    [Fact]
    public void Capsule_template_area_is_rectangle_plus_circle()
    {
        var m = ModuleTemplates.Create(ModuleTemplates.Get("capsule"), length: 12, width: 4);
        var stats = ModuleStats.Compute(m, new EquipmentLookup());
        Assert.Equal(8 * 4 + Math.PI * 4, stats.FloorAreaM2, 1e-9);
    }

    [Fact]
    public void Built_in_catalog_loads_with_unique_ids()
    {
        var all = EquipmentLookup.BuiltIn;
        Assert.True(all.Count > 30);
        Assert.Equal(all.Count, all.Select(e => e.Id).Distinct().Count());
        Assert.All(all, e => Assert.True(e.Width > 0 && e.Depth > 0 && e.Height > 0, e.Id));
    }

    [Fact]
    public void Custom_equipment_overrides_built_in_with_same_id()
    {
        var custom = new EquipmentDefinition { Id = "berth.single", Name = "Custom bunk", Berths = 1, MassKg = 1 };
        var lookup = new EquipmentLookup([custom]);
        Assert.Same(custom, lookup.Find("berth.single"));
        Assert.Single(lookup.All(), e => e.Id == "berth.single");
    }

    [Fact]
    public void Starter_modules_reference_only_known_equipment_and_fit_inside_shell()
    {
        var lookup = new EquipmentLookup();
        foreach (var m in StarterLibrary.Create())
        {
            var footprint = ModuleGeometry.Footprint(m);
            foreach (var item in m.Equipment)
            {
                var def = lookup.Find(item.DefinitionId);
                Assert.True(def is not null, $"{m.Name}: unknown equipment {item.DefinitionId}");
                foreach (var corner in ModuleGeometry.EquipmentCorners(item, def!))
                    Assert.True(Polygon.Contains(footprint, corner), $"{m.Name}: {def!.Name} sticks out of the shell at {corner}");
            }
            Assert.All(m.Openings, o => Assert.NotNull(ModuleGeometry.FindHostSegment(m, o.HostId)));
        }
    }

    [Fact]
    public void Starter_berthing_module_totals()
    {
        var m = StarterLibrary.Create().First(x => x.Category == ModuleCategory.Berthing);
        var s = ModuleStats.Compute(m, new EquipmentLookup());
        Assert.Equal(4, s.Berths);
        Assert.Equal(2300 + 2 * 110 + 2 * 45 + 2 * 30 + 15, s.TotalMassKg, 1e-9);
        Assert.Equal(0, s.MissingEquipmentCount);
    }

    [Fact]
    public void Wall_split_cuts_door_and_window()
    {
        var door = new Opening { Offset = 1, Width = 1, Height = 2, SillHeight = 0 };
        var window = new Opening { Offset = 4, Width = 1, Height = 1, SillHeight = 1 };
        var pieces = WallPieces.Split(6, 2.5, [window, door]);
        Assert.Equal(
        [
            new WallPiece(0, 0.5, 0, 2.5),
            new WallPiece(0.5, 1.5, 2, 2.5),   // header over door
            new WallPiece(1.5, 3.5, 0, 2.5),
            new WallPiece(3.5, 4.5, 0, 1),     // sill under window
            new WallPiece(3.5, 4.5, 2, 2.5),   // header over window
            new WallPiece(4.5, 6, 0, 2.5),
        ], pieces);
    }

    [Fact]
    public void Wall_split_merges_overlapping_openings()
    {
        var a = new Opening { Offset = 1, Width = 1, Height = 2.5 };
        var b = new Opening { Offset = 1.2, Width = 1, Height = 2.5 };
        var pieces = WallPieces.Split(3, 2.5, [a, b]);
        Assert.Equal([new WallPiece(0, 0.5, 0, 2.5), new WallPiece(1.7, 3, 0, 2.5)], pieces);
    }

    [Fact]
    public void Mesh_builder_produces_floor_walls_stilts_and_equipment()
    {
        var m = StarterLibrary.Create().First();
        m.ElevationAboveGround = 1.5;
        var scene = ModuleMeshBuilder.Build(m, new EquipmentLookup());
        var names = scene.Parts.Select(p => p.Name).ToHashSet();
        Assert.Contains("Floor", names);
        Assert.Contains("Shell", names);
        Assert.Contains("Stilts", names);
        Assert.Contains("Equipment", names);
        Assert.All(scene.Parts, p => Assert.Equal(0, p.Indices.Count % 3));
        var floor = scene.Parts.First(p => p.Name == "Floor");
        Assert.Equal(1.5, floor.Positions.Min(v => v.Z), 1e-9);
    }

    [Fact]
    public void Clamp_attachments_keeps_openings_on_shortened_edge_and_drops_orphans()
    {
        var m = ModuleTemplates.Create(ModuleTemplates.Get("rect"), length: 10, width: 6);
        var south = m.Shell[0];
        var door = Opening.Create(OpeningKind.Door, south.Id, 9);
        var orphan = Opening.Create(OpeningKind.Door, Guid.NewGuid(), 1);
        m.Openings.Add(door);
        m.Openings.Add(orphan);
        m.Shell[1].Position = new Vec2(0, -3); // south edge now 5 m long
        ModuleGeometry.ClampAttachments(m);
        Assert.Equal(5 - door.Width / 2, door.Offset, 1e-9);
        Assert.DoesNotContain(orphan, m.Openings);
    }
}
