using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DexStationPlanner.App.Editors;
using DexStationPlanner.App.Services;
using DexStationPlanner.Core.Equipment;
using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Logistics;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Modules;
using DexStationPlanner.Core.Stations;
using DexStationPlanner.Core.Units;

namespace DexStationPlanner.App.ViewModels;

/// <summary>
/// Base for property-panel editors. Wraps a model object, exposes lengths in display units, and refreshes
/// all bindings when the model (or the unit system) changes.
/// </summary>
public abstract class ElementEditor : ObservableObject, IDisposable
{
    private readonly List<INotifyPropertyChanged> _sources = [];

    protected static UnitContext Units => UnitContext.Instance;

    public abstract string Title { get; }
    public string LengthUnit => Units.LengthUnit;

    protected void Watch(params INotifyPropertyChanged?[] sources)
    {
        foreach (var s in sources)
        {
            if (s is null) continue;
            s.PropertyChanged += OnSourceChanged;
            _sources.Add(s);
        }
    }

    protected ElementEditor()
    {
        Watch(Units);
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(string.Empty);

    /// <summary>Re-reads every bound property (e.g. after a deep change the editor doesn't watch).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    protected static double D(double meters) => Math.Round(Units.ToDisplay(meters), 4);
    protected static double M(double display) => Units.FromDisplay(display);

    public void Dispose()
    {
        foreach (var s in _sources) s.PropertyChanged -= OnSourceChanged;
        _sources.Clear();
        GC.SuppressFinalize(this);
    }
}

public sealed class ModulePropertiesEditor(ModuleDefinition module, Func<ModuleStats> stats) : ElementEditor
{
    public override string Title => "Module";
    public ModuleDefinition Module { get; } = module;
    public IReadOnlyList<ModuleCategory> Categories { get; } = Enum.GetValues<ModuleCategory>();

    public double WallHeight { get => D(Module.WallHeight); set => Module.WallHeight = Math.Max(0.5, M(value)); }
    public double ShellThickness { get => D(Module.ShellThickness); set => Module.ShellThickness = Math.Clamp(M(value), 0.02, 1); }
    public double FloorThickness { get => D(Module.FloorThickness); set => Module.FloorThickness = Math.Clamp(M(value), 0.02, 1); }
    public double Elevation { get => D(Module.ElevationAboveGround); set => Module.ElevationAboveGround = Math.Max(0, M(value)); }

    public double StructureMass
    {
        get => Math.Round(Units.System == UnitSystem.Metric ? Module.StructureMassKg : Module.StructureMassKg / UnitFormatter.KgPerPound);
        set => Module.StructureMassKg = Math.Max(0, Units.System == UnitSystem.Metric ? value : value * UnitFormatter.KgPerPound);
    }

    public string MassUnit => Units.System == UnitSystem.Metric ? "kg" : "lb";
    public ModuleStats Stats => stats();

    public ModulePropertiesEditor Init()
    {
        Watch(Module);
        return this;
    }
}

public sealed class VertexEditor : ElementEditor
{
    public VertexEditor(ShellVertex vertex)
    {
        Vertex = vertex;
        Watch(vertex);
    }

    public override string Title => "Shell corner";
    public ShellVertex Vertex { get; }
    public double X { get => D(Vertex.Position.X); set => Vertex.Position = new Vec2(M(value), Vertex.Position.Y); }
    public double Y { get => D(Vertex.Position.Y); set => Vertex.Position = new Vec2(Vertex.Position.X, M(value)); }
}

/// <summary>Arc angle in degrees (signed) is friendlier than raw bulge; bulge = tan(angle / 4).</summary>
internal static class ArcAngle
{
    public static double FromBulge(double bulge) => Math.Round(4 * Math.Atan(bulge) * 180 / Math.PI, 2);
    public static double ToBulge(double degrees) => Math.Tan(Math.Clamp(degrees, -180, 180) * Math.PI / 180 / 4);
}

public sealed class EdgeEditor : ElementEditor
{
    private readonly ModuleDefinition _module;

    public EdgeEditor(EdgeSelection edge, ModuleDefinition module)
    {
        Edge = edge;
        _module = module;
        Watch(edge.Start);
        Watch(module.Shell.Select(v => (INotifyPropertyChanged)v).ToArray());
        StraightenCommand = new RelayCommand(() => Edge.Start.Bulge = 0);
    }

    public override string Title => "Shell wall";
    public EdgeSelection Edge { get; }
    private Segment? Segment => ModuleGeometry.FindEdge(_module, Edge.Start.Id)?.Segment;

