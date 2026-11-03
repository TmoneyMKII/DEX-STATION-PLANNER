using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using DexStationPlanner.Core.Geometry;

namespace DexStationPlanner.Core.Model;

/// <summary>
/// A station: module instances laid out on a local site grid anchored at <see cref="Site"/>.
/// Site coordinates are meters, X = grid east, Y = grid north.
/// </summary>
public sealed class Station : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set => SetProperty(ref field, value); } = "New Station";
    public string Description { get; set => SetProperty(ref field, value); } = "";
    public SiteEnvironment Environment { get; set => SetProperty(ref field, value); } = SiteEnvironment.Polar;
    public SiteLocation Site { get; init; } = new();

    public ObservableCollection<ModuleInstance> Modules { get; init; } = [];
    public ObservableCollection<ConnectorLink> Links { get; init; } = [];
    public ObservableCollection<UtilityRun> UtilityRuns { get; init; } = [];
}

/// <summary>Where the station's site grid origin sits on Earth.</summary>
public sealed class SiteLocation : ObservableObject
{
    /// <summary>Has the user placed the station on the globe yet?</summary>
    public bool IsPlaced { get; set => SetProperty(ref field, value); }

    public double Latitude { get; set => SetProperty(ref field, Math.Clamp(value, -90, 90)); }
    public double Longitude { get; set => SetProperty(ref field, value); }

    /// <summary>Ground elevation above the WGS84 ellipsoid, used when no terrain is loaded.</summary>
    public double GroundElevation { get; set => SetProperty(ref field, value); }

    /// <summary>Clockwise rotation of site-grid north from true north, in degrees.</summary>
    public double GridHeadingDegrees { get; set => SetProperty(ref field, value); }

    public string SiteName { get; set => SetProperty(ref field, value); } = "";
}

public sealed class ModuleInstance : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DefinitionId { get; set => SetProperty(ref field, value); }

    /// <summary>Short site label, e.g. "B-1".</summary>
    public string Tag { get; set => SetProperty(ref field, value); } = "";

    public Vec2 Position { get; set => SetProperty(ref field, value); }
    public double RotationDegrees { get; set => SetProperty(ref field, value); }

    /// <summary>Overrides the module definition's elevation above ground when set.</summary>
    public double? ElevationOverride { get; set => SetProperty(ref field, value); }

    [JsonIgnore]
    public Pose2 Pose => new(Position, RotationDegrees);
}

/// <summary>Joins two module connectors, either directly (mated) or through a corridor.</summary>
public sealed class ConnectorLink : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ModuleA { get; set => SetProperty(ref field, value); }
    public Guid ConnectorA { get; set => SetProperty(ref field, value); }
    public Guid ModuleB { get; set => SetProperty(ref field, value); }
    public Guid ConnectorB { get; set => SetProperty(ref field, value); }

    /// <summary>Corridor clear width when the connectors are apart. Ignored for mated links.</summary>
    public double CorridorWidth { get; set => SetProperty(ref field, value); } = 1.5;
}

/// <summary>A utility line (power, fuel, water, ...) routed between two modules.</summary>
public sealed class UtilityRun : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public UtilityKind Kind { get; set => SetProperty(ref field, value); }
    public Guid FromModule { get; set => SetProperty(ref field, value); }
    public Guid ToModule { get; set => SetProperty(ref field, value); }

    /// <summary>Intermediate route points in site coordinates.</summary>
    public ObservableCollection<Vec2> Waypoints { get; init; } = [];

    public string Label { get; set => SetProperty(ref field, value); } = "";
    public string Notes { get; set => SetProperty(ref field, value); } = "";
}
