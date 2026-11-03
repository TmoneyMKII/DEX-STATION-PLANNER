using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DexStationPlanner.Core.Model;

/// <summary>The root document saved as a .dexstation file.</summary>
public sealed class StationProject : ObservableObject
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Name { get; set => SetProperty(ref field, value); } = "Untitled Project";
    public string Organization { get; set => SetProperty(ref field, value); } = "";
    public string Author { get; set => SetProperty(ref field, value); } = "";
    public string Description { get; set => SetProperty(ref field, value); } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public UnitSystem DisplayUnits { get; set => SetProperty(ref field, value); } = UnitSystem.Metric;

    /// <summary>Project-specific equipment added on top of the built-in catalog.</summary>
    public ObservableCollection<EquipmentDefinition> CustomEquipment { get; init; } = [];

    public ObservableCollection<ModuleDefinition> Modules { get; init; } = [];
    public ObservableCollection<Station> Stations { get; init; } = [];

    public ModuleDefinition? FindModule(Guid id) => Modules.FirstOrDefault(m => m.Id == id);
}