    public string Length => Segment is { } s ? UnitFormatter.Dimension(s.Length, Units.System) : "–";
    public string Chord => Segment is { } s ? UnitFormatter.Dimension(s.ChordLength, Units.System) : "–";
    public string Radius => Segment is { IsArc: true } s ? UnitFormatter.Dimension(s.Radius, Units.System) : "straight";

    public double ArcDegrees { get => ArcAngle.FromBulge(Edge.Start.Bulge); set => Edge.Start.Bulge = ArcAngle.ToBulge(value); }
    public IRelayCommand StraightenCommand { get; }
}

public sealed class WallEditor : ElementEditor
{
    public WallEditor(InteriorWall wall)
    {
        Wall = wall;
        Watch(wall);
    }

    public override string Title => "Interior wall";
    public InteriorWall Wall { get; }
    public double StartX { get => D(Wall.Start.X); set => Wall.Start = new Vec2(M(value), Wall.Start.Y); }
    public double StartY { get => D(Wall.Start.Y); set => Wall.Start = new Vec2(Wall.Start.X, M(value)); }
    public double EndX { get => D(Wall.End.X); set => Wall.End = new Vec2(M(value), Wall.End.Y); }
    public double EndY { get => D(Wall.End.Y); set => Wall.End = new Vec2(Wall.End.X, M(value)); }
    public double Thickness { get => D(Wall.Thickness); set => Wall.Thickness = Math.Clamp(M(value), 0.02, 1); }
    public double ArcDegrees { get => ArcAngle.FromBulge(Wall.Bulge); set => Wall.Bulge = ArcAngle.ToBulge(value); }
    public string Length => UnitFormatter.Dimension(Wall.Segment.Length, Units.System);
}

public sealed class OpeningEditor : ElementEditor
{
    private readonly ModuleDefinition _module;

    public OpeningEditor(Opening opening, ModuleDefinition module)
    {
        Opening = opening;
        _module = module;
        Watch(opening);
    }

    public override string Title => Opening.Kind.ToString();
    public Opening Opening { get; }
    public IReadOnlyList<OpeningKind> Kinds { get; } = Enum.GetValues<OpeningKind>();
    public OpeningKind Kind { get => Opening.Kind; set => Opening.Kind = value; }

    private double HostLength => ModuleGeometry.FindHostSegment(_module, Opening.HostId)?.Length ?? 0;

    public double Offset
    {
        get => D(Opening.Offset);
        set => Opening.Offset = Math.Clamp(M(value), Opening.Width / 2, Math.Max(Opening.Width / 2, HostLength - Opening.Width / 2));
    }

    public double Width { get => D(Opening.Width); set => Opening.Width = Math.Clamp(M(value), 0.1, Math.Max(0.1, HostLength)); }
    public double Height { get => D(Opening.Height); set => Opening.Height = Math.Max(0.1, M(value)); }
    public double SillHeight { get => D(Opening.SillHeight); set => Opening.SillHeight = Math.Max(0, M(value)); }
}

public sealed class ConnectorEditor : ElementEditor
{
    private readonly ModuleDefinition _module;

    public ConnectorEditor(ModuleConnector connector, ModuleDefinition module)
    {
        Connector = connector;
        _module = module;
        Watch(connector);
    }

    public override string Title => "Connector";
    public ModuleConnector Connector { get; }
    public IReadOnlyList<ConnectorKind> Kinds { get; } = Enum.GetValues<ConnectorKind>();
    private double EdgeLength => ModuleGeometry.FindEdge(_module, Connector.EdgeId)?.Segment.Length ?? 0;

    public double Offset
    {
        get => D(Connector.Offset);
        set => Connector.Offset = Math.Clamp(M(value), Connector.Width / 2, Math.Max(Connector.Width / 2, EdgeLength - Connector.Width / 2));
    }

    public double Width { get => D(Connector.Width); set => Connector.Width = Math.Clamp(M(value), 0.3, Math.Max(0.3, EdgeLength)); }
}

public sealed class EquipmentEditor : ElementEditor
{
    public EquipmentEditor(EquipmentPlacement item, EquipmentDefinition? definition)
    {
        Item = item;
        Definition = definition;
        Watch(item);
    }

    public override string Title => Definition?.Name ?? $"Unknown equipment ({Item.DefinitionId})";
    public EquipmentPlacement Item { get; }
    public EquipmentDefinition? Definition { get; }
    public double X { get => D(Item.Position.X); set => Item.Position = new Vec2(M(value), Item.Position.Y); }
    public double Y { get => D(Item.Position.Y); set => Item.Position = new Vec2(Item.Position.X, M(value)); }
    public double Rotation { get => Item.RotationDegrees; set => Item.RotationDegrees = Pose2.NormalizeDegrees(value); }
    public double MountHeight { get => D(Item.MountHeight); set => Item.MountHeight = Math.Max(0, M(value)); }

