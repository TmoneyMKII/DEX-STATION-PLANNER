using DexStationPlanner.Core.Model;

namespace DexStationPlanner.Core.Globe;

public sealed record KnownSite(string Name, string Region, double Latitude, double Longitude, double ElevationM, SiteEnvironment Environment);

/// <summary>Reference locations for quickly jumping the globe to real research sites (approximate coordinates).</summary>
public static class KnownSites
{
    public static readonly IReadOnlyList<KnownSite> All =
    [
        new("Amundsen–Scott South Pole Station", "Antarctica", -90.0, 0.0, 2835, SiteEnvironment.Polar),
        new("McMurdo Station", "Antarctica", -77.846, 166.676, 10, SiteEnvironment.Polar),
        new("Concordia Station (Dome C)", "Antarctica", -75.100, 123.333, 3233, SiteEnvironment.Polar),
        new("Summit Station", "Greenland", 72.580, -38.459, 3216, SiteEnvironment.Polar),
        new("Utqiaġvik (Barrow)", "Alaska", 71.290, -156.789, 10, SiteEnvironment.Polar),
        new("Ny-Ålesund", "Svalbard", 78.925, 11.930, 10, SiteEnvironment.Polar),
        new("ALMA Array Operations Site", "Atacama, Chile", -23.023, -67.755, 5050, SiteEnvironment.Desert),
        new("Paranal Observatory", "Atacama, Chile", -24.627, -70.404, 2635, SiteEnvironment.Desert),
        new("Mars Desert Research Station", "Utah, USA", 38.406, -110.792, 1350, SiteEnvironment.Desert),
        new("Mauna Kea Summit", "Hawaiʻi, USA", 19.821, -155.468, 4205, SiteEnvironment.Mountain),
        new("Jungfraujoch Research Station", "Switzerland", 46.548, 7.985, 3466, SiteEnvironment.Mountain),
        new("Pyramid Laboratory (Everest)", "Nepal", 27.959, 86.813, 5050, SiteEnvironment.Mountain),
    ];
}
