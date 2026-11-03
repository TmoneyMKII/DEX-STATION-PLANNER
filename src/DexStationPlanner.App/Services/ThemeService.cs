using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DexStationPlanner.App.Services;

/// <summary>Switches between the dark and light DEX themes at runtime.</summary>
public static class ThemeService
{
    private const string DarkUri = "Themes/Dark.xaml";
    private const string LightUri = "Themes/Light.xaml";

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>Raised after the palette changes so custom-drawn canvases can repaint.</summary>
    public static event EventHandler? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        var app = Application.Current;
        Current = theme;
        app.ThemeMode = theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;

        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString is DarkUri or LightUri);
        var replacement = new ResourceDictionary { Source = new Uri(theme == AppTheme.Dark ? DarkUri : LightUri, UriKind.Relative) };
        if (existing is null) dictionaries.Insert(0, replacement);
        else dictionaries[dictionaries.IndexOf(existing)] = replacement;

        foreach (Window w in app.Windows) ApplyTitleBar(w);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Asks DWM to draw a dark or light caption to match the theme.</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var useDark = Current == AppTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
    }

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