    public string Size => Definition is { } d
        ? $"{UnitFormatter.Dimension(d.Width, Units.System)} × {UnitFormatter.Dimension(d.Depth, Units.System)} × {UnitFormatter.Dimension(d.Height, Units.System)}"
        : "–";

    public string Mass => Definition is { } d ? UnitFormatter.Mass(d.MassKg, Units.System) : "–";

    public string Power => Definition is { } d
        ? d.PowerSupplyW > 0 ? $"supplies {UnitFormatter.Power(d.PowerSupplyW)}" : UnitFormatter.Power(d.PowerDemandW)
        : "–";
}

public sealed class StationPropertiesEditor : ElementEditor
{
    public StationPropertiesEditor(Station station, Func<StationStats> stats)
    {
        Station = station;
        _stats = stats;
        Watch(station, station.Site);
    }

    private readonly Func<StationStats> _stats;
    public override string Title => "Station";
    public Station Station { get; }
    public IReadOnlyList<SiteEnvironment> Environments { get; } = Enum.GetValues<SiteEnvironment>();
    public StationStats Stats => _stats();
}

public sealed class InstanceEditor : ElementEditor
{
    public InstanceEditor(ModuleInstance instance, ModuleDefinition? definition)
    {
        Instance = instance;
        Definition = definition;
        Watch(instance, definition);
    }

    public override string Title => Definition?.Name ?? "Missing module";
    public ModuleInstance Instance { get; }
    public ModuleDefinition? Definition { get; }
    public double X { get => D(Instance.Position.X); set => Instance.Position = new Vec2(M(value), Instance.Position.Y); }
    public double Y { get => D(Instance.Position.Y); set => Instance.Position = new Vec2(Instance.Position.X, M(value)); }
    public double Rotation { get => Instance.RotationDegrees; set => Instance.RotationDegrees = Pose2.NormalizeDegrees(value); }

    public bool OverrideElevation
    {
        get => Instance.ElevationOverride.HasValue;
        set => Instance.ElevationOverride = value ? Instance.ElevationOverride ?? Definition?.ElevationAboveGround ?? 0 : null;
    }

    public double Elevation
    {
        get => D(Instance.ElevationOverride ?? Definition?.ElevationAboveGround ?? 0);
        set => Instance.ElevationOverride = Math.Max(0, M(value));
    }
}

public sealed class LinkEditor : ElementEditor
{
    private readonly Station _station;
    private readonly Func<Guid, ModuleDefinition?> _resolve;

    public LinkEditor(ConnectorLink link, Station station, Func<Guid, ModuleDefinition?> resolve)
    {
        Link = link;
        _station = station;
        _resolve = resolve;
        Watch(link);
        Watch(station.Modules.Select(m => (INotifyPropertyChanged)m).ToArray());
    }

    public ConnectorLink Link { get; }
    private LinkGeometry? Geometry => SiteLayout.GetLinkGeometry(_station, _resolve, Link);
    public override string Title => Geometry?.IsMated == true ? "Mated connection" : "Corridor";
    public bool IsCorridor => Geometry is { IsMated: false };
    public string Length => Geometry is { } g ? UnitFormatter.Dimension(g.Gap, Units.System) : "–";

    public string Between
    {
        get
        {
            string Name(Guid id) => _station.Modules.FirstOrDefault(m => m.Id == id)?.Tag ?? "?";
            return $"{Name(Link.ModuleA)} ↔ {Name(Link.ModuleB)}";
        }
    }

    public double CorridorWidth { get => D(Link.CorridorWidth); set => Link.CorridorWidth = Math.Clamp(M(value), 0.6, 10); }
}

/// <summary>One vehicle's verdict for a feature or station.</summary>
public sealed record AccessRow(string Vehicle, string Mode, bool Ok, string Reason);

public sealed class FeatureEditor : ElementEditor
{
    private const double LitersPerUsGallon = 3.785411784;
    private readonly Station _station;
    private readonly VehicleLookup _vehicles;

