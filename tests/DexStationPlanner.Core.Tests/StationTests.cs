using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Globe;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Modules;
using DexStationPlanner.Core.Serialization;
using DexStationPlanner.Core.Stations;

namespace DexStationPlanner.Core.Tests;

public class StationTests
{
    private static (StationProject Project, Station Station, ModuleDefinition Def) TwoContainerSite()
    {
        var project = new StationProject();
        var def = ModuleTemplates.Create(ModuleTemplates.Get("iso-20"));
        project.Modules.Add(def);
        var station = new Station();
        project.Stations.Add(station);
        station.Modules.Add(new ModuleInstance { DefinitionId = def.Id, Position = Vec2.Zero, Tag = "A" });
        return (project, station, def);
    }

    [Fact]
    public void Snap_rotates_and_translates_so_connectors_mate()
    {
        var (project, station, def) = TwoContainerSite();
        var moving = new ModuleInstance { DefinitionId = def.Id, Tag = "B" };
        station.Modules.Add(moving);

        // Drop B a little east of A's east connector, rotated 170° (so B's east connector roughly faces A).
        var proposed = new Pose2(new Vec2(6.5, 0.3), 170);
        var snap = SiteLayout.FindSnap(station, project.FindModule, moving, proposed, threshold: 2.0);

        Assert.NotNull(snap);
        var a = station.Modules[0];
        var aEast = SiteLayout.WorldConnector(a, def, def.Connectors[0].Id)!.Value;
        var bConn = SiteLayout.WorldConnector(snap!.Pose, def, def.Connectors.First(c => c.Id == snap.MovingConnector))!.Value;
        Assert.Equal(aEast.Position.X, bConn.Position.X, 1e-6);
        Assert.Equal(aEast.Position.Y, bConn.Position.Y, 1e-6);
        Assert.Equal(-1, Vec2.Dot(aEast.Outward, bConn.Outward), 1e-6);
        // Containers end up colinear: B's center sits 6.058 m east of A's.
        Assert.Equal(6.058, snap.Pose.Position.X, 1e-6);
        Assert.Equal(0, snap.Pose.Position.Y, 1e-6);
    }

    [Fact]
    public void Snap_ignores_connectors_out_of_range()
    {
        var (project, station, def) = TwoContainerSite();
        var moving = new ModuleInstance { DefinitionId = def.Id };
        station.Modules.Add(moving);
        Assert.Null(SiteLayout.FindSnap(station, project.FindModule, moving, new Pose2(new Vec2(30, 30), 0), 2.0));
    }

    [Fact]
    public void Snap_skips_connectors_already_linked_to_other_modules()
    {
        var (project, station, def) = TwoContainerSite();
        var moving = new ModuleInstance { DefinitionId = def.Id };
        station.Modules.Add(moving);
        // Moving module's west connector lands 0.2 m from A's east connector.
        var proposed = new Pose2(new Vec2(6.058 + 0.2, 0), 0);
        Assert.NotNull(SiteLayout.FindSnap(station, project.FindModule, moving, proposed, 0.5));

        // Once A's east connector is linked (by corridor) to a far-away module, it is no longer offered.
        var far = new ModuleInstance { DefinitionId = def.Id, Position = new Vec2(100, 0) };
        station.Modules.Add(far);
        station.Links.Add(new ConnectorLink
        {
            ModuleA = station.Modules[0].Id, ConnectorA = def.Connectors[0].Id,
            ModuleB = far.Id, ConnectorB = def.Connectors[1].Id,
        });
        Assert.Null(SiteLayout.FindSnap(station, project.FindModule, moving, proposed, 0.5));
    }

    [Fact]
    public void Link_geometry_reports_mated_or_corridor()
    {
        var (project, station, def) = TwoContainerSite();
        var b = new ModuleInstance { DefinitionId = def.Id, Position = new Vec2(16.058, 0) };
        station.Modules.Add(b);
        var link = new ConnectorLink
        {
            ModuleA = station.Modules[0].Id, ConnectorA = def.Connectors[0].Id,
            ModuleB = b.Id, ConnectorB = def.Connectors[1].Id, CorridorWidth = 2,
        };
        station.Links.Add(link);

        var g = SiteLayout.GetLinkGeometry(station, project.FindModule, link)!;
        Assert.False(g.IsMated);
        Assert.Equal(10, g.Gap, 1e-9);
        Assert.Equal(20, Math.Abs(Polygon.SignedArea(g.Corridor!)), 1e-9);

        b.Position = new Vec2(6.058, 0);
        g = SiteLayout.GetLinkGeometry(station, project.FindModule, link)!;
        Assert.True(g.IsMated);
        Assert.Null(g.Corridor);
    }

