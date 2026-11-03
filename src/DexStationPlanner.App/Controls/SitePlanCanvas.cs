using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DexStationPlanner.App.Editors;
using DexStationPlanner.App.Services;
using DexStationPlanner.Core.Equipment;
using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Globe;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Modules;
using DexStationPlanner.Core.Stations;
using DexStationPlanner.Core.Units;

namespace DexStationPlanner.App.Controls;

/// <summary>
/// Site plan for a <see cref="Station"/>: place module instances, snap their connectors together,
/// draw corridors between connectors and route utility lines.
/// </summary>
public sealed class SitePlanCanvas : PlanCanvasBase
{
    public const string ModuleDragFormat = "DexStationPlanner.ModuleId";
    private const double ConnectorHitPx = 12;

    private DeepChangeObserver? _stationObserver;
    private DeepChangeObserver? _projectObserver;
    private ModuleDrag? _drag;
    private (ModuleInstance Module, ModuleConnector Connector)? _linkStart;
    private ModuleInstance? _runStart;
    private readonly List<Vec2> _runWaypoints = [];
    private Vec2 _hover;
    private bool _hoverValid;
    private Guid? _dragOverModule;
    private WaypointDrag? _waypointDrag;
    private Guid? _lastStationId;
    private readonly List<Vec2> _featurePoints = [];
    private FeatureDrag? _featureDrag;

    public SitePlanCanvas()
    {
        AllowDrop = true;
        MinScale = 0.02; // ~50 m per pixel: room for airfields kilometers from camp
        MaxScale = 200;
        MajorStep = 10;
        GridStep = 0.5;
    }

    public event EventHandler? EditCompleted;

    #region Dependency properties

    public static readonly DependencyProperty StationProperty = DependencyProperty.Register(
        nameof(Station), typeof(Station), typeof(SitePlanCanvas),
        new PropertyMetadata(null, (d, _) => ((SitePlanCanvas)d).OnStationChanged()));

