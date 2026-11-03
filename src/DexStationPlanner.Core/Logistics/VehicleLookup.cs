using System.Text.Json;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Serialization;

namespace DexStationPlanner.Core.Logistics;

/// <summary>
/// Resolves vehicles against the built-in catalog plus a project's custom vehicles.
/// Custom entries with the same id as a built-in one take precedence.
/// </summary>
public sealed class VehicleLookup
{
    private static readonly Lazy<IReadOnlyList<VehicleDefinition>> BuiltInLazy = new(LoadBuiltIn);

    private readonly IEnumerable<VehicleDefinition> _custom;

    public VehicleLookup(IEnumerable<VehicleDefinition>? custom = null)
    {
        _custom = custom ?? [];
    }

    public static IReadOnlyList<VehicleDefinition> BuiltIn => BuiltInLazy.Value;

    public static VehicleLookup ForProject(StationProject project) => new(project.CustomVehicles);

    public VehicleDefinition? Find(string id) =>
        _custom.FirstOrDefault(v => v.Id == id) ?? BuiltIn.FirstOrDefault(v => v.Id == id);

    public IEnumerable<VehicleDefinition> All()
    {
        var custom = _custom.ToList();
        var overridden = custom.Select(c => c.Id).ToHashSet();
        return custom.Concat(BuiltIn.Where(b => !overridden.Contains(b.Id)));
    }

    private static IReadOnlyList<VehicleDefinition> LoadBuiltIn()
    {
        using var stream = typeof(VehicleLookup).Assembly
            .GetManifestResourceStream("DexStationPlanner.Core.Logistics.BuiltInVehicles.json")
            ?? throw new InvalidOperationException("Built-in vehicle catalog resource is missing.");
        var items = JsonSerializer.Deserialize<List<VehicleDefinition>>(stream, ProjectSerializer.Options) ?? [];
        foreach (var item in items) item.IsBuiltIn = true;
        return items;
    }
}
