using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Globe;
using DexStationPlanner.Core.Logistics;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Serialization;
using DexStationPlanner.Core.Stations;

namespace DexStationPlanner.Core.Tests;

public class GeodesyTests
{
    [Theory]
    [InlineData(72.5796, -38.4592)]   // Summit, Greenland
    [InlineData(-77.846, 166.676)]    // McMurdo
    [InlineData(19.821, -155.468)]    // Mauna Kea
    [InlineData(-89.999, 0)]          // near the South Pole
    public void Local_and_geodetic_round_trip(double lat, double lon)
    {
        var origin = new GeoPoint(lat, lon);
        foreach (var offset in new[] { new Vec2(1000, 0), new Vec2(0, -2500), new Vec2(-4000, 3000) })
        {
            var geo = Geodesy.FromLocal(origin, offset);
            var back = Geodesy.ToLocal(origin, geo);
            Assert.Equal(offset.X, back.X, 0.01);
            Assert.Equal(offset.Y, back.Y, 0.01);
        }
    }

    [Fact]
    public void One_kilometer_north_is_about_0_009_degrees_of_latitude()
    {
        var p = Geodesy.FromLocal(new GeoPoint(45, 10), new Vec2(0, 1000));
        Assert.Equal(45.00900, p.Latitude, 4);
        Assert.Equal(10, p.Longitude, 6);
    }

    [Fact]
    public void At_the_south_pole_north_points_along_the_origin_meridian()
    {
        // With origin longitude 0 at the pole, Cesium's ENU frame puts "north" toward 0° longitude.
        var p = Geodesy.FromLocal(new GeoPoint(-90, 0), new Vec2(0, 1000));
        Assert.Equal(0, p.Longitude, 3);
        Assert.True(p.Latitude > -90 && p.Latitude < -89.99);
    }

    [Fact]
    public void Great_circle_distance_matches_known_value()
    {
        // McMurdo to the South Pole is roughly 1,350 km.
        var d = Geodesy.GreatCircleDistance(new GeoPoint(-77.846, 166.676), new GeoPoint(-90, 0));
        Assert.InRange(d, 1_340_000, 1_360_000);
    }

    [Fact]
    public void Grid_heading_rotates_clockwise()
    {
        var enu = Geodesy.GridToEnu(new Vec2(0, 100), 90);
        Assert.Equal(100, enu.X, 1e-9);
        Assert.Equal(0, enu.Y, 1e-9);
        var grid = Geodesy.EnuToGrid(enu, 90);
        Assert.Equal(100, grid.Y, 1e-9);
    }
}

public class LogisticsTests
{
    private static VehicleDefinition V(string id) => VehicleLookup.BuiltIn.Single(v => v.Id == id);

    private static SiteFeature Runway(SurfaceType surface, double length, double width = 60) => new()
    {
        Kind = SiteFeatureKind.Runway, Name = "Test strip", Surface = surface, Length = length, Width = width,
    };

