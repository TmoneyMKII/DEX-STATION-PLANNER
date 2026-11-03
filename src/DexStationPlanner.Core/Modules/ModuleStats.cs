using DexStationPlanner.Core.Equipment;
using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;

namespace DexStationPlanner.Core.Modules;

/// <summary>Summary figures for a module design, in SI units.</summary>
public sealed record ModuleStats(
    double FloorAreaM2,
    double PerimeterM,
    double InteriorVolumeM3,
    double FootprintLengthM,
    double FootprintWidthM,
    double StructureMassKg,
    double EquipmentMassKg,
    double PowerDemandW,
    double PowerSupplyW,
    int Berths,
    int Workstations,
    int EquipmentCount,
    int MissingEquipmentCount)
{
    public double TotalMassKg => StructureMassKg + EquipmentMassKg;

    public static ModuleStats Compute(ModuleDefinition m, EquipmentLookup catalog)
    {
        var loop = ModuleGeometry.ShellLoop(m);
        var area = m.Shell.Count >= 3 ? Math.Abs(Polygon.SignedArea(loop)) : 0;
        var perimeter = m.Shell.Count >= 3 ? Polygon.Perimeter(loop) : 0;
        var (min, max) = Polygon.Bounds(ModuleGeometry.Footprint(m));

        double mass = 0, demand = 0, supply = 0;
        int berths = 0, seats = 0, missing = 0;
        foreach (var item in m.Equipment)
        {
            var def = catalog.Find(item.DefinitionId);
            if (def is null) { missing++; continue; }
            mass += def.MassKg;
            demand += def.PowerDemandW;
            supply += def.PowerSupplyW;
            berths += def.Berths;
            seats += def.Workstations;
        }

        return new ModuleStats(
            area, perimeter, area * m.WallHeight,
            Math.Max(max.X - min.X, max.Y - min.Y), Math.Min(max.X - min.X, max.Y - min.Y),
            m.StructureMassKg, mass, demand, supply, berths, seats, m.Equipment.Count, missing);
    }
}