    public Station? Station
    {
        get => (Station?)GetValue(StationProperty);
        set => SetValue(StationProperty, value);
    }

    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project), typeof(StationProject), typeof(SitePlanCanvas),
        new PropertyMetadata(null, (d, _) => ((SitePlanCanvas)d).OnProjectChanged()));

    public StationProject? Project
    {
        get => (StationProject?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public static readonly DependencyProperty ToolProperty = DependencyProperty.Register(
        nameof(Tool), typeof(SiteTool), typeof(SitePlanCanvas),
        new FrameworkPropertyMetadata(SiteTool.Select, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((SitePlanCanvas)d).CancelPending()));

    public SiteTool Tool
    {
        get => (SiteTool)GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
        nameof(Selection), typeof(object), typeof(SitePlanCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public object? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    public static readonly DependencyProperty UtilityKindProperty = DependencyProperty.Register(
        nameof(UtilityKind), typeof(UtilityKind), typeof(SitePlanCanvas), new PropertyMetadata(UtilityKind.Power));

    /// <summary>Kind of utility line drawn by the Utility tool.</summary>
    public UtilityKind UtilityKind
    {
        get => (UtilityKind)GetValue(UtilityKindProperty);
        set => SetValue(UtilityKindProperty, value);
    }

    public static readonly DependencyProperty FeatureKindProperty = DependencyProperty.Register(
        nameof(FeatureKind), typeof(SiteFeatureKind), typeof(SitePlanCanvas),
        new PropertyMetadata(SiteFeatureKind.Runway, (d, _) => ((SitePlanCanvas)d).CancelPending()));

    /// <summary>Kind of infrastructure placed by the Feature tool.</summary>
    public SiteFeatureKind FeatureKind
    {
        get => (SiteFeatureKind)GetValue(FeatureKindProperty);
        set => SetValue(FeatureKindProperty, value);
    }

    public static readonly DependencyProperty UnitsProperty = DependencyProperty.Register(
        nameof(Units), typeof(UnitSystem), typeof(SitePlanCanvas),
        new FrameworkPropertyMetadata(UnitSystem.Metric, FrameworkPropertyMetadataOptions.AffectsRender));

    public UnitSystem Units
    {
        get => (UnitSystem)GetValue(UnitsProperty);
        set => SetValue(UnitsProperty, value);
    }

    #endregion

    private ModuleDefinition? Resolve(Guid id) => Project?.FindModule(id);
    private EquipmentLookup Catalog => Project is { } p ? EquipmentLookup.ForProject(p) : new EquipmentLookup();

    private void OnStationChanged()
    {
        _stationObserver?.Dispose();
        _stationObserver = null;
        CancelPending();
        if (Station is { } s)
        {
            _stationObserver = new DeepChangeObserver(s);
            _stationObserver.Changed += (_, _) => InvalidateVisual();
            // Undo swaps in a copy with the same id; keep the user's view in that case.
            if (s.Id != _lastStationId) ZoomToFit();
            _lastStationId = s.Id;
        }
        InvalidateVisual();
    }

    private void OnProjectChanged()
    {
        _projectObserver?.Dispose();
        _projectObserver = null;
        if (Project is { } p)
        {
            // Module designs edited elsewhere change footprints here.
            _projectObserver = new DeepChangeObserver(p.Modules);
            _projectObserver.Changed += (_, _) => InvalidateVisual();
        }
        InvalidateVisual();
    }

    protected override (Vec2 Min, Vec2 Max)? ContentBounds()
    {
        if (Station is not { } s) return null;
        var pts = new List<Vec2>();
        foreach (var inst in s.Modules)
            if (Resolve(inst.DefinitionId) is { } def) pts.AddRange(SiteLayout.WorldFootprint(inst, def));
        foreach (var f in s.Features) pts.AddRange(FeatureGeometry.Points(f));
        if (pts.Count == 0) return (new Vec2(-25, -25), new Vec2(25, 25));
        var (min, max) = Polygon.Bounds(pts);
        return (min - new Vec2(5, 5), max + new Vec2(5, 5));
    }

    private void CancelPending()
    {
        _linkStart = null;
        _runStart = null;
        _runWaypoints.Clear();
        _featurePoints.Clear();
        InvalidateVisual();
    }

    private void Complete()
    {
        if (Station is { } s) SiteLayout.RemoveDanglingReferences(s, Resolve);
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override (double Meters, string Label) ScaleBarLength(double targetMeters)
    {
        if (Units == UnitSystem.Metric) return base.ScaleBarLength(targetMeters);
        var feet = new[] { 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500 };
        var ft = feet.FirstOrDefault(f => f * UnitFormatter.MetersPerFoot >= targetMeters, 2500);
        return (ft * UnitFormatter.MetersPerFoot, $"{ft} ft");
    }

    #region Rendering

    protected override void RenderContent(DrawingContext dc)
    {
        if (Station is not { } s) return;
        var catalog = Catalog;
        var used = SiteLayout.UsedConnectors(s);

        // Infrastructure sits under everything else: roads first, then areas.
        foreach (var f in s.Features.OrderBy(f => f.Kind == SiteFeatureKind.Road ? 0 : 1))
            DrawFeature(dc, s, f, ReferenceEquals(Selection, f));

        foreach (var link in s.Links) DrawLink(dc, s, link, ReferenceEquals(Selection, link));
        foreach (var inst in s.Modules) DrawModule(dc, inst, catalog, ReferenceEquals(Selection, inst), used);
        foreach (var run in s.UtilityRuns) DrawRun(dc, s, run, ReferenceEquals(Selection, run));

        DrawPending(dc, s);
        DrawNorthArrow(dc, s);
    }

    private void DrawFeature(DrawingContext dc, Station s, SiteFeature f, bool selected)
    {
        var color = GlobeSceneBuilder.FeatureColor(f);
        var outlinePen = new Pen(selected ? ThemeBrush("Dex.Canvas.Selection") : BrushFromHex(color), selected ? 2.5 : 1.2);
        outlinePen.Freeze();
        var textBrush = ThemeBrush("Dex.Canvas.Text");
        var halo = ThemeBrush("Dex.Surface");
        var name = string.IsNullOrWhiteSpace(f.Name) ? f.Kind.ToString() : f.Name;

        if (f.Kind == SiteFeatureKind.Road)
        {
            if (f.Path.Count < 2) return;
            var geometry = PolylineGeometry(f.Path, false);
            if (selected)
            {
                var glow = new Pen(ThemeBrush("Dex.Canvas.Selection"), Math.Max(2, f.Width * Scale) + 4) { LineJoin = PenLineJoin.Round };
                glow.Freeze();
                dc.DrawGeometry(null, glow, geometry);
            }
            var road = new Pen(BrushFromHex(color, 220), Math.Max(2, f.Width * Scale)) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            road.Freeze();
            dc.DrawGeometry(null, road, geometry);
            if (selected)
                foreach (var p in f.Path) dc.DrawEllipse(ThemeBrush("Dex.Canvas.Handle"), ThemePen("Dex.Canvas.Selection", 1.5), ToScreen(p), 5, 5);
            var mid = f.Path[f.Path.Count / 2 - (f.Path.Count % 2 == 0 ? 1 : 0)];
            var next = f.Path[Math.Min(f.Path.Count - 1, f.Path.IndexOf(mid) + 1)];
            DrawText(dc, name, ToScreen(Vec2.Lerp(mid, next, 0.5)) + new Vector(0, -12), textBrush, 10, halo: halo);
            return;
        }

        var outline = FeatureGeometry.Outline(f)!;
        dc.DrawGeometry(BrushFromHex(color, f.Kind == SiteFeatureKind.ScienceSite ? (byte)40 : (byte)200), outlinePen, PolylineGeometry(outline, true, true));
        var center = ToScreen(f.Position);
        var label = name;

        switch (f.Kind)
        {
            case SiteFeatureKind.Runway:
            {
                var (a, b) = FeatureGeometry.Ends(f);
                var marking = new Pen(f.Surface == SurfaceType.Snow ? Brushes.SteelBlue : Brushes.White, Math.Clamp(Scale, 1, 3)) { DashStyle = DashStyles.Dash };
                marking.Freeze();
                dc.DrawLine(marking, ToScreen(a), ToScreen(b));
                label = $"{name} {FeatureGeometry.RunwayDesignation(f, s.Site.GridHeadingDegrees)} · {UnitFormatter.Length(f.Length, Units, 0)}";
                break;
            }
            case SiteFeatureKind.Helipad:
            {
                var r = f.Length / 2 * Scale;
                if (r > 6) DrawText(dc, "H", center, Brushes.White, Math.Clamp(r, 8, 28), bold: true);
                if (r > 12) center += new Vector(0, r + 10);
                break;
            }
            case SiteFeatureKind.ScienceSite:
            {
                var dash = new Pen(BrushFromHex(color), 1.5) { DashStyle = DashStyles.Dash };
                dash.Freeze();
                dc.DrawGeometry(null, dash, PolylineGeometry(outline, true));
                break;
            }
        }
        DrawText(dc, label, center, textBrush, 10, halo: halo);
    }

    private void DrawModule(DrawingContext dc, ModuleInstance inst, EquipmentLookup catalog, bool selected, HashSet<(Guid, Guid)> used)
    {
        if (Resolve(inst.DefinitionId) is not { } def)
        {
            DrawText(dc, $"{inst.Tag} (missing module)", ToScreen(inst.Position), ThemeBrush("Dex.Warning"), 11);
            return;
        }
        if (!ModuleGeometry.HasClosedShell(def)) return;

        var pose = inst.Pose;
        var footprint = SiteLayout.WorldFootprint(inst, def);
        var stroke = new Pen(selected ? ThemeBrush("Dex.Canvas.Selection") : BrushFromHex(def.Color), selected ? 2.5 : 1.5);
        stroke.Freeze();
        dc.DrawGeometry(BrushFromHex(def.Color, 70), stroke, PolylineGeometry(footprint, true, true));

        // Interior detail once zoomed in enough to read it.
        if (Scale > 10)
        {
            var wallPen = ThemePen("Dex.Canvas.Wall", Math.Max(1, 0.1 * Scale));
            foreach (var w in def.InteriorWalls)
                dc.DrawGeometry(null, wallPen, PolylineGeometry(w.Segment.Tessellate().Select(pose.Apply).ToList(), false));
            var itemPen = new Pen(ThemeBrush("Dex.Canvas.Wall"), 0.8);
            itemPen.Freeze();
            foreach (var item in def.Equipment)
                if (catalog.Find(item.DefinitionId) is { } ed)
                    dc.DrawGeometry(BrushFromHex(ed.Color, 60), itemPen, PolylineGeometry(ModuleGeometry.EquipmentCorners(item, ed).Select(pose.Apply).ToList(), true, true));
        }

        foreach (var c in def.Connectors)
        {
            if (SiteLayout.WorldConnector(pose, def, c) is not { } f) continue;
            var linked = used.Contains((inst.Id, c.Id));
            var brush = linked ? ThemeBrush("Dex.Good") : ThemeBrush("Dex.Accent");
            var size = Math.Clamp(0.6 * Scale, 5, 12);
            var p = ToScreen(f.Position);
            var dir = new Vector(f.Outward.X, -f.Outward.Y);
            var side = new Vector(-dir.Y, dir.X);
            var tri = new StreamGeometry();
            using (var ctx = tri.Open())
            {
                ctx.BeginFigure(p + side * size * 0.6, true, true);
                ctx.LineTo(p + dir * size, true, false);
                ctx.LineTo(p - side * size * 0.6, true, false);
            }
            tri.Freeze();
            dc.DrawGeometry(brush, null, tri);
        }

        var center = Polygon.Bounds(footprint);
        var labelAt = ToScreen(Vec2.Lerp(center.Min, center.Max, 0.5));
        var label = string.IsNullOrWhiteSpace(inst.Tag) ? def.Name : inst.Tag;
        if ((center.Max.X - center.Min.X) * Scale > 30)
            DrawText(dc, label, labelAt, ThemeBrush("Dex.Canvas.Text"), 11, bold: true, halo: Scale > 10 ? ThemeBrush("Dex.Surface") : null);
    }

    private void DrawLink(DrawingContext dc, Station s, ConnectorLink link, bool selected)
    {
        if (SiteLayout.GetLinkGeometry(s, Resolve, link) is not { } g) return;
        if (g.Corridor is { } corridor)
        {
            var pen = new Pen(selected ? ThemeBrush("Dex.Canvas.Selection") : ThemeBrush("Dex.Canvas.Wall"), selected ? 2.5 : 1.5);
            pen.Freeze();
            dc.DrawGeometry(BrushFromHex("#7D8791", 90), pen, PolylineGeometry(corridor, true, true));
            if (g.Gap * Scale > 50)
                DrawText(dc, UnitFormatter.Dimension(g.Gap, Units), ToScreen(Vec2.Lerp(g.A, g.B, 0.5)), ThemeBrush("Dex.Canvas.Text"), 10);
        }
        else
        {
            var r = Math.Clamp(0.4 * Scale, 4, 9);
            dc.DrawEllipse(ThemeBrush("Dex.Good"), selected ? ThemePen("Dex.Canvas.Selection", 2) : null, ToScreen(g.A), r, r);
        }
    }

    private void DrawRun(DrawingContext dc, Station s, UtilityRun run, bool selected)
    {
        if (SiteLayout.UtilityRoute(s, run) is not { } route) return;
        var brush = BrushFromHex(GlobeSceneBuilder.UtilityColors[run.Kind]);
        var dash = run.Kind switch
        {
            UtilityKind.Fuel => new DashStyle([6, 3], 0),
            UtilityKind.Water => new DashStyle([4, 2], 0),
            UtilityKind.Wastewater => new DashStyle([2, 2], 0),
            UtilityKind.Data => new DashStyle([1, 2], 0),
            UtilityKind.Heat => new DashStyle([8, 2, 2, 2], 0),
            _ => DashStyles.Solid,
        };
        var pen = new Pen(brush, selected ? 4 : 2.5) { DashStyle = dash, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, PolylineGeometry(route, false));
        if (selected)
        {
            var handlePen = ThemePen("Dex.Canvas.Selection", 1.5);
            foreach (var wp in run.Waypoints) dc.DrawEllipse(ThemeBrush("Dex.Canvas.Handle"), handlePen, ToScreen(wp), 5, 5);
        }
    }

    private void DrawPending(DrawingContext dc, Station s)
    {
        if (!_hoverValid && _dragOverModule is null) return;
        var preview = ThemePen("Dex.Canvas.Preview", 1.5, DashStyles.Dash);
        if (_linkStart is { } ls && SiteLayout.WorldConnector(ls.Module.Pose, Resolve(ls.Module.DefinitionId)!, ls.Connector) is { } f)
        {
            dc.DrawLine(preview, ToScreen(f.Position), ToScreen(_hover));
            dc.DrawEllipse(null, ThemePen("Dex.Canvas.Preview", 2), ToScreen(f.Position), 9, 9);
        }
        if (_runStart is { } start)
        {
            var pts = new List<Vec2> { start.Position };
            pts.AddRange(_runWaypoints);
            pts.Add(Snap(_hover));
            var pen = new Pen(BrushFromHex(GlobeSceneBuilder.UtilityColors[UtilityKind]), 2) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            dc.DrawGeometry(null, pen, PolylineGeometry(pts, false));
        }
        if (Tool == SiteTool.Feature && _hoverValid)
        {
            var hover = Snap(_hover);
            if (FeatureKind == SiteFeatureKind.Road || SiteFeatureDefaults.IsTwoPoint(FeatureKind))
            {
                if (_featurePoints.Count > 0)
                {
                    dc.DrawGeometry(null, ThemePen("Dex.Canvas.Preview", 2, DashStyles.Dash), PolylineGeometry(_featurePoints.Append(hover).ToList(), false));
                    DrawText(dc, UnitFormatter.Dimension(Vec2.Distance(_featurePoints[^1], hover), Units),
                        ToScreen(Vec2.Lerp(_featurePoints[^1], hover, 0.5)) + new Vector(0, -14), ThemeBrush("Dex.Canvas.Text"), 11, halo: ThemeBrush("Dex.Surface"));
                }
                foreach (var p in _featurePoints) dc.DrawEllipse(ThemeBrush("Dex.Canvas.Preview"), null, ToScreen(p), 4, 4);
            }
            else
            {
                var ghost = SiteFeatureDefaults.Create(FeatureKind, s.Environment, hover);
                dc.DrawGeometry(null, preview, PolylineGeometry(FeatureGeometry.Outline(ghost)!, true));
            }
        }
        if (_dragOverModule is { } id && Resolve(id) is { } def && ModuleGeometry.HasClosedShell(def))
        {
            var ghost = new ModuleInstance { DefinitionId = id, Position = Snap(_hover) };
            dc.DrawGeometry(BrushFromHex(def.Color, 60), preview, PolylineGeometry(SiteLayout.WorldFootprint(ghost, def), true, true));
        }
    }

    private void DrawNorthArrow(DrawingContext dc, Station s)
    {
        var c = new Point(ActualWidth - 40, 46);
        var text = ThemeBrush("Dex.Canvas.Text");
        var accent = ThemeBrush("Dex.Accent");

        // Grid north is straight up. True north is rotated counterclockwise on screen by the grid heading.
        void Arrow(double degreesFromUp, Brush brush, string label)
        {
            var a = degreesFromUp * Math.PI / 180;
            var dir = new Vector(Math.Sin(a), -Math.Cos(a));
            var side = new Vector(-dir.Y, dir.X);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(c + dir * 22, true, true);
                ctx.LineTo(c - dir * 10 + side * 8, true, false);
                ctx.LineTo(c - dir * 4, true, false);
                ctx.LineTo(c - dir * 10 - side * 8, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(brush, null, g);
            DrawText(dc, label, c + dir * 32, brush, 11, bold: true);
        }

        var heading = s.Site.GridHeadingDegrees;
        if (Math.Abs(Pose2.NormalizeDegrees(heading)) > 0.01)
        {
            Arrow(0, text, "GN");
            Arrow(-heading, accent, "TN");
        }
        else
        {
            Arrow(0, accent, "N");
        }
    }

    #endregion

    #region Hit testing

    private (ModuleInstance Module, ModuleConnector Connector)? HitConnector(Station s, Vec2 w)
    {
        (ModuleInstance, ModuleConnector)? best = null;
        var bestD = double.MaxValue;
        foreach (var inst in s.Modules)
        {
            if (Resolve(inst.DefinitionId) is not { } def) continue;
            foreach (var c in def.Connectors)
            {
                if (SiteLayout.WorldConnector(inst.Pose, def, c) is not { } f) continue;
                var d = Vec2.Distance(f.Position, w) * Scale;
                if (d < ConnectorHitPx && d < bestD) { best = (inst, c); bestD = d; }
            }
        }
        return best;
    }

    private ModuleInstance? HitModule(Station s, Vec2 w)
    {
        for (var i = s.Modules.Count - 1; i >= 0; i--)
        {
            var inst = s.Modules[i];
            if (Resolve(inst.DefinitionId) is not { } def || !ModuleGeometry.HasClosedShell(def)) continue;
            if (Polygon.Contains(SiteLayout.WorldFootprint(inst, def), w)) return inst;
        }
        return null;
    }

    private object? HitTest(Station s, Vec2 w)
    {
        foreach (var run in s.UtilityRuns)
        {
            if (SiteLayout.UtilityRoute(s, run) is not { } route) continue;
            for (var i = 1; i < route.Count; i++)
                if (new Segment(route[i - 1], route[i]).DistanceTo(w) * Scale < 6) return run;
        }
        foreach (var link in s.Links)
        {
            if (SiteLayout.GetLinkGeometry(s, Resolve, link) is not { } g) continue;
            if (g.Corridor is { } c ? Polygon.Contains(c, w) : Vec2.Distance(g.A, w) * Scale < 9) return link;
        }
        if (HitModule(s, w) is { } module) return module;

        // Infrastructure is underneath, so it's hit last; smaller features win over larger ones they sit on.
        return s.Features
            .Where(f => FeatureGeometry.DistanceTo(f, w) * Scale < 6)
            .OrderBy(f => f.Kind == SiteFeatureKind.Road ? 0 : 1)
            .ThenBy(f => f.Length * Math.Max(f.Width, f.Length))
            .FirstOrDefault();
    }

    #endregion

    #region Mouse

    private sealed class ModuleDrag(ModuleInstance module, Vec2 start, Pose2 original)
    {
        public ModuleInstance Module { get; } = module;
        public Vec2 Start { get; } = start;
        public Pose2 Original { get; } = original;
        public SnapResult? Snap { get; set; }
        public bool Moved { get; set; }
    }

    private sealed record WaypointDrag(UtilityRun Run, int Index);

    /// <summary>Moves a whole feature, or one road point when <see cref="PathIndex"/> is set.</summary>
    private sealed class FeatureDrag(SiteFeature feature, Vec2 start, int? pathIndex)
    {
        public SiteFeature Feature { get; } = feature;
        public Vec2 Last { get; set; } = start;
        public int? PathIndex { get; } = pathIndex;
        public bool Moved { get; set; }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (Station is not { } s) return;
        if (TryBeginPan(e)) { e.Handled = true; return; }

        var w = ToWorld(e.GetPosition(this));
        _hover = w;
        _hoverValid = true;

        if (e.ChangedButton == MouseButton.Right)
        {
            if (HasPending) CancelPending();
            else if (Tool != SiteTool.Select) Tool = SiteTool.Select;
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        switch (Tool)
        {
            case SiteTool.Select:
                if (Selection is UtilityRun selectedRun)
                {
                    var idx = selectedRun.Waypoints.ToList().FindIndex(p => Vec2.Distance(p, w) * Scale < 8);
                    if (idx >= 0)
                    {
                        _waypointDrag = new WaypointDrag(selectedRun, idx);
                        CaptureMouse();
                        break;
                    }
                }
                if (Selection is SiteFeature { Kind: SiteFeatureKind.Road } selectedRoad)
                {
                    var idx = selectedRoad.Path.ToList().FindIndex(p => Vec2.Distance(p, w) * Scale < 8);
                    if (idx >= 0)
                    {
                        _featureDrag = new FeatureDrag(selectedRoad, w, idx);
                        CaptureMouse();
                        break;
                    }
                }
                var hit = HitTest(s, w);
                Selection = hit;
                if (hit is ModuleInstance inst)
                {
                    _drag = new ModuleDrag(inst, w, inst.Pose);
                    CaptureMouse();
                }
                else if (hit is SiteFeature feature)
                {
                    _featureDrag = new FeatureDrag(feature, w, null);
                    CaptureMouse();
                }
                else if (hit is null)
                {
                    BeginPan(e.GetPosition(this));
                }
                break;

            case SiteTool.Link:
                LinkClick(s, w);
                break;

            case SiteTool.Utility:
                UtilityClick(s, w);
                break;

            case SiteTool.Feature:
                FeatureClick(s, Snap(w), e.ClickCount);
                break;
        }
        InvalidateVisual();
        e.Handled = true;
    }

    private void FeatureClick(Station s, Vec2 p, int clickCount)
    {
        var kind = FeatureKind;
        if (kind == SiteFeatureKind.Road)
        {
            // Double-click (or Enter) finishes the road.
            if (clickCount == 2 && _featurePoints.Count >= 2) { CommitRoad(s); return; }
            if (_featurePoints.Count == 0 || Vec2.Distance(_featurePoints[^1], p) > 1e-6) _featurePoints.Add(p);
            return;
        }
        if (SiteFeatureDefaults.IsTwoPoint(kind))
        {
            if (_featurePoints.Count == 0) { _featurePoints.Add(p); return; }
            if (Vec2.Distance(_featurePoints[0], p) < 1) return;
            var f = SiteFeatureDefaults.Create(kind, s.Environment, p);
            SiteFeatureDefaults.SetEnds(f, _featurePoints[0], p);
            AddFeature(s, f);
            return;
        }
        AddFeature(s, SiteFeatureDefaults.Create(kind, s.Environment, p));
    }

    private void CommitRoad(Station s)
    {
        if (_featurePoints.Count < 2) return;
        var road = SiteFeatureDefaults.Create(SiteFeatureKind.Road, s.Environment, _featurePoints[0]);
        foreach (var p in _featurePoints) road.Path.Add(p);
        AddFeature(s, road);
    }

    private void AddFeature(Station s, SiteFeature f)
    {
        _featurePoints.Clear();
        s.Features.Add(f);
        Tool = SiteTool.Select;
        Selection = f;
        Complete();
    }

    private void LinkClick(Station s, Vec2 w)
    {
        if (HitConnector(s, w) is not { } hit) return;
        var used = SiteLayout.UsedConnectors(s);
        if (used.Contains((hit.Module.Id, hit.Connector.Id))) return;

        if (_linkStart is not { } start)
        {
            _linkStart = hit;
            return;
        }
        if (start.Module.Id == hit.Module.Id) { _linkStart = hit; return; }

        var link = new ConnectorLink
        {
            ModuleA = start.Module.Id, ConnectorA = start.Connector.Id,
            ModuleB = hit.Module.Id, ConnectorB = hit.Connector.Id,
            CorridorWidth = Math.Max(1.2, Math.Max(start.Connector.Width, hit.Connector.Width) + 0.3),
        };
        s.Links.Add(link);
        _linkStart = null;
        Selection = link;
        Complete();
    }

    private void UtilityClick(Station s, Vec2 w)
    {
        var module = HitModule(s, w);
        if (_runStart is not { } start)
        {
            if (module is not null) _runStart = module;
            return;
        }
        if (module is not null && module.Id != start.Id)
        {
            var run = new UtilityRun { Kind = UtilityKind, FromModule = start.Id, ToModule = module.Id };
            foreach (var p in _runWaypoints) run.Waypoints.Add(p);
            s.UtilityRuns.Add(run);
            CancelPending();
            Selection = run;
            Complete();
            return;
        }
        if (module is null) _runWaypoints.Add(Snap(w));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsPanning || Station is not { } s) return;
        var w = ToWorld(e.GetPosition(this));
        _hover = w;
        _hoverValid = true;

        if (_waypointDrag is { } wd && e.LeftButton == MouseButtonState.Pressed)
        {
            wd.Run.Waypoints[wd.Index] = Snap(w);
        }
        else if (_featureDrag is { } fd && e.LeftButton == MouseButtonState.Pressed)
        {
            if (fd.PathIndex is { } i)
            {
                fd.Feature.Path[i] = Snap(w);
            }
            else
            {
                // Move by snapped steps so features stay on the grid they were placed on.
                var step = Snap(w) - Snap(fd.Last);
                if (step.LengthSquared > 0)
                {
                    FeatureGeometry.Translate(fd.Feature, step);
                    fd.Last = w;
                }
            }
            fd.Moved = true;
        }
        else if (_drag is { } d && e.LeftButton == MouseButtonState.Pressed)
        {
            var proposed = new Pose2(Snap(d.Original.Position + (w - d.Start)), d.Original.RotationDegrees);
            var threshold = Math.Max(1.0, 18 / Scale);
            d.Snap = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? null : SiteLayout.FindSnap(s, Resolve, d.Module, proposed, threshold);
            var pose = d.Snap?.Pose ?? proposed;
            d.Module.Position = pose.Position;
            d.Module.RotationDegrees = pose.RotationDegrees;
            d.Moved = true;
        }

        Cursor = Tool switch
        {
            SiteTool.Link => HitConnector(s, w) is null ? Cursors.Cross : Cursors.Hand,
            SiteTool.Utility => HitModule(s, w) is null ? Cursors.Cross : Cursors.Hand,
            SiteTool.Feature => Cursors.Cross,
            _ => _drag is not null ? Cursors.SizeAll : HitTest(s, w) is null ? Cursors.Arrow : Cursors.SizeAll,
        };
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (IsPanning) { base.OnMouseUp(e); return; }
        if (_waypointDrag is not null)
        {
            _waypointDrag = null;
            ReleaseMouseCapture();
            Complete();
        }
        if (_featureDrag is { } fd)
        {
            _featureDrag = null;
            ReleaseMouseCapture();
            if (fd.Moved) Complete();
        }
        if (_drag is { } d && Station is { } s)
        {
            _drag = null;
            ReleaseMouseCapture();
            if (d.Snap is { } snap)
            {
                // Mating two connectors links them automatically.
                var exists = s.Links.Any(l =>
                    (l.ModuleA == d.Module.Id && l.ConnectorA == snap.MovingConnector && l.ModuleB == snap.TargetModule && l.ConnectorB == snap.TargetConnector) ||
                    (l.ModuleB == d.Module.Id && l.ConnectorB == snap.MovingConnector && l.ModuleA == snap.TargetModule && l.ConnectorA == snap.TargetConnector));
                if (!exists)
                    s.Links.Add(new ConnectorLink { ModuleA = snap.TargetModule, ConnectorA = snap.TargetConnector, ModuleB = d.Module.Id, ConnectorB = snap.MovingConnector });
            }
            if (d.Moved) Complete();
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverValid = false;
        InvalidateVisual();
    }

    #endregion

    #region Keyboard

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.Escape:
                if (HasPending) CancelPending();
                else if (Tool != SiteTool.Select) Tool = SiteTool.Select;
                else Selection = null;
                e.Handled = true;
                break;
            case Key.Enter when _runStart is not null:
                CancelPending();
                e.Handled = true;
                break;
            case Key.Enter when Tool == SiteTool.Feature && FeatureKind == SiteFeatureKind.Road && Station is { } station:
                CommitRoad(station);
                e.Handled = true;
                break;
            case Key.Back when _featurePoints.Count > 0:
                _featurePoints.RemoveAt(_featurePoints.Count - 1);
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Delete:
                DeleteSelection();
                e.Handled = true;
                break;
            case Key.R:
                RotateSelection(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1,
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 15 : 90);
                e.Handled = true;
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down when Selection is ModuleInstance inst:
            {
                var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? GridStep * 10 : GridStep;
                inst.Position += e.Key switch
                {
                    Key.Left => new Vec2(-step, 0),
                    Key.Right => new Vec2(step, 0),
                    Key.Up => new Vec2(0, step),
                    _ => new Vec2(0, -step),
                };
                Complete();
                e.Handled = true;
                break;
            }
        }
    }

    private bool HasPending => _linkStart is not null || _runStart is not null || _featurePoints.Count > 0;

    public void RotateSelection(int direction, double degrees = 90)
    {
        switch (Selection)
        {
            case ModuleInstance inst:
                inst.RotationDegrees = Pose2.NormalizeDegrees(inst.RotationDegrees + direction * degrees);
                break;
            case SiteFeature { Kind: not SiteFeatureKind.Road } f:
                f.RotationDegrees = Pose2.NormalizeDegrees(f.RotationDegrees + direction * degrees);
                break;
            default:
                return;
        }
        Complete();
    }

    public void DeleteSelection()
    {
        if (Station is not { } s) return;
        switch (Selection)
        {
            case ModuleInstance inst: s.Modules.Remove(inst); break;
            case ConnectorLink link: s.Links.Remove(link); break;
            case UtilityRun run: s.UtilityRuns.Remove(run); break;
            case SiteFeature f: s.Features.Remove(f); break;
            default: return;
        }
        Selection = null;
        Complete();
    }

    #endregion

    #region Drag and drop from the module library

    protected override void OnDragEnter(DragEventArgs e) => OnDragOver(e);

    protected override void OnDragOver(DragEventArgs e)
    {
        if (e.Data.GetData(ModuleDragFormat) is string id && Guid.TryParse(id, out var guid) && Station is not null)
        {
            e.Effects = DragDropEffects.Copy;
            _dragOverModule = guid;
            _hover = ToWorld(e.GetPosition(this));
            _hoverValid = true;
            InvalidateVisual();
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        _dragOverModule = null;
        InvalidateVisual();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        _dragOverModule = null;
        if (e.Data.GetData(ModuleDragFormat) is string id && Guid.TryParse(id, out var guid) && Station is { } s && Resolve(guid) is { } def)
            PlaceModule(s, def, Snap(ToWorld(e.GetPosition(this))));
        e.Handled = true;
    }

    /// <summary>Adds an instance of <paramref name="def"/> at <paramref name="at"/> (or the view center).</summary>
    public ModuleInstance? PlaceModule(Station s, ModuleDefinition def, Vec2? at = null)
    {
        var position = at ?? Snap(ToWorld(new Point(ActualWidth / 2, ActualHeight / 2)));
        var inst = new ModuleInstance { DefinitionId = def.Id, Position = position, Tag = SiteLayout.NextTag(s, def.Category) };
        s.Modules.Add(inst);
        Selection = inst;
        Focus();
        Complete();
        return inst;
    }

    #endregion
}
