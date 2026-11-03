using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DexStationPlanner.App.Editors;
using DexStationPlanner.App.Services;
using DexStationPlanner.Core.Globe;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Serialization;
using DexStationPlanner.Core.Stations;
using DexStationPlanner.Core.Units;

namespace DexStationPlanner.App.ViewModels;

/// <summary>Station list, the site-plan editor and the site/globe settings for the selected station.</summary>
public sealed partial class StationsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _statsTimer;
    private DeepChangeObserver? _observer;
    private DeepChangeObserver? _modulesObserver;

    public StationsViewModel(MainViewModel main)
    {
        _main = main;
        _statsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _statsTimer.Tick += (_, _) => { _statsTimer.Stop(); Refresh(); };
        main.ProjectReplaced += (_, _) => OnProjectReplaced();
        UnitContext.Instance.PropertyChanged += (_, _) => Refresh();
    }

    public StationProject Project => _main.Project;
    public ObservableCollection<Station> StationList => Project.Stations;
    public ObservableCollection<ModuleDefinition> Library => Project.Modules;
    public IReadOnlyList<KnownSite> KnownSites => Core.Globe.KnownSites.All;
    public IReadOnlyList<UtilityKind> UtilityKinds { get; } = Enum.GetValues<UtilityKind>();

    /// <summary>Raised whenever anything in the selected station (or the module designs it uses) changes.</summary>
    public event EventHandler? StationContentChanged;

    public Station? SelectedStation
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            Selection = null;
            _observer?.Dispose();
            _observer = null;
            StationEditor?.Dispose();
            StationEditor = value is null ? null : new StationPropertiesEditor(value, () => StationStats.Compute(value, Project));
            if (value is not null)
            {
                _observer = new DeepChangeObserver(value);
                _observer.Changed += (_, _) => ContentChanged();
            }
            OnPropertyChanged(nameof(StationEditor));
            Refresh();
            DeleteStationCommand.NotifyCanExecuteChanged();
            DuplicateStationCommand.NotifyCanExecuteChanged();
        }
    }

    public StationPropertiesEditor? StationEditor { get; private set; }

    public SiteTool Tool { get; set => SetProperty(ref field, value); }
    public UtilityKind UtilityKind { get; set => SetProperty(ref field, value); }
    public bool SnapEnabled { get; set => SetProperty(ref field, value); } = true;

    public object? Selection
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            SelectedEditor?.Dispose();
            SelectedEditor = value switch
            {
                ModuleInstance i => new InstanceEditor(i, Project.FindModule(i.DefinitionId)),
                ConnectorLink l when SelectedStation is { } s => new LinkEditor(l, s, Project.FindModule),
                UtilityRun r when SelectedStation is { } s => new UtilityRunEditor(r, s),
                _ => null,
            };
            OnPropertyChanged(nameof(SelectedEditor));
        }
    }

    public ElementEditor? SelectedEditor { get; private set; }

    public IReadOnlyList<StatRow> Stats { get; private set; } = [];

    private void ContentChanged()
    {
        _statsTimer.Stop();
        _statsTimer.Start();
        StationContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnProjectReplaced()
    {
        var keep = SelectedStation?.Id;
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(StationList));
        OnPropertyChanged(nameof(Library));
        _modulesObserver?.Dispose();
        _modulesObserver = new DeepChangeObserver(Project.Modules);
        _modulesObserver.Changed += (_, _) => ContentChanged();
        SelectedStation = null;
        SelectedStation = StationList.FirstOrDefault(s => s.Id == keep) ?? StationList.FirstOrDefault();
    }

    private void Refresh()
    {
        StationEditor?.Refresh();
        SelectedEditor?.Refresh();
        if (SelectedStation is not { } station)
        {
            Stats = [];
            OnPropertyChanged(nameof(Stats));
            return;
        }
        var s = StationStats.Compute(station, Project);
        var u = UnitContext.Instance.System;
        var rows = new List<StatRow>
        {
            new("Modules", s.ModuleCount.ToString()),
            new("Floor area", UnitFormatter.Area(s.FloorAreaM2, u)),
            new("Site extent", $"{UnitFormatter.Dimension(s.SiteLengthM, u)} × {UnitFormatter.Dimension(s.SiteWidthM, u)}"),
            new("Total mass", UnitFormatter.Mass(s.TotalMassKg, u)),
            new("Berths", s.Berths.ToString(), s.Berths == 0 && s.ModuleCount > 0 ? "warn" : ""),
            new("Work positions", s.Workstations.ToString()),
            new("Power demand", UnitFormatter.Power(s.PowerDemandW)),
            new("Power supply", UnitFormatter.Power(s.PowerSupplyW)),
            new("Power margin", UnitFormatter.Power(s.PowerMarginW), s.PowerMarginW < 0 ? "warn" : "good"),
            new("Mated joints", s.MatedLinks.ToString()),
            new("Corridors", s.Corridors == 0 ? "0" : $"{s.Corridors} ({UnitFormatter.Length(s.CorridorLengthM, u, 1)})"),
        };
        foreach (var (kind, len) in s.UtilityLengthM.Where(kv => kv.Value > 0))
            rows.Add(new($"{kind} lines", UnitFormatter.Length(len, u, 1)));
        if (!station.Site.IsPlaced) rows.Insert(0, new("Location", "Not placed on the globe yet", "warn"));
        Stats = rows;
        OnPropertyChanged(nameof(Stats));
    }

    [RelayCommand]
    private void AddStation()
    {
        var n = StationList.Count + 1;
        while (StationList.Any(s => s.Name == $"Station {n}")) n++;
        var station = new Station { Name = $"Station {n}" };
        StationList.Add(station);
        SelectedStation = station;
        _main.CheckpointNow();
    }

    [RelayCommand(CanExecute = nameof(HasStation))]
    private void DuplicateStation()
    {
        if (SelectedStation is not { } s) return;
        var json = System.Text.Json.JsonSerializer.Serialize(s, ProjectSerializer.Options);
        // New ids for the station and its instances/links/runs; module definition ids stay shared.
        var ids = new[] { s.Id }.Concat(s.Modules.Select(m => m.Id)).Concat(s.Links.Select(l => l.Id)).Concat(s.UtilityRuns.Select(r => r.Id));
        foreach (var id in ids) json = json.Replace(id.ToString(), Guid.NewGuid().ToString());
        var copy = System.Text.Json.JsonSerializer.Deserialize<Station>(json, ProjectSerializer.Options)!;
        copy.Name = s.Name + " (copy)";
        StationList.Insert(StationList.IndexOf(s) + 1, copy);
        SelectedStation = copy;
        _main.CheckpointNow();
    }

    [RelayCommand(CanExecute = nameof(HasStation))]
    private void DeleteStation()
    {
        if (SelectedStation is not { } s || !Dialogs.Confirm($"Delete station \"{s.Name}\"?")) return;
        var index = StationList.IndexOf(s);
        StationList.Remove(s);
        SelectedStation = StationList.Count == 0 ? null : StationList[Math.Min(index, StationList.Count - 1)];
        _main.CheckpointNow();
    }

    [RelayCommand]
    private void SetTool(SiteTool tool) => Tool = tool;

    /// <summary>Moves the selected station to a known research site.</summary>
    [RelayCommand]
    private void UseKnownSite(KnownSite? site)
    {
        if (site is null || SelectedStation is not { } s) return;
        s.Site.Latitude = site.Latitude;
        s.Site.Longitude = site.Longitude;
        s.Site.GroundElevation = site.ElevationM;
        s.Site.SiteName = site.Name;
        s.Site.IsPlaced = true;
        s.Environment = site.Environment;
        _main.CheckpointNow();
    }

    /// <summary>Called by the globe when the user clicks a location in placement mode.</summary>
    public void SetLocation(double latitude, double longitude, double? height)
    {
        if (SelectedStation is not { } s) return;
        s.Site.Latitude = Math.Round(latitude, 6);
        s.Site.Longitude = Math.Round(longitude, 6);
        if (height is { } h && double.IsFinite(h)) s.Site.GroundElevation = Math.Round(h, 1);
        s.Site.SiteName = "";
        s.Site.IsPlaced = true;
        _main.CheckpointNow();
    }

    private bool HasStation() => SelectedStation is not null;

    public void CheckpointNow() => _main.CheckpointNow();
}
