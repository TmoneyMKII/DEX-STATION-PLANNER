using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Modules;

namespace DexStationPlanner.Core.Stations;

/// <summary>Result of snapping a moving module so one of its connectors mates with another module's connector.</summary>
public sealed record SnapResult(Pose2 Pose, Guid MovingConnector, Guid TargetModule, Guid TargetConnector, double Distance);

public sealed record LinkGeometry(Vec2 A, Vec2 B, Vec2 OutwardA, Vec2 OutwardB, double Gap, bool IsMated, IReadOnlyList<Vec2>? Corridor);

/// <summary>Site-level geometry: placing module instances, snapping connectors, links and utility routes.</summary>
public static class SiteLayout
{
    /// <summary>Connectors closer than this (meters) count as mated, with no corridor between them.</summary>
    public const double MatedTolerance = 0.05;

    public static List<Vec2> WorldFootprint(ModuleInstance inst, ModuleDefinition def)
    {
        var pose = inst.Pose;
        return ModuleGeometry.Footprint(def).Select(pose.Apply).ToList();
    }

    public static ConnectorFrame? WorldConnector(ModuleInstance inst, ModuleDefinition def, Guid connectorId)
    {
        var c = def.Connectors.FirstOrDefault(x => x.Id == connectorId);
        return c is null ? null : WorldConnector(inst.Pose, def, c);
    }

    public static ConnectorFrame? WorldConnector(Pose2 pose, ModuleDefinition def, ModuleConnector c)
    {
        if (ModuleGeometry.ConnectorFrame(def, c) is not { } local) return null;
        return new ConnectorFrame(pose.Apply(local.Position), pose.ApplyDirection(local.Outward));
    }

