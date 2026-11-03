using DexStationPlanner.Core.Equipment;
using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Modules;

namespace DexStationPlanner.Core.Stations;

/// <summary>Station-wide totals, in SI units.</summary>
public sealed record StationStats(
    int ModuleCount,
    double FloorAreaM2,
    double TotalMassKg,
    double PowerDemandW,
    double PowerSupplyW,
    int Berths,
    int Workstations,
    int MatedLinks,
    int Corridors,
    double CorridorLengthM,
    IReadOnlyDictionary<UtilityKind, double> UtilityLengthM,
    double SiteLengthM,
    double SiteWidthM)
{
    /// <summary>Supply minus demand. Negative means the station can't power itself.</summary>
    public double PowerMarginW => PowerSupplyW - PowerDemandW;

    public static StationStats Compute(Station station, StationProject project)
    {
        var catalog = EquipmentLookup.ForProject(project);
        Func<Guid, ModuleDefinition?> resolve = project.FindModule;

        double area = 0, mass = 0, demand = 0, supply = 0;
        int berths = 0, seats = 0;
        var allPoints = new List<Vec2>();
        foreach (var inst in station.Modules)
        {
            var def = resolve(inst.DefinitionId);
            if (def is null) continue;
            var s = ModuleStats.Compute(def, catalog);
            area += s.FloorAreaM2;
            mass += s.TotalMassKg;
            demand += s.PowerDemandW;
            supply += s.PowerSupplyW;
            berths += s.Berths;
            seats += s.Workstations;
            allPoints.AddRange(SiteLayout.WorldFootprint(inst, def));
        }

        int mated = 0, corridors = 0;
        double corridorLen = 0;
        foreach (var link in station.Links)
        {
            if (SiteLayout.GetLinkGeometry(station, resolve, link) is not { } g) continue;
            if (g.IsMated) mated++;
            else { corridors++; corridorLen += g.Gap; }
        }

        var utility = Enum.GetValues<UtilityKind>().ToDictionary(k => k, _ => 0.0);
        foreach (var run in station.UtilityRuns)
            if (SiteLayout.UtilityRoute(station, run) is { } route)
                utility[run.Kind] += SiteLayout.PolylineLength(route);

        var (min, max) = Polygon.Bounds(allPoints);
        return new StationStats(
            station.Modules.Count, area, mass, demand, supply, berths, seats,
            mated, corridors, corridorLen, utility, max.X - min.X, max.Y - min.Y);
    }
}
