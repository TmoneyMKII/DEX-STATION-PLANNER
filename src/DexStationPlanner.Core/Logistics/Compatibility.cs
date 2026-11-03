using DexStationPlanner.Core.Units;
using DexStationPlanner.Core.Model;

namespace DexStationPlanner.Core.Logistics;

/// <summary>Whether a vehicle can use a feature (or reach the station at all), with a human-readable reason.</summary>
public sealed record AccessCheck(VehicleDefinition Vehicle, SiteFeature? Feature, bool Ok, string Reason);

/// <summary>
/// Planning-level checks of vehicles against site infrastructure: runway surface, length and width; helipad size;
/// port depth, berth length and beach landings; road surface and width; and site elevation limits.
/// </summary>
public static class Compatibility
{
    /// <summary>Minimum water under the keel at the berth, m.</summary>
    public const double UnderKeelClearanceM = 1.0;

    /// <summary>Largest draft that can run up onto a beach landing, m.</summary>
    public const double BeachLandingMaxDraftM = 2.5;

    /// <summary>A pier must be at least this fraction of a ship's length to berth it (mooring dolphins cover the rest).</summary>
    public const double MinBerthFraction = 0.6;

    public static string SurfaceName(SurfaceType s) => s switch
    {
        SurfaceType.Snow => "groomed snow",
        SurfaceType.BlueIce => "blue ice",
        SurfaceType.Paved => "paved",
        SurfaceType.Gravel => "gravel",
        _ => "water",
    };

    public static string ModeFeatureName(TransportMode mode) => mode switch
    {
        TransportMode.FixedWing => "runway or skiway",
        TransportMode.Rotorcraft => "helipad or runway",
        TransportMode.Ship => "port, pier or beach landing",
        _ => "road or track",
    };

    /// <summary>Checks one vehicle against one feature. Returns null when the feature is irrelevant to the vehicle's mode.</summary>
    public static AccessCheck? Check(VehicleDefinition v, SiteFeature f, double siteElevationM, UnitSystem units = UnitSystem.Metric)
    {
        var relevant = v.Mode switch
        {
            TransportMode.FixedWing => f.Kind == SiteFeatureKind.Runway,
            TransportMode.Rotorcraft => f.Kind is SiteFeatureKind.Helipad or SiteFeatureKind.Runway,
            TransportMode.Ship => f.Kind == SiteFeatureKind.Port,
            _ => f.Kind == SiteFeatureKind.Road,
        };
        if (!relevant) return null;

        AccessCheck Fail(string reason) => new(v, f, false, reason);
        string M(double meters, int decimals = 0) => UnitFormatter.Length(meters, units, decimals);
        var name = string.IsNullOrWhiteSpace(f.Name) ? f.Kind.ToString() : f.Name;

        if (v.Mode != TransportMode.Ship && siteElevationM > v.MaxSiteElevationM)
            return Fail($"Site elevation {M(siteElevationM)} is above its {M(v.MaxSiteElevationM)} operating limit.");

        switch (v.Mode)
        {
            case TransportMode.FixedWing:
            {
                if (!v.RunwayLengthM.TryGetValue(f.Surface, out var need))
                    return Fail($"Can't operate from {SurfaceName(f.Surface)} surfaces.");
                if (f.Length < need)
                    return Fail($"Needs {M(need)} of {SurfaceName(f.Surface)}; {name} is {M(f.Length)}.");
                if (f.Width < v.MinRunwayWidthM)
                    return Fail($"Needs {M(v.MinRunwayWidthM)} width; {name} is {M(f.Width)}.");
                return new(v, f, true, $"{name}: {M(f.Length)} {SurfaceName(f.Surface)} (needs {M(need)}).");
            }
            case TransportMode.Rotorcraft:
            {
                if (f.Kind == SiteFeatureKind.Runway)
                    return new(v, f, true, $"Can land on {name}.");
                if (f.Length < v.RotorDiameterM)
                    return Fail($"Landing area must be at least {M(v.RotorDiameterM)} across; {name} is {M(f.Length)}.");
                return new(v, f, true, $"{name}: {M(f.Length)} pad (needs {M(v.RotorDiameterM)}).");
            }
            case TransportMode.Ship:
            {
                if (f.PortKind == PortKind.BeachLanding)
                {
                    return v.DraftM <= BeachLandingMaxDraftM
                        ? new(v, f, true, $"Beach landing at {name}.")
                        : Fail($"Draft {M(v.DraftM, 1)} is too deep for a beach landing (max {M(BeachLandingMaxDraftM, 1)}).");
                }
                var depthNeeded = v.DraftM + UnderKeelClearanceM;
                if (f.WaterDepth < depthNeeded)
                    return Fail($"Needs {M(depthNeeded, 1)} of water alongside; {name} has {M(f.WaterDepth, 1)}.");
                var berth = v.LengthM * MinBerthFraction;
                if (f.Length < berth)
                    return Fail($"Needs a berth of about {M(berth)}; {name} is {M(f.Length)}.");
                return new(v, f, true, $"Berths at {name} ({M(f.WaterDepth, 1)} depth).");
            }
            default:
            {
                if (!v.RoadSurfaces.Contains(f.Surface))
                    return Fail($"Can't drive on {SurfaceName(f.Surface)}.");
                if (f.Width < v.MinRoadWidthM)
                    return Fail($"Needs {M(v.MinRoadWidthM)} width; {name} is {M(f.Width)}.");
                return new(v, f, true, $"Uses {name} ({SurfaceName(f.Surface)}).");
            }
        }
    }

    /// <summary>
    /// Best access result for each vehicle at a station: the first feature that works, otherwise the most
    /// informative failure, or a note that the station has no infrastructure of the right kind.
    /// </summary>
    public static IReadOnlyList<AccessCheck> StationAccess(Station station, IEnumerable<VehicleDefinition> vehicles, UnitSystem units = UnitSystem.Metric)
    {
        var results = new List<AccessCheck>();
        foreach (var v in vehicles)
        {
            var checks = station.Features
                .Select(f => Check(v, f, station.Site.GroundElevation, units))
                .OfType<AccessCheck>()
                .ToList();
            results.Add(checks.FirstOrDefault(c => c.Ok)
                ?? checks.FirstOrDefault()
                ?? new AccessCheck(v, null, false, $"No {ModeFeatureName(v.Mode)} on site."));
        }
        return results;
    }

    /// <summary>Every relevant vehicle checked against one feature.</summary>
    public static IReadOnlyList<AccessCheck> FeatureAccess(SiteFeature feature, IEnumerable<VehicleDefinition> vehicles, double siteElevationM, UnitSystem units = UnitSystem.Metric) =>
        vehicles.Select(v => Check(v, feature, siteElevationM, units)).OfType<AccessCheck>().ToList();
}
