using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Modules;
using DexStationPlanner.Core.Stations;

namespace DexStationPlanner.Core.Globe;

/// <summary>
/// What the globe page draws for one station. All shapes are in true east/north meters relative to the
/// site origin (site-grid heading already applied); the page converts them with Cesium's
/// east-north-up frame at the origin, which stays correct at the poles.
/// Heights are meters above local ground.
/// </summary>
public sealed record GlobeScene(
    Guid StationId,
    string Name,
    bool IsPlaced,
    double Latitude,
    double Longitude,
    double GroundElevation,
    IReadOnlyList<GlobeVolume> Volumes,
    IReadOnlyList<GlobeLine> Lines);

/// <summary>
/// An extruded footprint (module, corridor) or, when <see cref="Top"/> equals <see cref="Bottom"/>,
/// a flat shape draped on the ground (runway, helipad, yard, fuel farm). Kind is "module", "corridor" or a site feature kind.
/// </summary>
public sealed record GlobeVolume(string Id, string Label, string Kind, string Color, double[][] Ring, double Bottom, double Top);

/// <summary>A line on the ground. <see cref="WidthM"/> &gt; 0 draws a corridor of that real width (roads); 0 draws a thin line.</summary>
public sealed record GlobeLine(string Id, string Label, string Kind, string Color, double[][] Points, double WidthM = 0);

public static class GlobeSceneBuilder
{
    public const double CorridorHeight = 2.4;

    public static readonly IReadOnlyDictionary<UtilityKind, string> UtilityColors = new Dictionary<UtilityKind, string>
    {
        [UtilityKind.Power] = "#F2C230",
        [UtilityKind.Fuel] = "#E0663A",
        [UtilityKind.Water] = "#3A9BE0",
        [UtilityKind.Wastewater] = "#8C6D3F",
        [UtilityKind.Heat] = "#E03A6B",
        [UtilityKind.Data] = "#3AE08A",
    };

    public static GlobeScene Build(Station station, StationProject project)
    {
        Func<Guid, ModuleDefinition?> resolve = project.FindModule;
        double[] ToEnu(Vec2 grid)
        {
            var p = Geodesy.GridToEnu(grid, station.Site.GridHeadingDegrees);
            return [Math.Round(p.X, 3), Math.Round(p.Y, 3)];
        }

        var volumes = new List<GlobeVolume>();
        foreach (var inst in station.Modules)
        {
            var def = resolve(inst.DefinitionId);
            if (def is null || !ModuleGeometry.HasClosedShell(def)) continue;
            var elevation = inst.ElevationOverride ?? def.ElevationAboveGround;
            var ring = SiteLayout.WorldFootprint(inst, def).Select(ToEnu).ToArray();
            // Short tags keep labels readable when modules sit close together on the globe.
            var label = string.IsNullOrWhiteSpace(inst.Tag) ? def.Name : inst.Tag;
            volumes.Add(new GlobeVolume(inst.Id.ToString(), label, "module", def.Color, ring,
                elevation, elevation + def.FloorThickness + def.WallHeight));
        }

        foreach (var link in station.Links)
        {
            if (SiteLayout.GetLinkGeometry(station, resolve, link) is not { Corridor: { } corridor }) continue;
            var a = station.Modules.First(m => m.Id == link.ModuleA);
            var b = station.Modules.First(m => m.Id == link.ModuleB);
            var floor = Math.Min(FloorTop(a, resolve), FloorTop(b, resolve));
            volumes.Add(new GlobeVolume(link.Id.ToString(), "Corridor", "corridor", "#7D8791",
                corridor.Select(ToEnu).ToArray(), Math.Max(0, floor - 0.2), floor + CorridorHeight));
        }

        var lines = new List<GlobeLine>();
        foreach (var run in station.UtilityRuns)
        {
            if (SiteLayout.UtilityRoute(station, run) is not { } route) continue;
            var label = string.IsNullOrWhiteSpace(run.Label) ? run.Kind.ToString() : run.Label;
            lines.Add(new GlobeLine(run.Id.ToString(), label, run.Kind.ToString(), UtilityColors[run.Kind],
                route.Select(ToEnu).ToArray()));
        }

        foreach (var f in station.Features)
        {
            var label = string.IsNullOrWhiteSpace(f.Name) ? f.Kind.ToString() : f.Name;
            if (f.Kind == SiteFeatureKind.Road)
            {
                if (f.Path.Count >= 2)
                    lines.Add(new GlobeLine(f.Id.ToString(), label, "Road", FeatureColor(f), f.Path.Select(ToEnu).ToArray(), Math.Max(1, f.Width)));
                continue;
            }
            // All area infrastructure is draped flat on the ground (a fuel farm is a bermed area, not one big block).
            volumes.Add(new GlobeVolume(f.Id.ToString(), label, f.Kind.ToString(), FeatureColor(f),
                FeatureGeometry.Outline(f)!.Select(ToEnu).ToArray(), 0, 0));
        }

        var site = station.Site;
        return new GlobeScene(station.Id, station.Name, site.IsPlaced, site.Latitude, site.Longitude,
            site.GroundElevation, volumes, lines);
    }

    /// <summary>Display color for a feature, by kind and surface. Shared by the site plan and the globe.</summary>
    public static string FeatureColor(SiteFeature f) => f.Kind switch
    {
        SiteFeatureKind.Runway or SiteFeatureKind.Road or SiteFeatureKind.Helipad => f.Surface switch
        {
            SurfaceType.Paved => "#4A4F57",
            SurfaceType.Gravel => "#9C8B70",
            SurfaceType.Snow => "#E6EEF5",
            SurfaceType.BlueIce => "#8FC3E8",
            _ => "#3A7CC0",
        },
        SiteFeatureKind.Port => "#2F6FA8",
        SiteFeatureKind.FuelFarm => "#E0663A",
        SiteFeatureKind.CargoYard => "#B08A3E",
        _ => "#3FBF7F",
    };

    private static double FloorTop(ModuleInstance inst, Func<Guid, ModuleDefinition?> resolve)
    {
        var def = resolve(inst.DefinitionId);
        if (def is null) return 0;
        return (inst.ElevationOverride ?? def.ElevationAboveGround) + def.FloorThickness;
    }
}
