using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Modules;

namespace DexStationPlanner.Core.Stations;

/// <summary>Builds new projects: an empty one with the starter module library, or a worked example station.</summary>
public static class ExampleProject
{
    public static StationProject CreateBlank(string name = "Untitled Project")
    {
        var project = new StationProject { Name = name };
        foreach (var m in StarterLibrary.Create()) project.Modules.Add(m);
        project.Stations.Add(new Station { Name = "Station 1" });
        return project;
    }

    /// <summary>A small elevated polar camp at Summit, Greenland, assembled from the starter modules.</summary>
    public static StationProject CreateExample()
    {
        var project = new StationProject
        {
            Name = "Example – Summit Field Camp",
            Organization = "Diversified Exploration Works",
            Description = "A worked example: a small elevated polar science camp assembled from the starter modules.",
        };
        foreach (var m in StarterLibrary.Create()) project.Modules.Add(m);
        ModuleDefinition Find(ModuleCategory c) => project.Modules.First(m => m.Category == c);

        var station = new Station
        {
            Name = "Summit Field Camp",
            Description = "Elevated modules on 1.5 m stilts so drifting snow passes underneath.",
            Environment = SiteEnvironment.Polar,
            Site = { IsPlaced = true, Latitude = 72.5796, Longitude = -38.4592, GroundElevation = 3216, SiteName = "Summit Station, Greenland" },
        };
        project.Stations.Add(station);

        var corridorDef = Find(ModuleCategory.Corridor);
        var corridor = Add(station, corridorDef, Pose2.Identity, "COR-1");

        var berthing = Attach(station, project, corridor, "West end", Find(ModuleCategory.Berthing), "East end", "BER-1");
        Attach(station, project, corridor, "East end", Find(ModuleCategory.Galley), "West end", "GAL-1");
        Attach(station, project, corridor, "South side", Find(ModuleCategory.Laboratory), "West end", "LAB-1");
        Attach(station, project, berthing, "West end", Find(ModuleCategory.Recreation), "South-east", "REC-1", gap: 6);

        var power = Add(station, Find(ModuleCategory.Power), new Pose2(new Vec2(30, 14), 0), "PWR-1");
        foreach (var inst in station.Modules) inst.ElevationOverride = 1.5;
        power.ElevationOverride = 0.5;

        var galley = station.Modules.First(m => m.Tag == "GAL-1");
        station.UtilityRuns.Add(new UtilityRun
        {
            Kind = UtilityKind.Power, FromModule = power.Id, ToModule = corridor.Id, Label = "Main feeder",
            Waypoints = { new Vec2(30, 6), new Vec2(0, 6) },
        });
        station.UtilityRuns.Add(new UtilityRun
        {
            Kind = UtilityKind.Fuel, FromModule = power.Id, ToModule = galley.Id, Label = "Galley fuel",
            Waypoints = { new Vec2(22, 14) },
        });

        // Infrastructure: a long groomed skiway south-east of camp, the track to it, fuel, cargo and a clean-air sector.
        var skiway = SiteFeatureDefaults.Create(SiteFeatureKind.Runway, station.Environment, Vec2.Zero);
        skiway.Name = "Skiway";
        skiway.Width = 60;
        SiteFeatureDefaults.SetEnds(skiway, new Vec2(-1400, -900), new Vec2(3000, -900));
        station.Features.Add(skiway);

        var track = SiteFeatureDefaults.Create(SiteFeatureKind.Road, station.Environment, Vec2.Zero);
        track.Name = "Skiway track";
        foreach (var p in new[] { new Vec2(10, -14), new Vec2(10, -300), new Vec2(200, -840) }) track.Path.Add(p);
        station.Features.Add(track);

        var fuel = SiteFeatureDefaults.Create(SiteFeatureKind.FuelFarm, station.Environment, new Vec2(60, 14));
        fuel.CapacityLiters = 400_000;
        station.Features.Add(fuel);

        station.Features.Add(SiteFeatureDefaults.Create(SiteFeatureKind.CargoYard, station.Environment, new Vec2(80, -60)));

        var cleanAir = SiteFeatureDefaults.Create(SiteFeatureKind.ScienceSite, station.Environment, new Vec2(-250, 600));
        cleanAir.Name = "Clean air sector";
        cleanAir.Length = 300;
        station.Features.Add(cleanAir);
        return project;
    }

    private static ModuleInstance Add(Station s, ModuleDefinition def, Pose2 pose, string tag)
    {
        var inst = new ModuleInstance { DefinitionId = def.Id, Position = pose.Position, RotationDegrees = pose.RotationDegrees, Tag = tag };
        s.Modules.Add(inst);
        return inst;
    }

    private static ModuleInstance Attach(Station s, StationProject p, ModuleInstance target, string targetConnector,
        ModuleDefinition def, string connector, string tag, double gap = 0)
    {
        var targetDef = p.FindModule(target.DefinitionId)!;
        var tc = targetDef.Connectors.First(c => c.Name == targetConnector);
        var mc = def.Connectors.First(c => c.Name == connector);
        var frame = SiteLayout.WorldConnector(target.Pose, targetDef, tc)!.Value;
        var pose = SiteLayout.MatePose(frame, def, mc, gap)!.Value;
        var inst = Add(s, def, pose, tag);
        s.Links.Add(new ConnectorLink { ModuleA = target.Id, ConnectorA = tc.Id, ModuleB = inst.Id, ConnectorB = mc.Id });
        return inst;
    }
}