    [Fact]
    public void Station_stats_sum_modules_corridors_and_utilities()
    {
        var (project, station, def) = TwoContainerSite();
        var b = new ModuleInstance { DefinitionId = def.Id, Position = new Vec2(16.058, 0) };
        station.Modules.Add(b);
        station.Links.Add(new ConnectorLink
        {
            ModuleA = station.Modules[0].Id, ConnectorA = def.Connectors[0].Id,
            ModuleB = b.Id, ConnectorB = def.Connectors[1].Id,
        });
        station.UtilityRuns.Add(new UtilityRun
        {
            Kind = UtilityKind.Power, FromModule = station.Modules[0].Id, ToModule = b.Id,
            Waypoints = { new Vec2(0, 10), new Vec2(16.058, 10) },
        });

        var s = StationStats.Compute(station, project);
        Assert.Equal(2, s.ModuleCount);
        Assert.Equal(2 * 6.058 * 2.438, s.FloorAreaM2, 1e-9);
        Assert.Equal(1, s.Corridors);
        Assert.Equal(10, s.CorridorLengthM, 1e-9);
        Assert.Equal(10 + 16.058 + 10, s.UtilityLengthM[UtilityKind.Power], 1e-9);
        Assert.Equal(22.116, s.SiteLengthM, 1e-9);
    }

    [Fact]
    public void Removing_a_module_drops_its_links_and_runs()
    {
        var (project, station, def) = TwoContainerSite();
        var b = new ModuleInstance { DefinitionId = def.Id, Position = new Vec2(10, 0) };
        station.Modules.Add(b);
        station.Links.Add(new ConnectorLink { ModuleA = station.Modules[0].Id, ConnectorA = def.Connectors[0].Id, ModuleB = b.Id, ConnectorB = def.Connectors[1].Id });
        station.UtilityRuns.Add(new UtilityRun { FromModule = station.Modules[0].Id, ToModule = b.Id });
        station.Modules.Remove(b);
        SiteLayout.RemoveDanglingReferences(station, project.FindModule);
        Assert.Empty(station.Links);
        Assert.Empty(station.UtilityRuns);
    }

    [Fact]
    public void Next_tag_skips_used_numbers()
    {
        var station = new Station();
        station.Modules.Add(new ModuleInstance { Tag = "LAB-1" });
        station.Modules.Add(new ModuleInstance { Tag = "LAB-3" });
        Assert.Equal("LAB-2", SiteLayout.NextTag(station, ModuleCategory.Laboratory));
    }

    [Fact]
    public void Globe_scene_applies_grid_heading_clockwise()
    {
        var (project, station, def) = TwoContainerSite();
        station.Modules[0].Position = new Vec2(0, 100); // 100 m grid-north
        station.Site.GridHeadingDegrees = 90;           // grid north points true east
        var scene = GlobeSceneBuilder.Build(station, project);
        var ring = scene.Volumes.Single().Ring;
        var cx = ring.Average(p => p[0]);
        var cy = ring.Average(p => p[1]);
        Assert.Equal(100, cx, 1e-3);
        Assert.Equal(0, cy, 1e-3);
    }

    [Fact]
    public void Project_round_trips_through_json()
    {
        var project = new StationProject { Name = "Dome X", DisplayUnits = UnitSystem.Imperial };
        foreach (var m in StarterLibrary.Create()) project.Modules.Add(m);
        var (_, station, _) = TwoContainerSite();
        station.Modules[0].DefinitionId = project.Modules[0].Id;
        station.Site.Latitude = -75.1;
        station.Site.Longitude = 123.333;
        station.Site.IsPlaced = true;
        station.UtilityRuns.Add(new UtilityRun { Kind = UtilityKind.Fuel, Waypoints = { new Vec2(1.23456, -2) } });
        project.Stations.Add(station);

        var json = ProjectSerializer.Serialize(project);
        var back = ProjectSerializer.Deserialize(json);

        Assert.Equal(json, ProjectSerializer.Serialize(back));
        Assert.Equal(UnitSystem.Imperial, back.DisplayUnits);
        Assert.Equal(project.Modules[0].Id, back.Modules[0].Id);
        Assert.Equal(project.Modules[0].Shell[2].Position, back.Modules[0].Shell[2].Position);
        Assert.Equal(new Vec2(1.2346, -2), back.Stations[0].UtilityRuns[0].Waypoints[0]);
        Assert.Contains("\"kind\": \"Fuel\"", json);
    }

    [Fact]
    public void Example_project_modules_are_mated_and_do_not_overlap()
    {
        var project = ExampleProject.CreateExample();
        var station = project.Stations.Single();
        var links = station.Links.Select(l => SiteLayout.GetLinkGeometry(station, project.FindModule, l)!).ToList();
        Assert.Equal(4, links.Count);
        Assert.Equal(3, links.Count(l => l.IsMated));
        Assert.Equal(6, links.Single(l => !l.IsMated).Gap, 1e-6);

        // No module's footprint contains another module's center.
        foreach (var a in station.Modules)
        foreach (var b in station.Modules.Where(b => b != a))
            Assert.False(Polygon.Contains(SiteLayout.WorldFootprint(a, project.FindModule(a.DefinitionId)!), b.Position),
                $"{a.Tag} overlaps {b.Tag}");

        var stats = StationStats.Compute(station, project);
        Assert.True(stats.PowerMarginW > 0, "Example should be power-positive");
        Assert.Equal(4, stats.Berths);
    }

    [Fact]
    public void Blank_project_has_starter_modules_and_one_station()
    {
        var p = ExampleProject.CreateBlank();
        Assert.Equal(6, p.Modules.Count);
        Assert.Single(p.Stations);
    }

    [Fact]
    public void Newer_schema_is_rejected_with_clear_message()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize("{\"schemaVersion\": 99}"));
        Assert.Contains("newer version", ex.Message);
    }
}
