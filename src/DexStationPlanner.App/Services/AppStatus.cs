namespace DexStationPlanner.App.Services;

/// <summary>Short status-bar messages (cursor position, hints) from anywhere in the app.</summary>
public static class AppStatus
{
    public static event EventHandler<string>? Changed;

    public static void Set(string message) => Changed?.Invoke(null, message);
}
