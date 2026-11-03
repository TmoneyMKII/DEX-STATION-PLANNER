using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using DexStationPlanner.Core.Geometry;

namespace DexStationPlanner.Core.Model;

/// <summary>A catalog entry describing a piece of equipment or furniture. Dimensions in meters.</summary>
public sealed class EquipmentDefinition : ObservableObject
{
    public string Id { get; set => SetProperty(ref field, value); } = "";
    public string Name { get; set => SetProperty(ref field, value); } = "";
    public EquipmentCategory Category { get; set => SetProperty(ref field, value); }
    public string Description { get; set => SetProperty(ref field, value); } = "";
    public EquipmentShape Shape { get; set => SetProperty(ref field, value); }

    /// <summary>Size along the local X axis.</summary>
    public double Width { get; set => SetProperty(ref field, value); } = 1;

    /// <summary>Size along the local Y axis.</summary>
    public double Depth { get; set => SetProperty(ref field, value); } = 1;

    public double Height { get; set => SetProperty(ref field, value); } = 1;
    public double MassKg { get; set => SetProperty(ref field, value); }

    /// <summary>Average continuous electrical demand.</summary>
    public double PowerDemandW { get; set => SetProperty(ref field, value); }

    /// <summary>Electrical generation capacity (generators, PV, wind).</summary>
    public double PowerSupplyW { get; set => SetProperty(ref field, value); }

    /// <summary>Sleeping places provided.</summary>
    public int Berths { get; set => SetProperty(ref field, value); }

    /// <summary>Seated work positions (desks, lab benches) provided.</summary>
    public int Workstations { get; set => SetProperty(ref field, value); }

    /// <summary>Display color as #RRGGBB.</summary>
    public string Color { get; set => SetProperty(ref field, value); } = "#8899AA";

    [JsonIgnore]
    public bool IsBuiltIn { get; set; }

    public EquipmentDefinition Clone() => new()
    {
        Id = Id, Name = Name, Category = Category, Description = Description, Shape = Shape,
        Width = Width, Depth = Depth, Height = Height, MassKg = MassKg,
        PowerDemandW = PowerDemandW, PowerSupplyW = PowerSupplyW,
        Berths = Berths, Workstations = Workstations, Color = Color,
    };
}

/// <summary>An equipment item placed inside a module. Position is the footprint center.</summary>
public sealed class EquipmentPlacement : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DefinitionId { get; set => SetProperty(ref field, value); } = "";
    public Vec2 Position { get; set => SetProperty(ref field, value); }
    public double RotationDegrees { get; set => SetProperty(ref field, value); }

    /// <summary>Height of the item's base above the module floor (e.g. wall-mounted cabinets).</summary>
    public double MountHeight { get; set => SetProperty(ref field, value); }

    public string Label { get; set => SetProperty(ref field, value); } = "";

    [JsonIgnore]
    public Pose2 Pose => new(Position, RotationDegrees);
}