    [Fact]
    public void Catalog_has_the_requested_vehicles_with_unique_ids()
    {
        var all = VehicleLookup.BuiltIn;
        Assert.Equal(all.Count, all.Select(v => v.Id).Distinct().Count());
        Assert.Contains(all, v => v.Name.Contains("LC-130"));
        Assert.Contains(all, v => v.Name.Contains("Twin Otter"));
        Assert.Contains(all, v => v.Name.Contains("Basler"));
        Assert.Contains(all, v => v.Mode == TransportMode.Rotorcraft);
        Assert.Contains(all, v => v.Name.Contains("icebreaker", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, v => v.Mode == TransportMode.Overland);
        Assert.All(all.Where(v => v.Mode == TransportMode.FixedWing), v => Assert.NotEmpty(v.RunwayLengthM));
        Assert.All(all.Where(v => v.Mode == TransportMode.Rotorcraft), v => Assert.True(v.RotorDiameterM > 0));
        Assert.All(all.Where(v => v.Mode == TransportMode.Ship), v => Assert.True(v.DraftM > 0));
        Assert.All(all.Where(v => v.Mode == TransportMode.Overland), v => Assert.NotEmpty(v.RoadSurfaces));
    }

    [Fact]
    public void LC130_needs_a_long_skiway()
    {
        var lc130 = V("air.lc130h");
        Assert.False(Compatibility.Check(lc130, Runway(SurfaceType.Snow, 2400), 2000)!.Ok);
        Assert.Contains("3,000", Compatibility.Check(lc130, Runway(SurfaceType.Snow, 2400), 2000)!.Reason);
        Assert.True(Compatibility.Check(lc130, Runway(SurfaceType.Snow, 3600), 2000)!.Ok);
    }

    [Fact]
    public void C17_cannot_use_snow()
    {
        var check = Compatibility.Check(V("air.c17a"), Runway(SurfaceType.Snow, 5000), 0)!;
        Assert.False(check.Ok);
        Assert.Contains("groomed snow", check.Reason);
    }

    [Fact]
    public void Site_elevation_limit_applies()
    {
        var lc130 = V("air.lc130h");
        var check = Compatibility.Check(lc130, Runway(SurfaceType.Snow, 4000), 4000)!; // e.g. Dome A
        Assert.False(check.Ok);
        Assert.Contains("elevation", check.Reason);
        Assert.True(Compatibility.Check(V("air.basler-bt67"), Runway(SurfaceType.Snow, 4000), 4000)!.Ok);
    }

    [Fact]
    public void Helicopters_need_a_big_enough_pad_or_any_runway()
    {
        var pad = new SiteFeature { Kind = SiteFeatureKind.Helipad, Length = 15 };
        Assert.True(Compatibility.Check(V("heli.h125"), pad, 0)!.Ok);
        Assert.False(Compatibility.Check(V("heli.mi8"), pad, 0)!.Ok);
        Assert.True(Compatibility.Check(V("heli.mi8"), Runway(SurfaceType.Gravel, 800), 0)!.Ok);
    }

    [Fact]
    public void Ships_need_depth_and_berth_or_a_shallow_draft_for_beaches()
    {
        var pier = new SiteFeature { Kind = SiteFeatureKind.Port, PortKind = PortKind.Pier, Length = 120, WaterDepth = 8 };
        Assert.False(Compatibility.Check(V("ship.ice-class-cargo"), pier, 0)!.Ok); // 9.5 m draft + 1 m clearance
        Assert.True(Compatibility.Check(V("ship.research-supply"), pier, 0)!.Ok);

        var beach = new SiteFeature { Kind = SiteFeatureKind.Port, PortKind = PortKind.BeachLanding };
        Assert.True(Compatibility.Check(V("ship.landing-craft"), beach, 0)!.Ok);
        Assert.False(Compatibility.Check(V("ship.polar-icebreaker"), beach, 0)!.Ok);
    }

    [Fact]
    public void Overland_vehicles_need_a_matching_road()
    {
        var snowTrack = new SiteFeature { Kind = SiteFeatureKind.Road, Surface = SurfaceType.Snow, Width = 12 };
        Assert.True(Compatibility.Check(V("land.tractor-traverse"), snowTrack, 2800)!.Ok);
        Assert.False(Compatibility.Check(V("land.6x6-truck"), snowTrack, 2800)!.Ok);
    }

    [Fact]
    public void Irrelevant_features_are_skipped()
    {
        Assert.Null(Compatibility.Check(V("ship.landing-craft"), Runway(SurfaceType.Paved, 3000), 0));
    }

    [Fact]
    public void Station_access_reports_missing_infrastructure()
    {
        var station = new Station();
        var results = Compatibility.StationAccess(station, VehicleLookup.BuiltIn);
        Assert.All(results, r => Assert.False(r.Ok));
        Assert.Contains(results, r => r.Reason.Contains("No runway or skiway"));
    }

    [Fact]
    public void Example_camp_is_reachable_by_ski_aircraft_and_traverse_but_not_ships()
    {
        var project = ExampleProject.CreateExample();
        var station = project.Stations.Single();
        var access = Compatibility.StationAccess(station, VehicleLookup.BuiltIn).ToDictionary(a => a.Vehicle.Id);
        Assert.True(access["air.lc130h"].Ok);
        Assert.True(access["air.dhc6-twin-otter"].Ok);
        Assert.True(access["land.tractor-traverse"].Ok);
        Assert.False(access["air.c17a"].Ok);
        Assert.False(access["ship.ice-class-cargo"].Ok);
    }

    [Fact]
    public void Imperial_messages_use_feet()
    {
        var check = Compatibility.Check(V("air.lc130h"), Runway(SurfaceType.Snow, 2400), 0, UnitSystem.Imperial)!;
        Assert.Contains("ft", check.Reason);
    }
}

public class FeatureTests
{
    [Fact]
    public void Two_point_placement_sets_center_length_and_rotation()
    {
        var f = new SiteFeature { Kind = SiteFeatureKind.Runway, Width = 50 };
        SiteFeatureDefaults.SetEnds(f, new Vec2(0, 0), new Vec2(0, 3000));
        Assert.Equal(new Vec2(0, 1500), f.Position);
        Assert.Equal(3000, f.Length, 1e-9);
        Assert.Equal(90, f.RotationDegrees, 1e-9);
        var (a, b) = FeatureGeometry.Ends(f);
        Assert.Equal(0, a.Y, 1e-9);
        Assert.Equal(3000, b.Y, 1e-9);
    }

