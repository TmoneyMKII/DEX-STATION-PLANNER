using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DexStationPlanner.Core.Geometry;

namespace DexStationPlanner.Core.Model;

public enum SiteFeatureKind { Runway, Helipad, Port, Road, FuelFarm, CargoYard, ScienceSite }

/// <summary>Surface of a runway, helipad or road. Determines which aircraft and vehicles can use it.</summary>
public enum SurfaceType { Paved, Gravel, Snow, BlueIce, Water }

public enum PortKind { Pier, IcePier, BeachLanding }

/// <summary>
/// Infrastructure placed on the site: airfields and skiways, helipads, ports, roads and tracks, fuel farms,
/// cargo yards and remote science sites. Positions are site-grid meters (often kilometers from the station).
/// Which properties matter depends on <see cref="Kind"/>; see <see cref="SiteFeatureDefaults"/>.
/// </summary>
public sealed class SiteFeature : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public SiteFeatureKind Kind { get; set => SetProperty(ref field, value); }
    public string Name { get; set => SetProperty(ref field, value); } = "";
    public string Notes { get; set => SetProperty(ref field, value); } = "";

    /// <summary>Center of the feature (unused for roads, which use <see cref="Path"/>).</summary>
    public Vec2 Position { get; set => SetProperty(ref field, value); }

    /// <summary>Direction of the long axis, degrees counterclockwise from site-grid east (like module rotation).</summary>
    public double RotationDegrees { get; set => SetProperty(ref field, value); }

    /// <summary>Runway/pier/yard length, road width, or helipad and science-site diameter.</summary>
    public double Length { get; set => SetProperty(ref field, value); }

    public double Width { get; set => SetProperty(ref field, value); }
    public SurfaceType Surface { get; set => SetProperty(ref field, value); }

    /// <summary>Port: water depth alongside, meters.</summary>
    public double WaterDepth { get; set => SetProperty(ref field, value); }

    public PortKind PortKind { get; set => SetProperty(ref field, value); }

    /// <summary>Fuel farm: storage capacity, liters.</summary>
    public double CapacityLiters { get; set => SetProperty(ref field, value); }

    /// <summary>Road centerline in site-grid meters.</summary>
    public ObservableCollection<Vec2> Path { get; init; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public Pose2 Pose => new(Position, RotationDegrees);
}

/// <summary>Sensible starting values for new features, tuned to the station's environment.</summary>
public static class SiteFeatureDefaults
{
    public static SiteFeature Create(SiteFeatureKind kind, SiteEnvironment environment, Vec2 position)
    {
        var f = new SiteFeature { Kind = kind, Position = position };
        var polar = environment == SiteEnvironment.Polar;
        switch (kind)
        {
            case SiteFeatureKind.Runway:
                f.Name = polar ? "Skiway" : "Airstrip";
                f.Surface = environment switch
                {
                    SiteEnvironment.Polar => SurfaceType.Snow,
                    SiteEnvironment.Temperate => SurfaceType.Paved,
                    _ => SurfaceType.Gravel,
                };
                f.Length = polar ? 3000 : 1500;
                f.Width = polar ? 60 : 30;
                break;
            case SiteFeatureKind.Helipad:
                f.Name = "Helipad";
                f.Surface = polar ? SurfaceType.Snow : SurfaceType.Gravel;
                f.Length = 30; // diameter of the landing area
                break;
            case SiteFeatureKind.Port:
                f.Name = polar ? "Ice pier" : "Pier";
                f.PortKind = polar ? PortKind.IcePier : PortKind.Pier;
                f.Length = 150;
                f.Width = 20;
                f.WaterDepth = 10;
                f.Surface = SurfaceType.Water;
                break;
            case SiteFeatureKind.Road:
                f.Name = polar ? "Groomed track" : "Road";
                f.Surface = polar ? SurfaceType.Snow : SurfaceType.Gravel;
                f.Width = polar ? 12 : 8;
                break;
            case SiteFeatureKind.FuelFarm:
                f.Name = "Fuel farm";
                f.Length = 40;
                f.Width = 25;
                f.CapacityLiters = 500_000;
                break;
            case SiteFeatureKind.CargoYard:
                f.Name = "Cargo yard";
                f.Length = 60;
                f.Width = 40;
                break;
            case SiteFeatureKind.ScienceSite:
                f.Name = "Science site";
                f.Length = 50; // diameter of the clear zone
                break;
        }
        return f;
    }

    public static bool IsLinear(SiteFeatureKind kind) => kind == SiteFeatureKind.Road;

    /// <summary>Runways and piers are placed by their two ends.</summary>
    public static bool IsTwoPoint(SiteFeatureKind kind) => kind is SiteFeatureKind.Runway or SiteFeatureKind.Port;

    /// <summary>Sets position, rotation and length from two end points (runway thresholds, pier root and head).</summary>
    public static void SetEnds(SiteFeature f, Vec2 a, Vec2 b)
    {
        f.Position = Vec2.Lerp(a, b, 0.5);
        f.Length = Math.Max(1, Vec2.Distance(a, b));
        f.RotationDegrees = Pose2.NormalizeDegrees((b - a).Angle * 180 / Math.PI);
    }
}
