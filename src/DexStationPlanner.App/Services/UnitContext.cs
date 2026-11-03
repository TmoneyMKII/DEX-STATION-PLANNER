using CommunityToolkit.Mvvm.ComponentModel;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Units;

namespace DexStationPlanner.App.Services;

/// <summary>The display unit system for the open project, shared by editors and panels.</summary>
public sealed class UnitContext : ObservableObject
{
    public static UnitContext Instance { get; } = new();

    public UnitSystem System
    {
        get;
        set
        {
            if (SetProperty(ref field, value)) OnPropertyChanged(nameof(LengthUnit));
        }
    }

    public string LengthUnit => UnitFormatter.LengthUnit(System);

    public double ToDisplay(double meters) => UnitFormatter.ToDisplayLength(meters, System);
    public double FromDisplay(double value) => UnitFormatter.FromDisplayLength(value, System);
}