    public FeatureEditor(SiteFeature feature, Station station, VehicleLookup vehicles)
    {
        Feature = feature;
        _station = station;
        _vehicles = vehicles;
        Watch(feature, station.Site);
        feature.Path.CollectionChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    public SiteFeature Feature { get; }

    public override string Title => Feature.Kind switch
    {
        SiteFeatureKind.Runway => Feature.Surface is SurfaceType.Snow or SurfaceType.BlueIce ? "Skiway / ice runway" : "Runway",
        SiteFeatureKind.Port => "Port / landing",
        SiteFeatureKind.Road => "Road / track",
        SiteFeatureKind.FuelFarm => "Fuel farm",
        SiteFeatureKind.CargoYard => "Cargo yard",
        SiteFeatureKind.ScienceSite => "Science site",
        _ => "Helipad",
    };

    public bool IsRunway => Feature.Kind == SiteFeatureKind.Runway;
    public bool IsPort => Feature.Kind == SiteFeatureKind.Port;
    public bool IsRoad => Feature.Kind == SiteFeatureKind.Road;
    public bool IsFuelFarm => Feature.Kind == SiteFeatureKind.FuelFarm;
    public bool HasSurface => Feature.Kind is SiteFeatureKind.Runway or SiteFeatureKind.Helipad or SiteFeatureKind.Road;
    public bool HasPosition => !IsRoad;
    public bool HasLength => !IsRoad;
    public bool HasWidth => Feature.Kind is SiteFeatureKind.Runway or SiteFeatureKind.Port or SiteFeatureKind.Road
                                         or SiteFeatureKind.FuelFarm or SiteFeatureKind.CargoYard;
    public bool HasRotation => HasWidth && !IsRoad;

    public IReadOnlyList<SurfaceType> Surfaces { get; } = [SurfaceType.Paved, SurfaceType.Gravel, SurfaceType.Snow, SurfaceType.BlueIce];
    public IReadOnlyList<PortKind> PortKinds { get; } = Enum.GetValues<PortKind>();

    public string LengthLabel => Feature.Kind switch
    {
        SiteFeatureKind.Helipad => "Pad diameter",
        SiteFeatureKind.ScienceSite => "Zone diameter",
        SiteFeatureKind.Port => "Berth length",
        _ => "Length",
    };

    public double X { get => D(Feature.Position.X); set => Feature.Position = new Vec2(M(value), Feature.Position.Y); }
    public double Y { get => D(Feature.Position.Y); set => Feature.Position = new Vec2(Feature.Position.X, M(value)); }
    public double Length { get => D(Feature.Length); set => Feature.Length = Math.Max(1, M(value)); }
    public double Width { get => D(Feature.Width); set => Feature.Width = Math.Max(0.5, M(value)); }
    public double Rotation { get => Feature.RotationDegrees; set => Feature.RotationDegrees = Pose2.NormalizeDegrees(value); }
    public double WaterDepth { get => D(Feature.WaterDepth); set => Feature.WaterDepth = Math.Max(0, M(value)); }

    public string CapacityUnit => Units.System == UnitSystem.Metric ? "L" : "US gal";

    public double Capacity
    {
        get => Math.Round(Units.System == UnitSystem.Metric ? Feature.CapacityLiters : Feature.CapacityLiters / LitersPerUsGallon);
        set => Feature.CapacityLiters = Math.Max(0, Units.System == UnitSystem.Metric ? value : value * LitersPerUsGallon);
    }

    public string Designation => FeatureGeometry.RunwayDesignation(Feature, _station.Site.GridHeadingDegrees);
    public string RoadLength => UnitFormatter.Length(FeatureGeometry.RoadLength(Feature), Units.System, 0);

    public string DistanceFromCamp =>
        UnitFormatter.Length(IsRoad ? (Feature.Path.Count > 0 ? Feature.Path.Min(p => p.Length) : 0) : Feature.Position.Length, Units.System, 0);

    public IReadOnlyList<AccessRow> Access =>
        Compatibility.FeatureAccess(Feature, _vehicles.All(), _station.Site.GroundElevation, Units.System)
            .Select(c => new AccessRow(c.Vehicle.Name, c.Vehicle.Mode.ToString(), c.Ok, c.Reason))
            .OrderByDescending(r => r.Ok)
            .ToList();

    public bool HasAccess => Access.Count > 0;
}

public sealed class UtilityRunEditor : ElementEditor
{
    private readonly Station _station;

    public UtilityRunEditor(UtilityRun run, Station station)
    {
        Run = run;
        _station = station;
        Watch(run);
        run.Waypoints.CollectionChanged += (_, _) => OnPropertyChanged(string.Empty);
        ClearWaypointsCommand = new RelayCommand(() => Run.Waypoints.Clear());
    }

    public override string Title => "Utility run";
    public UtilityRun Run { get; }
    public IReadOnlyList<UtilityKind> Kinds { get; } = Enum.GetValues<UtilityKind>();

    public string Length => SiteLayout.UtilityRoute(_station, Run) is { } r
        ? UnitFormatter.Length(SiteLayout.PolylineLength(r), Units.System, 1)
        : "–";

    public string Between
    {
        get
        {
            string Name(Guid id) => _station.Modules.FirstOrDefault(m => m.Id == id)?.Tag ?? "?";
            return $"{Name(Run.FromModule)} → {Name(Run.ToModule)}";
        }
    }

    public IRelayCommand ClearWaypointsCommand { get; }
}
