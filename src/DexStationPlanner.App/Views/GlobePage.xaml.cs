using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DexStationPlanner.App.Services;
using DexStationPlanner.App.ViewModels;
using DexStationPlanner.Core.Globe;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Serialization;
using Microsoft.Web.WebView2.Core;

namespace DexStationPlanner.App.Views;

/// <summary>
/// Hosts the CesiumJS globe (wwwroot/globe.html) in WebView2 and keeps it in sync with the selected station.
/// </summary>
public partial class GlobePage : UserControl
{
    private const string VirtualHost = "dex.local";
    private static GlobePage? _instance;

    private readonly DispatcherTimer _sceneTimer;
    private bool _initStarted;
    private bool _pageReady;
    private bool _placementRequested;
    private MainViewModel? _vm;
    private Guid? _lastFlownStation;

    public GlobePage()
    {
        InitializeComponent();
        _instance = this;
        _sceneTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        _sceneTimer.Tick += (_, _) => { _sceneTimer.Stop(); SendScene(); };
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
        ThemeService.ThemeChanged += (_, _) => { if (_pageReady) Post(new { type = "theme", theme = ThemeName }); };
    }

    /// <summary>Asks the globe to enter placement mode as soon as it is ready.</summary>
    public static void RequestPlacement()
    {
        if (_instance is null) return;
        _instance._placementRequested = true;
        if (_instance._pageReady) _instance.BeginPlacement();
    }

    private static string ThemeName => ThemeService.Current == AppTheme.Dark ? "dark" : "light";

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Stations.PropertyChanged -= OnStationsPropertyChanged;
            _vm.Stations.StationContentChanged -= OnStationContentChanged;
            _vm.IonTokenChanged -= OnIonTokenChanged;
        }
        _vm = e.NewValue as MainViewModel;
        if (_vm is not null)
        {
            _vm.Stations.PropertyChanged += OnStationsPropertyChanged;
            _vm.Stations.StationContentChanged += OnStationContentChanged;
            _vm.IonTokenChanged += OnIonTokenChanged;
        }
    }

    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) return;
        if (!_initStarted)
        {
            _initStarted = true;
            await InitializeWebViewAsync();
        }
        else if (_pageReady && _placementRequested)
        {
            BeginPlacement();
        }
    }

    private async Task InitializeWebViewAsync()
    {
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!File.Exists(Path.Combine(wwwroot, "cesium", "Cesium.js")))
        {
            WebStatus.Text = "The globe files are missing (wwwroot/cesium). Rebuild the app; the build installs CesiumJS with npm.";
            return;
        }
        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DEX Station Planner", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException)
        {
            WebStatus.Text = "The globe needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11. " +
                             "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and restart.\n\n" + ex.Message;
            return;
        }

        var core = Web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping(VirtualHost, wwwroot, CoreWebView2HostResourceAccessKind.DenyCors);
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.WebMessageReceived += OnWebMessage;
        core.NewWindowRequested += (_, args) => args.Handled = true; // Keep credits/links from spawning windows.
        core.Navigate($"https://{VirtualHost}/globe.html");
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        switch (root.GetProperty("type").GetString())
        {
            case "ready":
                WebStatus.Visibility = Visibility.Collapsed;
                Post(new { type = "init", ionToken = _vm?.CesiumIonToken ?? "", theme = ThemeName });
                _pageReady = true;
                _lastFlownStation = null;
                SendScene();
                FlyToSelected(force: true);
                if (_placementRequested) BeginPlacement();
                break;
            case "placed":
                _placementRequested = false;
                PlaceButton.IsEnabled = true;
                double? height = root.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetDouble() : null;
                _vm?.Stations.SetLocation(root.GetProperty("lat").GetDouble(), root.GetProperty("lon").GetDouble(), height);
                AppStatus.Set("Station placed.");
                FlyToSelected(force: true);
                break;
            case "placeCancelled":
                _placementRequested = false;
                PlaceButton.IsEnabled = true;
                break;
            case "log":
                var message = root.TryGetProperty("message", out var m) ? m.GetString() : "";
                System.Diagnostics.Debug.WriteLine($"[globe] {message}");
                if (root.TryGetProperty("level", out var level) && level.GetString() == "error") AppStatus.Set($"Globe: {message}");
                break;
        }
    }

    private void Post(object message)
    {
        if (Web.CoreWebView2 is null) return;
        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, ProjectSerializer.Options));
    }

    private void SendScene()
    {
        if (!_pageReady || _vm is null) return;
        var station = _vm.Stations.SelectedStation;
        object? scene = station is null ? null : GlobeSceneBuilder.Build(station, _vm.Project);
        Post(new { type = "scene", scene });
    }

    private void FlyToSelected(bool force)
    {
        if (!_pageReady || _vm?.Stations.SelectedStation is not { } s || !s.Site.IsPlaced) return;
        if (!force && _lastFlownStation == s.Id) return;
        _lastFlownStation = s.Id;
        Post(new { type = "flyTo", lat = s.Site.Latitude, lon = s.Site.Longitude, toStation = true });
    }

    private void OnStationsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StationsViewModel.SelectedStation)) return;
        SendScene();
        FlyToSelected(force: false);
    }

    private void OnStationContentChanged(object? sender, EventArgs e)
    {
        _sceneTimer.Stop();
        _sceneTimer.Start();
    }

    private void OnIonTokenChanged(object? sender, EventArgs e)
    {
        if (Web.CoreWebView2 is null) return;
        _pageReady = false;
        WebStatus.Visibility = Visibility.Visible;
        WebStatus.Text = "Reloading globe…";
        Web.CoreWebView2.Reload();
    }

    private void BeginPlacement()
    {
        if (_vm?.Stations.SelectedStation is not { } s) return;
        Post(new { type = "placeMode", enabled = true, name = s.Name });
        PlaceButton.IsEnabled = false;
        Web.Focus();
    }

    private void PlaceButton_Click(object sender, RoutedEventArgs e)
    {
        _placementRequested = true;
        if (_pageReady) BeginPlacement();
    }

    private void FlyButton_Click(object sender, RoutedEventArgs e) => FlyToSelected(force: true);

    private void SiteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SiteList.SelectedItem is KnownSite site)
            Post(new { type = "flyTo", lat = site.Latitude, lon = site.Longitude, range = 3000 });
    }

    private void UseSiteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SiteList.SelectedItem is not KnownSite site || _vm is null) return;
        _vm.Stations.UseKnownSiteCommand.Execute(site);
        FlyToSelected(force: true);
    }
}
