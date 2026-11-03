using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DexStationPlanner.App.Editors;
using DexStationPlanner.App.Services;
using DexStationPlanner.Core.Equipment;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Modules;
using DexStationPlanner.Core.Serialization;
using DexStationPlanner.Core.Units;

namespace DexStationPlanner.App.ViewModels;

public sealed record GridOption(string Label, double Meters);

/// <summary>The module library and the floor-plan editor for the selected module.</summary>
public sealed partial class ModulesViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _statsTimer;
    private DeepChangeObserver? _moduleObserver;

    public ModulesViewModel(MainViewModel main)
    {
        _main = main;
        _statsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _statsTimer.Tick += (_, _) => { _statsTimer.Stop(); RefreshStats(); };
        main.ProjectReplaced += (_, _) => OnProjectReplaced();
        UnitContext.Instance.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(GridOptions)); RefreshStats(); };
        EquipmentView = CollectionViewSource.GetDefaultView(new ObservableCollection<EquipmentDefinition>());
    }

    public StationProject Project => _main.Project;
    public ObservableCollection<ModuleDefinition> Library => Project.Modules;
    public IReadOnlyList<ModuleTemplate> Templates => ModuleTemplates.All;
    public EquipmentLookup Catalog { get; private set; } = new();

    public ModuleDefinition? SelectedModule
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            Selection = null;
            _moduleObserver?.Dispose();
            _moduleObserver = null;
            ModuleEditor?.Dispose();
            ModuleEditor = value is null ? null : new ModulePropertiesEditor(value, () => ModuleStats.Compute(value, Catalog)).Init();
            if (value is not null)
            {
                _moduleObserver = new DeepChangeObserver(value);
                _moduleObserver.Changed += (_, _) => { _statsTimer.Stop(); _statsTimer.Start(); };
            }
            OnPropertyChanged(nameof(ModuleEditor));
            RefreshStats();
            DuplicateModuleCommand.NotifyCanExecuteChanged();
            DeleteModuleCommand.NotifyCanExecuteChanged();
            ExportModuleCommand.NotifyCanExecuteChanged();
        }
    }

    public ModulePropertiesEditor? ModuleEditor { get; private set; }

    public ModuleTool Tool
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && value != ModuleTool.Equipment) PlacingEquipmentId = null;
        }
    }

    public object? Selection
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            SelectedEditor?.Dispose();
            SelectedEditor = CreateEditor(value);
            OnPropertyChanged(nameof(SelectedEditor));
        }
    }

    public ElementEditor? SelectedEditor { get; private set; }

    public string? PlacingEquipmentId
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool SnapEnabled { get; set => SetProperty(ref field, value); } = true;
    public bool ShowPreview { get; set => SetProperty(ref field, value); } = true;

    public IReadOnlyList<GridOption> GridOptions => UnitContext.Instance.System == UnitSystem.Metric
        ? [new("5 cm", 0.05), new("10 cm", 0.1), new("25 cm", 0.25), new("50 cm", 0.5), new("1 m", 1)]
        : [new("1 in", 0.0254), new("3 in", 0.0762), new("6 in", 0.1524), new("1 ft", 0.3048), new("2 ft", 0.6096)];

    public double GridStep { get; set => SetProperty(ref field, value); } = 0.25;

    public ICollectionView EquipmentView { get; private set; }

    public string EquipmentFilter
    {
        get;
        set
        {
            if (SetProperty(ref field, value)) EquipmentView.Refresh();
        }
    } = "";

    public IReadOnlyList<StatRow> Stats { get; private set; } = [];

    private void OnProjectReplaced()
    {
        var keepId = SelectedModule?.Id;
        Catalog = EquipmentLookup.ForProject(Project);
        OnPropertyChanged(nameof(Catalog));
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(Library));

        var items = new ObservableCollection<EquipmentDefinition>(Catalog.All().OrderBy(e => e.Category).ThenBy(e => e.Name));
        EquipmentView = CollectionViewSource.GetDefaultView(items);
        EquipmentView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EquipmentDefinition.Category)));
        EquipmentView.Filter = o => o is EquipmentDefinition d && (string.IsNullOrWhiteSpace(EquipmentFilter)
            || d.Name.Contains(EquipmentFilter, StringComparison.OrdinalIgnoreCase)
            || d.Category.ToString().Contains(EquipmentFilter, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(EquipmentView));

        // Force a rebind even when the same id comes back (undo replaces every object).
        SelectedModule = null;
        SelectedModule = Library.FirstOrDefault(m => m.Id == keepId) ?? Library.FirstOrDefault();
    }

    private ElementEditor? CreateEditor(object? selection)
    {
        if (SelectedModule is not { } m) return null;
        return selection switch
        {
            ShellVertex v => new VertexEditor(v),
            EdgeSelection e => new EdgeEditor(e, m),
            InteriorWall w => new WallEditor(w),
            Opening o => new OpeningEditor(o, m),
            ModuleConnector c => new ConnectorEditor(c, m),
            EquipmentPlacement p => new EquipmentEditor(p, Catalog.Find(p.DefinitionId)),
            _ => null,
        };
    }

    private void RefreshStats()
    {
        ModuleEditor?.Refresh();
        SelectedEditor?.Refresh();
        if (SelectedModule is not { } m)
        {
            Stats = [];
        }
        else
        {
            var s = ModuleStats.Compute(m, Catalog);
            var u = UnitContext.Instance.System;
            var rows = new List<StatRow>
            {
                new("Footprint", $"{UnitFormatter.Dimension(s.FootprintLengthM, u)} × {UnitFormatter.Dimension(s.FootprintWidthM, u)}"),
                new("Floor area", UnitFormatter.Area(s.FloorAreaM2, u)),
                new("Interior volume", UnitFormatter.Volume(s.InteriorVolumeM3, u)),
                new("Total mass", UnitFormatter.Mass(s.TotalMassKg, u)),
                new("Equipment items", s.EquipmentCount.ToString()),
                new("Berths", s.Berths.ToString()),
                new("Work positions", s.Workstations.ToString()),
                new("Power demand", UnitFormatter.Power(s.PowerDemandW)),
            };
            if (s.PowerSupplyW > 0) rows.Add(new("Power supply", UnitFormatter.Power(s.PowerSupplyW), "good"));
            if (s.MissingEquipmentCount > 0) rows.Add(new("Unknown equipment", s.MissingEquipmentCount.ToString(), "warn"));
            if (!ModuleGeometry.HasClosedShell(m)) rows.Insert(0, new("Shell", "Not drawn yet: use the Shell tool", "warn"));
            Stats = rows;
        }
        OnPropertyChanged(nameof(Stats));
    }

    [RelayCommand]
    private void NewModule(ModuleTemplate template)
    {
        var name = template.Shape == ModuleShapeKind.Blank ? "New Module" : template.Name;
        var m = ModuleTemplates.Create(template, UniqueName(name));
        Library.Add(m);
        SelectedModule = m;
        Tool = template.Shape == ModuleShapeKind.Blank ? ModuleTool.Shell : ModuleTool.Select;
        _main.CheckpointNow();
    }

    [RelayCommand(CanExecute = nameof(HasModule))]
    private void DuplicateModule()
    {
        if (SelectedModule is not { } m) return;
        var json = ProjectSerializer.SerializeModule(m);
        // Fresh ids for the copy so the two designs stay independent.
        var copy = ProjectSerializer.DeserializeModule(ReplaceIds(json, m));
        copy.Name = UniqueName(m.Name + " (copy)");
        Library.Insert(Library.IndexOf(m) + 1, copy);
        SelectedModule = copy;
        _main.CheckpointNow();
    }

    [RelayCommand(CanExecute = nameof(HasModule))]
    private void DeleteModule()
    {
        if (SelectedModule is not { } m) return;
        var uses = Project.Stations.Sum(s => s.Modules.Count(i => i.DefinitionId == m.Id));
        var message = uses > 0
            ? $"Delete \"{m.Name}\"? It is placed {uses} time(s) in your stations; those placements will be removed too."
            : $"Delete \"{m.Name}\"?";
        if (!Dialogs.Confirm(message)) return;

        foreach (var station in Project.Stations)
        {
            foreach (var inst in station.Modules.Where(i => i.DefinitionId == m.Id).ToList()) station.Modules.Remove(inst);
            Core.Stations.SiteLayout.RemoveDanglingReferences(station, Project.FindModule);
        }
        var index = Library.IndexOf(m);
        Library.Remove(m);
        SelectedModule = Library.Count == 0 ? null : Library[Math.Min(index, Library.Count - 1)];
        _main.CheckpointNow();
    }

    [RelayCommand(CanExecute = nameof(HasModule))]
    private async Task ExportModule()
    {
        if (SelectedModule is not { } m || Dialogs.SaveModule(m.Name) is not { } path) return;
        try
        {
            await File.WriteAllTextAsync(path, ProjectSerializer.SerializeModule(m));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error($"Couldn't export the module.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportModule()
    {
        if (Dialogs.OpenModule() is not { } path) return;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var m = ProjectSerializer.DeserializeModule(json);
            if (Library.Any(x => x.Id == m.Id)) m = ProjectSerializer.DeserializeModule(ReplaceIds(json, m));
            m.Name = UniqueName(m.Name);
            Library.Add(m);
            SelectedModule = m;
            _main.CheckpointNow();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Dialogs.Error($"Couldn't import the module.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void SetTool(ModuleTool tool) => Tool = tool;

    [RelayCommand]
    private void PlaceEquipment(EquipmentDefinition? def)
    {
        if (def is null) return;
        PlacingEquipmentId = def.Id;
        Tool = ModuleTool.Equipment;
    }

    private bool HasModule() => SelectedModule is not null;

    private string UniqueName(string name)
    {
        if (Library.All(m => m.Name != name)) return name;
        var n = 2;
        while (Library.Any(m => m.Name == $"{name} {n}")) n++;
        return $"{name} {n}";
    }

    /// <summary>Rewrites every id in a serialized module so a copy shares nothing with the original.</summary>
    private static string ReplaceIds(string json, ModuleDefinition m)
    {
        var ids = new[] { m.Id }
            .Concat(m.Shell.Select(v => v.Id))
            .Concat(m.InteriorWalls.Select(w => w.Id))
            .Concat(m.Openings.Select(o => o.Id))
            .Concat(m.Connectors.Select(c => c.Id))
            .Concat(m.Equipment.Select(e => e.Id));
        foreach (var id in ids) json = json.Replace(id.ToString(), Guid.NewGuid().ToString());
        return json;
    }
}
