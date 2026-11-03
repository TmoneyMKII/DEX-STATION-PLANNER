using System.Text.Json;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Serialization;

namespace DexStationPlanner.Core.Equipment;

/// <summary>
/// Resolves equipment ids against the built-in catalog plus a project's custom equipment.
/// Custom entries with the same id as a built-in one take precedence.
/// </summary>
public sealed class EquipmentLookup
{
    private static readonly Lazy<IReadOnlyList<EquipmentDefinition>> BuiltInLazy = new(LoadBuiltIn);

    private readonly IEnumerable<EquipmentDefinition> _custom;

    public EquipmentLookup(IEnumerable<EquipmentDefinition>? custom = null)
    {
        _custom = custom ?? [];
    }

    public static IReadOnlyList<EquipmentDefinition> BuiltIn => BuiltInLazy.Value;

    public static EquipmentLookup ForProject(StationProject project) => new(project.CustomEquipment);

    public EquipmentDefinition? Find(string id) =>
        _custom.FirstOrDefault(e => e.Id == id) ?? BuiltIn.FirstOrDefault(e => e.Id == id);

    public IEnumerable<EquipmentDefinition> All()
    {
        var custom = _custom.ToList();
        var overridden = custom.Select(c => c.Id).ToHashSet();
        return custom.Concat(BuiltIn.Where(b => !overridden.Contains(b.Id)));
    }

    private static IReadOnlyList<EquipmentDefinition> LoadBuiltIn()
    {
        using var stream = typeof(EquipmentLookup).Assembly
            .GetManifestResourceStream("DexStationPlanner.Core.Equipment.BuiltInEquipment.json")
            ?? throw new InvalidOperationException("Built-in equipment catalog resource is missing.");
        var items = JsonSerializer.Deserialize<List<EquipmentDefinition>>(stream, ProjectSerializer.Options) ?? [];
        foreach (var item in items) item.IsBuiltIn = true;
        return items;
    }
}
