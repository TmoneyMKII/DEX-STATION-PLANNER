using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DexStationPlanner.App.Converters;
using DexStationPlanner.App.Services;
using DexStationPlanner.Core.Logistics;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Units;

namespace DexStationPlanner.App.ViewModels;

/// <summary>A vehicle's access verdict at the selected station, with the feature it would use.</summary>
public sealed record StationAccessRow(string Vehicle, TransportMode Mode, bool Ok, string Via, string Reason);

/// <summary>The transport catalog and how each vehicle can (or can't) reach the selected station.</summary>
public sealed class LogisticsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _refreshTimer;

    public LogisticsViewModel(MainViewModel main)
    {
        _main = main;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); RefreshAccess(); };
        main.ProjectReplaced += (_, _) => Reload();
        main.Stations.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StationsViewModel.SelectedStation)) ScheduleRefresh();
        };
        main.Stations.StationContentChanged += (_, _) => ScheduleRefresh();
        UnitContext.Instance.PropertyChanged += (_, _) => { ScheduleRefresh(); OnPropertyChanged(nameof(Specs)); };
        VehicleView = CollectionViewSource.GetDefaultView(new List<VehicleDefinition>());
    }

    public StationsViewModel Stations => _main.Stations;
    public ICollectionView VehicleView { get; private set; }

    public VehicleDefinition? SelectedVehicle
    {
        get;
        set
        {
            if (SetProperty(ref field, value)) OnPropertyChanged(nameof(Specs));
        }
    }

    /// <summary>Spec sheet for the selected vehicle, in display units.</summary>
    public IReadOnlyList<StatRow> Specs
    {
        get
        {
            if (SelectedVehicle is not { } v) return [];
            var u = UnitContext.Instance.System;
            var rows = new List<StatRow>
            {
                new("Type", HumanizeConverter.Humanize(v.Mode)),
                new("Payload", UnitFormatter.Mass(v.PayloadKg, u)),
                new("Passengers", v.Passengers.ToString()),
                new("Cruise speed", u == UnitSystem.Metric ? $"{v.CruiseSpeedKmh:N0} km/h" : $"{v.CruiseSpeedKmh / 1.852:N0} kt"),
                new("Range with payload", u == UnitSystem.Metric ? $"{v.RangeKm:N0} km" : $"{v.RangeKm / 1.852:N0} nmi"),
                new("Fuel burn", u == UnitSystem.Metric ? $"{v.FuelBurnLitersPerHour:N0} L/h" : $"{v.FuelBurnLitersPerHour / 3.785411784:N0} gal/h"),
                new("Fuel", v.FuelType),
            };
            if (v.Mode != TransportMode.Ship)
                rows.Add(new("Max site elevation", UnitFormatter.Length(v.MaxSiteElevationM, u, 0)));
            switch (v.Mode)
            {
                case TransportMode.FixedWing:
                    foreach (var (surface, length) in v.RunwayLengthM.OrderBy(kv => kv.Value))
                        rows.Add(new($"Runway: {Compatibility.SurfaceName(surface)}", UnitFormatter.Length(length, u, 0)));
                    rows.Add(new("Min runway width", UnitFormatter.Length(v.MinRunwayWidthM, u, 0)));
                    break;
                case TransportMode.Rotorcraft:
                    rows.Add(new("Rotor diameter (D)", UnitFormatter.Length(v.RotorDiameterM, u, 1)));
                    break;
                case TransportMode.Ship:
                    rows.Add(new("Draft", UnitFormatter.Length(v.DraftM, u, 1)));
                    rows.Add(new("Length overall", UnitFormatter.Length(v.LengthM, u, 0)));
                    break;
                default:
                    rows.Add(new("Surfaces", string.Join(", ", v.RoadSurfaces.Select(Compatibility.SurfaceName))));
                    rows.Add(new("Min road width", UnitFormatter.Length(v.MinRoadWidthM, u, 0)));
                    break;
            }
            return rows;
        }
    }

    public IReadOnlyList<StationAccessRow> Access { get; private set; } = [];

    public string AccessTitle => Stations.SelectedStation is { } s ? $"Access to {s.Name}" : "Access";

    private void Reload()
    {
        var keep = SelectedVehicle?.Id;
        var items = VehicleLookup.ForProject(_main.Project).All().OrderBy(v => v.Mode).ToList();
        VehicleView = CollectionViewSource.GetDefaultView(items);
        VehicleView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VehicleDefinition.Mode)));
        OnPropertyChanged(nameof(VehicleView));
        SelectedVehicle = items.FirstOrDefault(v => v.Id == keep) ?? items.FirstOrDefault();
        RefreshAccess();
    }

    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void RefreshAccess()
    {
        if (Stations.SelectedStation is not { } station)
        {
            Access = [];
        }
        else
        {
            var vehicles = VehicleLookup.ForProject(_main.Project).All();
            Access = Compatibility.StationAccess(station, vehicles, UnitContext.Instance.System)
                .Select(a => new StationAccessRow(a.Vehicle.Name, a.Vehicle.Mode, a.Ok,
                    a.Ok && a.Feature is { } f ? (string.IsNullOrWhiteSpace(f.Name) ? HumanizeConverter.Humanize(f.Kind) : f.Name) : "",
                    a.Reason))
                .ToList();
        }
        OnPropertyChanged(nameof(Access));
        OnPropertyChanged(nameof(AccessTitle));
    }
}
