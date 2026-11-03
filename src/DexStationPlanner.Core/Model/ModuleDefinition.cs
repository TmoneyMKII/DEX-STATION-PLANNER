using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DexStationPlanner.Core.Geometry;

namespace DexStationPlanner.Core.Model;

/// <summary>
/// A reusable module design (e.g. a 20 ft berthing container). The outer envelope is the
/// <see cref="Shell"/>: a closed loop of vertices whose edges may be straight or curved and
/// which defines the floor footprint. Interior partitions are free-standing <see cref="InteriorWalls"/>.
/// All coordinates are meters in the module's local plan frame.
/// </summary>
public sealed class ModuleDefinition : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set => SetProperty(ref field, value); } = "New Module";
    public string Description { get; set => SetProperty(ref field, value); } = "";
    public ModuleCategory Category { get; set => SetProperty(ref field, value); }

    /// <summary>Clear height from finished floor to ceiling/top of wall.</summary>
    public double WallHeight { get; set => SetProperty(ref field, value); } = 2.4;

    /// <summary>Thickness of the shell walls (centered on the shell line).</summary>
    public double ShellThickness { get; set => SetProperty(ref field, value); } = 0.15;

    public double FloorThickness { get; set => SetProperty(ref field, value); } = 0.2;

    /// <summary>Height of the underside of the floor above grade (stilts/piers when &gt; 0).</summary>
    public double ElevationAboveGround { get; set => SetProperty(ref field, value); }

    /// <summary>Mass of the empty structure (shell, frame, floor, finishes).</summary>
    public double StructureMassKg { get; set => SetProperty(ref field, value); }

    /// <summary>Tint used on the site plan and globe, as #RRGGBB.</summary>
    public string Color { get; set => SetProperty(ref field, value); } = "#B8C2CC";

    public string Notes { get; set => SetProperty(ref field, value); } = "";

    public ObservableCollection<ShellVertex> Shell { get; init; } = [];
    public ObservableCollection<InteriorWall> InteriorWalls { get; init; } = [];
    public ObservableCollection<Opening> Openings { get; init; } = [];
    public ObservableCollection<ModuleConnector> Connectors { get; init; } = [];
    public ObservableCollection<EquipmentPlacement> Equipment { get; init; } = [];
}

/// <summary>A shell corner. <see cref="Bulge"/> curves the edge from this vertex to the next one.</summary>
public sealed class ShellVertex : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Vec2 Position { get; set => SetProperty(ref field, value); }
    public double Bulge { get; set => SetProperty(ref field, value); }

    public ShellVertex() { }
    public ShellVertex(double x, double y, double bulge = 0)
    {
        Position = new Vec2(x, y);
        Bulge = bulge;
    }
}

public sealed class InteriorWall : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Vec2 Start { get; set => SetProperty(ref field, value); }
    public Vec2 End { get; set => SetProperty(ref field, value); }
    public double Bulge { get; set => SetProperty(ref field, value); }
    public double Thickness { get; set => SetProperty(ref field, value); } = 0.1;

    [System.Text.Json.Serialization.JsonIgnore]
    public Segment Segment => new(Start, End, Bulge);
}

/// <summary>
/// A door, window, hatch or vent cut into a wall. <see cref="HostId"/> is either a shell vertex
/// (meaning the edge starting at that vertex) or an interior wall.
/// </summary>
public sealed class Opening : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid HostId { get; set => SetProperty(ref field, value); }
    public OpeningKind Kind { get; set => SetProperty(ref field, value); }

    /// <summary>Distance along the host from its start to the opening's center.</summary>
    public double Offset { get; set => SetProperty(ref field, value); }

    public double Width { get; set => SetProperty(ref field, value); } = 0.9;
    public double Height { get; set => SetProperty(ref field, value); } = 2.0;
    public double SillHeight { get; set => SetProperty(ref field, value); }

    public static Opening Create(OpeningKind kind, Guid hostId, double offset) => kind switch
    {
        OpeningKind.Window => new Opening { Kind = kind, HostId = hostId, Offset = offset, Width = 0.9, Height = 0.6, SillHeight = 1.0 },
        OpeningKind.Hatch => new Opening { Kind = kind, HostId = hostId, Offset = offset, Width = 1.2, Height = 1.8, SillHeight = 0.2 },
        OpeningKind.Vent => new Opening { Kind = kind, HostId = hostId, Offset = offset, Width = 0.4, Height = 0.4, SillHeight = 1.8 },
        _ => new Opening { Kind = kind, HostId = hostId, Offset = offset, Width = 0.9, Height = 2.0, SillHeight = 0 },
    };
}

/// <summary>
/// A point on the shell where this module can join another module or a corridor on the site.
/// It faces outward from the shell edge that starts at <see cref="EdgeId"/>.
/// </summary>
public sealed class ModuleConnector : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set => SetProperty(ref field, value); } = "Connector";
    public Guid EdgeId { get; set => SetProperty(ref field, value); }
    public double Offset { get; set => SetProperty(ref field, value); }
    public double Width { get; set => SetProperty(ref field, value); } = 1.2;
    public ConnectorKind Kind { get; set => SetProperty(ref field, value); }
}
