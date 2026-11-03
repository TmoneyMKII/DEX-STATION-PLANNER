using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using DexStationPlanner.App.Services;
using DexStationPlanner.Core.Equipment;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Rendering;
using HelixToolkit.Wpf;

namespace DexStationPlanner.App.Controls;

/// <summary>Live 3D preview of a module, rebuilt (throttled) whenever the design changes.</summary>
public sealed class ModulePreview3D : UserControl
{
    private readonly HelixViewport3D _viewport;
    private readonly ModelVisual3D _model = new();
    private readonly GridLinesVisual3D _ground;
    private readonly DispatcherTimer _rebuildTimer;
    private DeepChangeObserver? _observer;
    private bool _zoomPending = true;
    private Guid? _lastModuleId;

    public ModulePreview3D()
    {
        _viewport = new HelixViewport3D
        {
            ShowViewCube = true,
            ShowCoordinateSystem = false,
            ModelUpDirection = new Vector3D(0, 0, 1),
            IsHeadLightEnabled = true,
            ZoomExtentsWhenLoaded = false,
        };
        _viewport.Children.Add(new DefaultLights());
        _ground = new GridLinesVisual3D { Width = 20, Length = 20, MinorDistance = 1, MajorDistance = 5, Thickness = 0.015, Fill = Brushes.Gray };
        _viewport.Children.Add(_ground);
        _viewport.Children.Add(_model);
        Content = _viewport;

        _rebuildTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => Rebuild(), Dispatcher);
        _rebuildTimer.Stop();

        Loaded += (_, _) => { ThemeService.ThemeChanged += OnThemeChanged; ApplyTheme(); ScheduleRebuild(); };
        Unloaded += (_, _) => ThemeService.ThemeChanged -= OnThemeChanged;
    }

    public static readonly DependencyProperty ModuleProperty = DependencyProperty.Register(
        nameof(Module), typeof(ModuleDefinition), typeof(ModulePreview3D),
        new PropertyMetadata(null, (d, _) => ((ModulePreview3D)d).OnModuleChanged()));

    public ModuleDefinition? Module
    {
        get => (ModuleDefinition?)GetValue(ModuleProperty);
        set => SetValue(ModuleProperty, value);
    }

    public static readonly DependencyProperty CatalogProperty = DependencyProperty.Register(
        nameof(Catalog), typeof(EquipmentLookup), typeof(ModulePreview3D),
        new PropertyMetadata(null, (d, _) => ((ModulePreview3D)d).ScheduleRebuild()));

    public EquipmentLookup? Catalog
    {
        get => (EquipmentLookup?)GetValue(CatalogProperty);
        set => SetValue(CatalogProperty, value);
    }

    public void ZoomToFit()
    {
        if (_model.Content is { Bounds.IsEmpty: false } content) _viewport.ZoomExtents(content.Bounds, 300);
    }

    private void OnModuleChanged()
    {
        _observer?.Dispose();
        _observer = null;
        if (Module is { } m)
        {
            _observer = new DeepChangeObserver(m);
            _observer.Changed += (_, _) => ScheduleRebuild();
            if (m.Id != _lastModuleId) _zoomPending = true;
            _lastModuleId = m.Id;
        }
        ScheduleRebuild();
    }

    private void ScheduleRebuild()
    {
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    private void Rebuild()
    {
        _rebuildTimer.Stop();
        if (!IsLoaded) return;
        var group = new Model3DGroup();
        if (Module is { } m)
        {
            // See-through walls show the fit-out. WPF blends in draw order, so opaque parts go first.
            var scene = ModuleMeshBuilder.Build(m, Catalog ?? new EquipmentLookup(), wallOpacity: 0.45);
            foreach (var part in scene.Parts.OrderByDescending(p => p.Opacity)) group.Children.Add(ToModel(part));
        }
        group.Freeze();
        _model.Content = group;

        // Size the ground grid to the module rather than letting a huge grid dominate the view.
        var bounds = group.Bounds;
        if (!bounds.IsEmpty)
        {
            var extent = Math.Ceiling(Math.Max(bounds.SizeX, bounds.SizeY) + 4);
            _ground.Width = extent;
            _ground.Length = extent;
            _ground.Center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, 0);
        }

        if (_zoomPending && !bounds.IsEmpty && _viewport.Camera is { } camera)
        {
            _zoomPending = false;
            camera.Position = new Point3D(-12, -16, 14);
            camera.LookDirection = new Vector3D(12, 16, -13);
            camera.UpDirection = new Vector3D(0, 0, 1);
            _viewport.ZoomExtents(bounds, 0);
        }
    }

    private static GeometryModel3D ToModel(MeshPart part)
    {
        var mesh = new MeshGeometry3D();
        var positions = new Point3DCollection(part.Positions.Count);
        foreach (var p in part.Positions) positions.Add(new Point3D(p.X, p.Y, p.Z));
        mesh.Positions = positions;
        mesh.TriangleIndices = new Int32Collection(part.Indices);
        mesh.Freeze();

        var color = (Color)ColorConverter.ConvertFromString(part.Color);
        color.A = (byte)Math.Round(255 * Math.Clamp(part.Opacity, 0, 1));
        var material = new DiffuseMaterial(new SolidColorBrush(color));
        material.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void ApplyTheme()
    {
        if (TryFindResource("Dex.Preview3D.Background") is Color bg) _viewport.Background = new SolidColorBrush(bg);
        _ground.Fill = TryFindResource("Dex.Canvas.GridMajor") as Brush ?? Brushes.Gray;
    }
}