    /// <summary>
    /// Finds the best connector-to-connector snap for a module being dragged to <paramref name="proposed"/>.
    /// The returned pose rotates the module so the two connectors face each other and translates it so they touch.
    /// Connectors already used by a link (other than ones involving the moving module) are skipped.
    /// </summary>
    public static SnapResult? FindSnap(
        Station station,
        Func<Guid, ModuleDefinition?> resolve,
        ModuleInstance moving,
        Pose2 proposed,
        double threshold)
    {
        var movingDef = resolve(moving.DefinitionId);
        if (movingDef is null) return null;

        var used = UsedConnectors(station, exceptModule: moving.Id);
        SnapResult? best = null;

        foreach (var mc in movingDef.Connectors)
        {
            if (WorldConnector(proposed, movingDef, mc) is not { } mf) continue;
            foreach (var other in station.Modules)
            {
                if (other.Id == moving.Id) continue;
                var otherDef = resolve(other.DefinitionId);
                if (otherDef is null) continue;
                foreach (var oc in otherDef.Connectors)
                {
                    if (used.Contains((other.Id, oc.Id))) continue;
                    if (WorldConnector(other.Pose, otherDef, oc) is not { } of) continue;
                    var d = Vec2.Distance(mf.Position, of.Position);
                    if (d > threshold || (best is not null && d >= best.Distance)) continue;

                    if (MatePose(of, movingDef, mc) is not { } snapped) continue;
                    best = new SnapResult(snapped, mc.Id, other.Id, oc.Id, d);
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Pose that puts <paramref name="connector"/> of <paramref name="def"/> on <paramref name="target"/>,
    /// facing it, optionally pushed out along the target's outward direction by <paramref name="gap"/> meters.
    /// </summary>
    public static Pose2? MatePose(ConnectorFrame target, ModuleDefinition def, ModuleConnector connector, double gap = 0)
    {
        if (ModuleGeometry.ConnectorFrame(def, connector) is not { } local) return null;
        var rotation = (-target.Outward).Angle - local.Outward.Angle;
        var degrees = Pose2.NormalizeDegrees(Math.Round(rotation * 180 / Math.PI, 6));
        var rotated = local.Position.Rotate(degrees * Math.PI / 180);
        return new Pose2(target.Position + target.Outward * gap - rotated, degrees);
    }

    public static HashSet<(Guid Module, Guid Connector)> UsedConnectors(Station station, Guid? exceptModule = null)
    {
        var set = new HashSet<(Guid, Guid)>();
        foreach (var l in station.Links)
        {
            if (exceptModule is { } ex && (l.ModuleA == ex || l.ModuleB == ex)) continue;
            set.Add((l.ModuleA, l.ConnectorA));
            set.Add((l.ModuleB, l.ConnectorB));
        }
        return set;
    }

    public static LinkGeometry? GetLinkGeometry(Station station, Func<Guid, ModuleDefinition?> resolve, ConnectorLink link)
    {
        var a = station.Modules.FirstOrDefault(m => m.Id == link.ModuleA);
        var b = station.Modules.FirstOrDefault(m => m.Id == link.ModuleB);
        if (a is null || b is null) return null;
        var da = resolve(a.DefinitionId);
        var db = resolve(b.DefinitionId);
        if (da is null || db is null) return null;
        if (WorldConnector(a, da, link.ConnectorA) is not { } fa) return null;
        if (WorldConnector(b, db, link.ConnectorB) is not { } fb) return null;

        var gap = Vec2.Distance(fa.Position, fb.Position);
        var mated = gap <= MatedTolerance;
        IReadOnlyList<Vec2>? corridor = null;
        if (!mated)
        {
            var dir = (fb.Position - fa.Position) / gap;
            var half = dir.LeftNormal * (link.CorridorWidth / 2);
            corridor = [fa.Position - half, fb.Position - half, fb.Position + half, fa.Position + half];
        }
        return new LinkGeometry(fa.Position, fb.Position, fa.Outward, fb.Outward, gap, mated, corridor);
    }

    /// <summary>Route of a utility run: module A origin, waypoints, module B origin.</summary>
    public static List<Vec2>? UtilityRoute(Station station, UtilityRun run)
    {
        var a = station.Modules.FirstOrDefault(m => m.Id == run.FromModule);
        var b = station.Modules.FirstOrDefault(m => m.Id == run.ToModule);
        if (a is null || b is null) return null;
        var pts = new List<Vec2> { a.Position };
        pts.AddRange(run.Waypoints);
        pts.Add(b.Position);
        return pts;
    }

    public static double PolylineLength(IReadOnlyList<Vec2> pts)
    {
        double len = 0;
        for (var i = 1; i < pts.Count; i++) len += Vec2.Distance(pts[i - 1], pts[i]);
        return len;
    }

    /// <summary>Removes links and utility runs that reference a module or connector that no longer exists.</summary>
    public static void RemoveDanglingReferences(Station station, Func<Guid, ModuleDefinition?> resolve)
    {
        var instances = station.Modules.ToDictionary(m => m.Id);
        bool ConnectorExists(Guid module, Guid connector) =>
            instances.TryGetValue(module, out var inst)
            && resolve(inst.DefinitionId) is { } def
            && def.Connectors.Any(c => c.Id == connector);

        foreach (var l in station.Links.ToList())
            if (!ConnectorExists(l.ModuleA, l.ConnectorA) || !ConnectorExists(l.ModuleB, l.ConnectorB))
                station.Links.Remove(l);

        foreach (var r in station.UtilityRuns.ToList())
            if (!instances.ContainsKey(r.FromModule) || !instances.ContainsKey(r.ToModule))
                station.UtilityRuns.Remove(r);
    }

    /// <summary>Next free tag like "LAB-2" for a module of the given category.</summary>
    public static string NextTag(Station station, ModuleCategory category)
    {
        var prefix = category switch
        {
            ModuleCategory.Berthing => "BER",
            ModuleCategory.Laboratory => "LAB",
            ModuleCategory.Galley => "GAL",
            ModuleCategory.Hygiene => "HYG",
            ModuleCategory.Medical => "MED",
            ModuleCategory.Power => "PWR",
            ModuleCategory.Mechanical => "MEC",
            ModuleCategory.Storage => "STO",
            ModuleCategory.Workshop => "SHP",
            ModuleCategory.Communications => "COM",
            ModuleCategory.Office => "OFF",
            ModuleCategory.Recreation => "REC",
            ModuleCategory.Corridor => "COR",
            _ => "MOD",
        };
        var n = 1;
        while (station.Modules.Any(m => m.Tag == $"{prefix}-{n}")) n++;
        return $"{prefix}-{n}";
    }
}