    [Theory]
    [InlineData(90, 0, "18/36")]   // runway along grid north, grid = true north
    [InlineData(0, 0, "09/27")]    // along grid east
    [InlineData(0, 30, "12/30")]   // grid rotated 30° clockwise
    public void Runway_designation_from_bearing(double rotation, double gridHeading, string expected)
    {
        var f = new SiteFeature { Kind = SiteFeatureKind.Runway, RotationDegrees = rotation };
        Assert.Equal(expected, FeatureGeometry.RunwayDesignation(f, gridHeading));
    }

    [Fact]
    public void Distance_to_area_and_road_features()
    {
        var yard = new SiteFeature { Kind = SiteFeatureKind.CargoYard, Position = Vec2.Zero, Length = 20, Width = 10 };
        Assert.Equal(0, FeatureGeometry.DistanceTo(yard, new Vec2(5, 2)), 1e-9);
        Assert.Equal(5, FeatureGeometry.DistanceTo(yard, new Vec2(15, 0)), 1e-9);

        var road = new SiteFeature { Kind = SiteFeatureKind.Road, Width = 6, Path = { new Vec2(0, 0), new Vec2(100, 0) } };
        Assert.Equal(7, FeatureGeometry.DistanceTo(road, new Vec2(50, 10)), 1e-9);
        Assert.Equal(100, FeatureGeometry.RoadLength(road), 1e-9);
    }

    [Fact]
    public void Features_appear_in_the_globe_scene()
    {
        var project = ExampleProject.CreateExample();
        var scene = GlobeSceneBuilder.Build(project.Stations[0], project);
        Assert.Contains(scene.Volumes, v => v.Kind == "Runway" && v.Top == v.Bottom);
        Assert.Contains(scene.Volumes, v => v.Kind == "FuelFarm" && v.Top == v.Bottom);
        Assert.Contains(scene.Volumes, v => v.Kind == "module" && v.Top > v.Bottom);
        Assert.Contains(scene.Lines, l => l.Kind == "Road" && l.WidthM > 0);
    }

    [Fact]
    public void Features_round_trip_and_version_1_files_still_load()
    {
        var project = ExampleProject.CreateExample();
        var json = ProjectSerializer.Serialize(project);
        var back = ProjectSerializer.Deserialize(json);
        Assert.Equal(json, ProjectSerializer.Serialize(back));
        Assert.Equal(project.Stations[0].Features.Count, back.Stations[0].Features.Count);

        var v1 = ProjectSerializer.Deserialize("{\"schemaVersion\": 1, \"name\": \"Old\", \"stations\": [{\"name\": \"S\"}]}");
        Assert.Empty(v1.Stations[0].Features);
        Assert.Equal(StationProject.CurrentSchemaVersion, v1.SchemaVersion);
    }
}
