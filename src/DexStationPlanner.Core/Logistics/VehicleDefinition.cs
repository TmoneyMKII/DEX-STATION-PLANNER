using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using DexStationPlanner.Core.Model;

namespace DexStationPlanner.Core.Logistics;

public enum TransportMode { FixedWing, Rotorcraft, Ship, Overland }

/// <summary>
/// An aircraft, vessel or ground vehicle. Figures are representative planning values; real capability depends
/// on configuration, temperature, elevation and operator, so always confirm with the operator.
/// </summary>
public sealed class VehicleDefinition : ObservableObject
{
    public string Id { get; set => SetProperty(ref field, value); } = "";
    public string Name { get; set => SetProperty(ref field, value); } = "";
    public TransportMode Mode { get; set => SetProperty(ref field, value); }
    public string Description { get; set => SetProperty(ref field, value); } = "";

    /// <summary>Typical usable payload for station resupply, kg.</summary>
    public double PayloadKg { get; set => SetProperty(ref field, value); }

    public int Passengers { get; set => SetProperty(ref field, value); }
    public double CruiseSpeedKmh { get; set => SetProperty(ref field, value); }

    /// <summary>Range with a typical payload, km.</summary>
    public double RangeKm { get; set => SetProperty(ref field, value); }

    public double FuelBurnLitersPerHour { get; set => SetProperty(ref field, value); }
    public string FuelType { get; set => SetProperty(ref field, value); } = "";

    /// <summary>Highest site elevation the vehicle can routinely operate from, m (density-altitude limit).</summary>
    public double MaxSiteElevationM { get; set => SetProperty(ref field, value); } = 9000;

    /// <summary>Fixed-wing: minimum runway length by surface. Surfaces not listed can't be used.</summary>
    public Dictionary<SurfaceType, double> RunwayLengthM { get; set; } = [];

    public double MinRunwayWidthM { get; set => SetProperty(ref field, value); }

    /// <summary>Rotorcraft: overall length with rotors turning ("D" value); the landing area must be at least this wide.</summary>
    public double RotorDiameterM { get; set => SetProperty(ref field, value); }

    /// <summary>Ship: draft and overall length, m.</summary>
    public double DraftM { get; set => SetProperty(ref field, value); }

    public double LengthM { get; set => SetProperty(ref field, value); }

    /// <summary>Overland: road/track surfaces it can drive and the minimum width it needs.</summary>
    public List<SurfaceType> RoadSurfaces { get; set; } = [];

    public double MinRoadWidthM { get; set => SetProperty(ref field, value); }

    [JsonIgnore]
    public bool IsBuiltIn { get; set; }
}
