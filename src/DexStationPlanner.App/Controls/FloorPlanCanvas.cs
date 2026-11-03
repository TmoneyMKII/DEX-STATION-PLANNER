using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DexStationPlanner.App.Editors;
using DexStationPlanner.App.Services;
using DexStationPlanner.Core.Equipment;
using DexStationPlanner.Core.Geometry;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Modules;
using DexStationPlanner.Core.Units;

namespace DexStationPlanner.App.Controls;

/// <summary>
/// The 2D floor-plan editor for a <see cref="ModuleDefinition"/>: draw the shell, interior walls (straight or
/// curved), doors/windows, connectors and equipment on a snapping grid.
/// </summary>
public sealed class FloorPlanCanvas : PlanCanvasBase
{
    public const string EquipmentDragFormat = "DexStationPlanner.EquipmentId";
    private const double HandleRadius = 5;
    private const double HitTolerance = 7;

    private DeepChangeObserver? _observer;
    private Drag? _drag;
    private readonly List<Vec2> _pendingShell = [];
    private Vec2? _wallStart;
    private (Vec2 A, Vec2 B)? _arcChord;
    private Vec2 _hover;
    private bool _hoverValid;
    private double _ghostRotation;
    private string? _dragOverEquipment;
    private Guid? _lastModuleId;

    public FloorPlanCanvas()
    {
        AllowDrop = true;
    }

    public event EventHandler? EditCompleted;

    #region Dependency properties

    public static readonly DependencyProperty ModuleProperty = DependencyProperty.Register(
        nameof(Module), typeof(ModuleDefinition), typeof(FloorPlanCanvas),
        new PropertyMetadata(null, (d, e) => ((FloorPlanCanvas)d).OnModuleChanged()));

    public ModuleDefinition? Module
    {
        get => (ModuleDefinition?)GetValue(ModuleProperty);
        set => SetValue(ModuleProperty, value);
    }

    public static readonly DependencyProperty CatalogProperty = DependencyProperty.Register(
        nameof(Catalog), typeof(EquipmentLookup), typeof(FloorPlanCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public EquipmentLookup? Catalog
    {
        get => (EquipmentLookup?)GetValue(CatalogProperty);
        set => SetValue(CatalogProperty, value);
    }

    public static readonly DependencyProperty ToolProperty = DependencyProperty.Register(
        nameof(Tool), typeof(ModuleTool), typeof(FloorPlanCanvas),
        new FrameworkPropertyMetadata(ModuleTool.Select, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((FloorPlanCanvas)d).CancelPending()));

    public ModuleTool Tool
    {
        get => (ModuleTool)GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
        nameof(Selection), typeof(object), typeof(FloorPlanCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public object? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    public static readonly DependencyProperty PlacingEquipmentIdProperty = DependencyProperty.Register(
        nameof(PlacingEquipmentId), typeof(string), typeof(FloorPlanCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Catalog id placed by clicks while <see cref="Tool"/> is <see cref="ModuleTool.Equipment"/>.</summary>
    public string? PlacingEquipmentId
    {
        get => (string?)GetValue(PlacingEquipmentIdProperty);
        set => SetValue(PlacingEquipmentIdProperty, value);
    }

    public static readonly DependencyProperty UnitsProperty = DependencyProperty.Register(
        nameof(Units), typeof(UnitSystem), typeof(FloorPlanCanvas),
        new FrameworkPropertyMetadata(UnitSystem.Metric, FrameworkPropertyMetadataOptions.AffectsRender));

    public UnitSystem Units
    {
        get => (UnitSystem)GetValue(UnitsProperty);
        set => SetValue(UnitsProperty, value);
    }

    #endregion

    private void OnModuleChanged()
    {
        _observer?.Dispose();
        _observer = null;
        CancelPending();
        _drag = null;
        if (Module is { } m)
        {
            _observer = new DeepChangeObserver(m);
            _observer.Changed += (_, _) => InvalidateVisual();
            // Undo swaps in a copy with the same id; keep the user's view in that case.
            if (m.Id != _lastModuleId) ZoomToFit();
            _lastModuleId = m.Id;
        }
        InvalidateVisual();
    }

    protected override (Vec2 Min, Vec2 Max)? ContentBounds()
    {
        if (Module is not { } m) return null;
        var pts = m.Shell.Select(v => v.Position).Concat(m.InteriorWalls.SelectMany(w => new[] { w.Start, w.End })).ToList();
        if (pts.Count == 0) return null;
        var (min, max) = Polygon.Bounds(pts);
        return (min - new Vec2(1, 1), max + new Vec2(1, 1));
    }

    private void CancelPending()
    {
        _pendingShell.Clear();
        _wallStart = null;
        _arcChord = null;
        InvalidateVisual();
    }

    private void Complete()
    {
        if (Module is { } m) ModuleGeometry.ClampAttachments(m);
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    #region Rendering

    protected override void RenderContent(DrawingContext dc)
    {
        if (Module is not { } m) return;
        var catalog = Catalog ?? new EquipmentLookup();
        var edges = ModuleGeometry.ShellEdges(m);
        var closed = m.Shell.Count >= 3;

        if (closed)
            dc.DrawGeometry(ThemeBrush("Dex.Canvas.ShellFill"), null, PolylineGeometry(ModuleGeometry.Footprint(m, Math.PI / 72), true, true));

        foreach (var item in m.Equipment) DrawEquipment(dc, item, catalog.Find(item.DefinitionId), ReferenceEquals(Selection, item));

        foreach (var w in m.InteriorWalls)
            DrawWallStroke(dc, w.Segment, w.Thickness, ThemeBrush("Dex.Canvas.Wall"));

        if (m.Shell.Count >= 2)
        {
            var shellBrush = ThemeBrush("Dex.Canvas.Shell");
            foreach (var e in edges)
            {
                if (!closed && e.Index == m.Shell.Count - 1) break;
                DrawWallStroke(dc, e.Segment, m.ShellThickness, shellBrush);
            }
        }

        foreach (var o in m.Openings) DrawOpening(dc, m, o, ReferenceEquals(Selection, o));
        foreach (var c in m.Connectors) DrawConnector(dc, m, c, ReferenceEquals(Selection, c));

        if (closed && Scale > 25) DrawEdgeDimensions(dc, m, edges);
        DrawSelection(dc, m, edges);
        DrawToolPreview(dc, m, catalog);
    }

    private void DrawWallStroke(DrawingContext dc, Segment seg, double thickness, Brush brush)
    {
        var pen = new Pen(brush, Math.Max(2, thickness * Scale)) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, PolylineGeometry(seg.Tessellate(Math.PI / 72), false));
    }

    private void DrawEquipment(DrawingContext dc, EquipmentPlacement item, EquipmentDefinition? def, bool selected)
    {
        if (def is null)
        {
            var p = ToScreen(item.Position);
            dc.DrawEllipse(Brushes.Transparent, ThemePen("Dex.Warning", 2), p, 8, 8);
            DrawText(dc, "?", p, ThemeBrush("Dex.Warning"), 11, bold: true);
            return;
        }
        var fill = BrushFromHex(def.Color, 150);
        var stroke = new Pen(selected ? ThemeBrush("Dex.Canvas.Selection") : BrushFromHex(def.Color), selected ? 2 : 1.2);
        stroke.Freeze();
        if (def.Shape == EquipmentShape.Cylinder)
        {
            var r = Math.Min(def.Width, def.Depth) / 2 * Scale;
            dc.DrawEllipse(fill, stroke, ToScreen(item.Position), r, r);
        }
        else
        {
            dc.DrawGeometry(fill, stroke, PolylineGeometry(ModuleGeometry.EquipmentCorners(item, def), true, true));
        }
        // Label along the item's long side when it fits, falling back to a smaller font.
        var label = string.IsNullOrWhiteSpace(item.Label) ? def.Name : item.Label;
        var quarterTurn = Math.Abs(Math.Sin(item.RotationDegrees * Math.PI / 180)) > 0.7;
        var screenWidth = (quarterTurn ? def.Depth : def.Width) * Scale - 6;
        var screenHeight = (quarterTurn ? def.Width : def.Depth) * Scale;
        if (screenHeight > 14)
        {
            var brush = ThemeBrush("Dex.Canvas.Text");
            _ = DrawTextIfFits(dc, label, ToScreen(item.Position), brush, 11, screenWidth)
                || DrawTextIfFits(dc, label, ToScreen(item.Position), brush, 9, screenWidth);
        }
    }

    private void DrawOpening(DrawingContext dc, ModuleDefinition m, Opening o, bool selected)
    {
        if (ModuleGeometry.FindHostSegment(m, o.HostId) is not { } seg) return;
        var isShell = ModuleGeometry.FindEdge(m, o.HostId) is not null;
        var thickness = isShell ? m.ShellThickness : m.InteriorWalls.First(w => w.Id == o.HostId).Thickness;
        var s0 = Math.Max(0, o.Offset - o.Width / 2);
        var s1 = Math.Min(seg.Length, o.Offset + o.Width / 2);
        var gap = SubPath(seg, s0, s1);

        // Cut the wall by painting the gap in the floor color, then draw the symbol.
        var cutPen = new Pen(ThemeBrush("Dex.Canvas.ShellFill"), Math.Max(2, thickness * Scale) + 2) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        cutPen.Freeze();
        dc.DrawGeometry(null, cutPen, PolylineGeometry(gap, false));

        var symbolBrush = selected ? ThemeBrush("Dex.Canvas.Selection") : ThemeBrush(o.Kind == OpeningKind.Window ? "Dex.Canvas.Window" : "Dex.Canvas.Door");
        var pen = new Pen(symbolBrush, selected ? 2.2 : 1.4);
        pen.Freeze();
        var a = seg.PointAt(s0);
        var b = seg.PointAt(s1);
        var inward = InwardNormal(m, seg, o.Offset, isShell);
        var half = thickness / 2;

        switch (o.Kind)
        {
            case OpeningKind.Door:
            {
                // Door leaf hinged at A, swung 90° into the room, with its swing arc.
                var leafEnd = a + inward * (s1 - s0);
                dc.DrawLine(pen, ToScreen(a), ToScreen(leafEnd));
                var arc = new List<Vec2>();
                for (var i = 0; i <= 16; i++)
                {
                    var t = i / 16.0;
                    var dir = Vec2.Lerp((b - a).Normalized(), inward, t).Normalized();
                    arc.Add(a + dir * (s1 - s0));
                }
                var thin = new Pen(symbolBrush, 1) { DashStyle = DashStyles.Dash };
                thin.Freeze();
                dc.DrawGeometry(null, thin, PolylineGeometry(arc, false));
                break;
            }
            case OpeningKind.Window:
            {
                var n = seg.TangentAt(o.Offset).LeftNormal * half;
                dc.DrawGeometry(null, pen, PolylineGeometry(gap.Select(p => p + n).ToList(), false));
                dc.DrawGeometry(null, pen, PolylineGeometry(gap.Select(p => p - n).ToList(), false));
                dc.DrawGeometry(null, pen, PolylineGeometry(gap, false));
                break;
            }
            default:
            {
                var n = seg.TangentAt(o.Offset).LeftNormal * half;
                var box = new List<Vec2> { a + n, b + n, b - n, a - n };
                var dashed = new Pen(symbolBrush, 1.4) { DashStyle = o.Kind == OpeningKind.Vent ? DashStyles.Dot : DashStyles.Dash };
                dashed.Freeze();
                dc.DrawGeometry(null, dashed, PolylineGeometry(box, true));
                break;
            }
        }
        if (selected) DrawHandle(dc, seg.PointAt(o.Offset), round: true);
    }

    private void DrawConnector(DrawingContext dc, ModuleDefinition m, ModuleConnector c, bool selected)
    {
        if (ModuleGeometry.ConnectorFrame(m, c) is not { } f) return;
        var accent = selected ? ThemeBrush("Dex.Canvas.Selection") : ThemeBrush("Dex.Accent");
        var along = f.Outward.RightNormal;
        var half = c.Width / 2;
        var bar = new Pen(accent, 3);
        bar.Freeze();
        var offset = f.Outward * (m.ShellThickness / 2 + 0.05);
        dc.DrawLine(bar, ToScreen(f.Position + offset - along * half), ToScreen(f.Position + offset + along * half));

        // Outward-pointing chevron.
        var tipLen = Math.Max(0.35, 14 / Scale);
        var tip = f.Position + offset + f.Outward * tipLen;
        var tri = new List<Vec2> { f.Position + offset - along * (tipLen * 0.6), tip, f.Position + offset + along * (tipLen * 0.6) };
        dc.DrawGeometry(accent, null, PolylineGeometry(tri, true, true));
        // Names would collide with edge dimensions, so only the selected connector is labeled.
        if (selected)
            DrawText(dc, c.Name, ToScreen(tip + f.Outward * (14 / Scale)), accent, 11, bold: true, halo: ThemeBrush("Dex.Surface"));
    }

    private void DrawEdgeDimensions(DrawingContext dc, ModuleDefinition m, List<ShellEdge> edges)
    {
        var brush = ThemeBrush("Dex.Canvas.Dimension");
        var winding = ModuleGeometry.ShellWinding(m);
        foreach (var e in edges)
        {
            var len = e.Segment.Length;
            if (len * Scale < 60) continue;
            var mid = e.Segment.PointAt(len / 2);
            var tangent = e.Segment.TangentAt(len / 2);
            var outward = winding > 0 ? tangent.RightNormal : tangent.LeftNormal;
            DrawText(dc, UnitFormatter.Dimension(len, Units), ToScreen(mid + outward * (m.ShellThickness / 2 + 16 / Scale)), brush, 10);
        }
    }

    private void DrawSelection(DrawingContext dc, ModuleDefinition m, List<ShellEdge> edges)
    {
        var sel = ThemePen("Dex.Canvas.Selection", 2);
        switch (Selection)
        {
            case EdgeSelection es when ModuleGeometry.FindEdge(m, es.Start.Id) is { } edge:
                dc.DrawGeometry(null, sel, PolylineGeometry(edge.Segment.Tessellate(Math.PI / 72), false));
                DrawHandle(dc, edge.Segment.PointAt(edge.Segment.Length / 2), round: true);
                DrawText(dc, UnitFormatter.Dimension(edge.Segment.Length, Units),
                    ToScreen(edge.Segment.PointAt(edge.Segment.Length / 2)) + new Vector(0, -16), ThemeBrush("Dex.Canvas.Text"), 11, halo: ThemeBrush("Dex.Surface"));
                break;
            case InteriorWall w:
                dc.DrawGeometry(null, sel, PolylineGeometry(w.Segment.Tessellate(Math.PI / 72), false));
                DrawHandle(dc, w.Start);
                DrawHandle(dc, w.End);
                DrawHandle(dc, w.Segment.PointAt(w.Segment.Length / 2), round: true);
                DrawText(dc, UnitFormatter.Dimension(w.Segment.Length, Units),
                    ToScreen(w.Segment.PointAt(w.Segment.Length / 2)) + new Vector(0, -16), ThemeBrush("Dex.Canvas.Text"), 11, halo: ThemeBrush("Dex.Surface"));
                break;
        }

        if (Tool == ModuleTool.Select)
            foreach (var v in m.Shell) DrawHandle(dc, v.Position, selected: ReferenceEquals(Selection, v));
    }

    private void DrawHandle(DrawingContext dc, Vec2 at, bool round = false, bool selected = false)
    {
        var p = ToScreen(at);
        var fill = selected ? ThemeBrush("Dex.Canvas.Selection") : ThemeBrush("Dex.Canvas.Handle");
        var pen = ThemePen("Dex.Canvas.Selection", 1.5);
        if (round) dc.DrawEllipse(fill, pen, p, HandleRadius, HandleRadius);
        else dc.DrawRectangle(fill, pen, new Rect(p.X - HandleRadius, p.Y - HandleRadius, HandleRadius * 2, HandleRadius * 2));
    }

    private void DrawToolPreview(DrawingContext dc, ModuleDefinition m, EquipmentLookup catalog)
    {
        if (!_hoverValid && _dragOverEquipment is null) return;
        var preview = ThemePen("Dex.Canvas.Preview", 1.5, DashStyles.Dash);
        var textBrush = ThemeBrush("Dex.Canvas.Text");
        var halo = ThemeBrush("Dex.Surface");
        var hover = ToolPoint(_hover);

        switch (Tool)
        {
            case ModuleTool.Shell when _pendingShell.Count > 0:
            {
                var pts = _pendingShell.Append(hover).ToList();
                dc.DrawGeometry(null, preview, PolylineGeometry(pts, false));
                foreach (var p in _pendingShell) DrawHandle(dc, p);
                if (_pendingShell.Count >= 3 && Vec2.Distance(ToScreen(hover).ToVec(), ToScreen(_pendingShell[0]).ToVec()) < HitTolerance * 1.5)
                    dc.DrawEllipse(null, ThemePen("Dex.Canvas.Preview", 2), ToScreen(_pendingShell[0]), 10, 10);
                DrawLengthLabel(dc, _pendingShell[^1], hover, textBrush, halo);
                break;
            }
            case ModuleTool.Wall when _wallStart is { } start:
                dc.DrawLine(preview, ToScreen(start), ToScreen(hover));
                DrawLengthLabel(dc, start, hover, textBrush, halo);
                break;
            case ModuleTool.ArcWall when _arcChord is { } chord:
            {
                var bulge = Segment.BulgeFromSagitta(chord.A, chord.B, _hover);
                dc.DrawGeometry(null, preview, PolylineGeometry(new Segment(chord.A, chord.B, bulge).Tessellate(Math.PI / 72), false));
                break;
            }
            case ModuleTool.ArcWall when _wallStart is { } start:
                dc.DrawLine(preview, ToScreen(start), ToScreen(hover));
                DrawLengthLabel(dc, start, hover, textBrush, halo);
                break;
            case ModuleTool.Door or ModuleTool.Window or ModuleTool.Connector:
            {
                var host = Tool == ModuleTool.Connector ? NearestShellEdge(m, _hover) : NearestHost(m, _hover);
                if (host is { } h)
                {
                    var width = Tool switch { ModuleTool.Window => 0.9, ModuleTool.Connector => 1.2, _ => 0.9 };
                    var s = Math.Clamp(h.S, Math.Min(width / 2, h.Seg.Length / 2), Math.Max(h.Seg.Length - width / 2, h.Seg.Length / 2));
                    var pen = ThemePen("Dex.Canvas.Preview", Math.Max(4, m.ShellThickness * Scale));
                    dc.DrawGeometry(null, pen, PolylineGeometry(SubPath(h.Seg, s - width / 2, s + width / 2), false));
                }
                break;
            }
            case ModuleTool.Equipment:
            {
                var id = _dragOverEquipment ?? PlacingEquipmentId;
                if (id is null || catalog.Find(id) is not { } def) break;
                var ghost = new EquipmentPlacement { DefinitionId = id, Position = Snap(_hover), RotationDegrees = _ghostRotation };
                dc.DrawGeometry(BrushFromHex(def.Color, 90), preview, PolylineGeometry(ModuleGeometry.EquipmentCorners(ghost, def), true, true));
                break;
            }
        }
    }

    private void DrawLengthLabel(DrawingContext dc, Vec2 a, Vec2 b, Brush text, Brush halo)
    {
        var len = Vec2.Distance(a, b);
        if (len < 1e-6) return;
        DrawText(dc, UnitFormatter.Dimension(len, Units), ToScreen(Vec2.Lerp(a, b, 0.5)) + new Vector(0, -14), text, 11, halo: halo);
    }

    private static List<Vec2> SubPath(Segment seg, double s0, double s1)
    {
        var pts = new List<Vec2>();
        var steps = seg.IsArc ? Math.Max(2, (int)Math.Ceiling(Math.Abs(seg.Sweep) * (s1 - s0) / Math.Max(seg.Length, 1e-9) / (Math.PI / 72))) : 1;
        for (var i = 0; i <= steps; i++) pts.Add(seg.PointAt(s0 + (s1 - s0) * i / steps));
        return pts;
    }

    private static Vec2 InwardNormal(ModuleDefinition m, Segment seg, double s, bool isShell)
    {
        var t = seg.TangentAt(s);
        if (!isShell) return t.LeftNormal;
        return ModuleGeometry.ShellWinding(m) > 0 ? t.LeftNormal : t.RightNormal;
    }

    #endregion

    #region Hit testing

    private readonly record struct HostHit(Guid HostId, Segment Seg, double S, double DistancePx);

    private HostHit? NearestHost(ModuleDefinition m, Vec2 world)
    {
        HostHit? best = null;
        var closed = m.Shell.Count >= 3;
        foreach (var e in ModuleGeometry.ShellEdges(m))
        {
            if (!closed && e.Index == m.Shell.Count - 1) break;
            Consider(e.Vertex.Id, e.Segment, m.ShellThickness);
        }
        foreach (var w in m.InteriorWalls) Consider(w.Id, w.Segment, w.Thickness);
        return best;

        void Consider(Guid id, Segment seg, double thickness)
        {
            var s = seg.ProjectDistance(world);
            var d = Vec2.Distance(seg.PointAt(s), world) * Scale;
            if (d <= Math.Max(HitTolerance * 2, thickness * Scale / 2 + HitTolerance) && (best is null || d < best.Value.DistancePx))
                best = new HostHit(id, seg, s, d);
        }
    }

    private HostHit? NearestShellEdge(ModuleDefinition m, Vec2 world)
    {
        if (m.Shell.Count < 3) return null;
        HostHit? best = null;
        foreach (var e in ModuleGeometry.ShellEdges(m))
        {
            var s = e.Segment.ProjectDistance(world);
            var d = Vec2.Distance(e.Segment.PointAt(s), world) * Scale;
            if (d <= HitTolerance * 2 + m.ShellThickness * Scale / 2 && (best is null || d < best.Value.DistancePx))
                best = new HostHit(e.Vertex.Id, e.Segment, s, d);
        }
        return best;
    }

    private bool NearScreen(Vec2 a, Vec2 b, double tolerance = HitTolerance) =>
        Vec2.Distance(a, b) * Scale <= tolerance;

    private object? HitTest(ModuleDefinition m, Vec2 w, EquipmentLookup catalog)
    {
        foreach (var v in m.Shell) if (NearScreen(v.Position, w)) return v;

        foreach (var c in m.Connectors)
            if (ModuleGeometry.ConnectorFrame(m, c) is { } f && NearScreen(f.Position + f.Outward * (m.ShellThickness / 2 + Math.Max(0.35, 14 / Scale) / 2), w, HitTolerance * 1.5))
                return c;

        foreach (var o in m.Openings)
        {
            if (ModuleGeometry.FindHostSegment(m, o.HostId) is not { } seg) continue;
            var s = seg.ProjectDistance(w);
            if (Math.Abs(s - o.Offset) <= o.Width / 2 && Vec2.Distance(seg.PointAt(s), w) * Scale <= HitTolerance + m.ShellThickness * Scale / 2)
                return o;
        }

        // Topmost equipment first (drawn last).
        for (var i = m.Equipment.Count - 1; i >= 0; i--)
        {
            var item = m.Equipment[i];
            if (catalog.Find(item.DefinitionId) is not { } def)
            {
                if (NearScreen(item.Position, w, 10)) return item;
                continue;
            }
            var local = item.Pose.Inverse(w);
            if (Math.Abs(local.X) <= def.Width / 2 && Math.Abs(local.Y) <= def.Depth / 2) return item;
        }

        foreach (var wall in m.InteriorWalls)
            if (wall.Segment.DistanceTo(w) * Scale <= wall.Thickness * Scale / 2 + HitTolerance) return wall;

        if (NearestShellEdge(m, w) is { } edge) return new EdgeSelection(m.Shell.First(v => v.Id == edge.HostId));
        return null;
    }

    #endregion

    #region Mouse

    private abstract record Drag(Vec2 StartWorld)
    {
        public bool Moved { get; set; }
    }

    private sealed record VertexDrag(Vec2 StartWorld, ShellVertex Vertex, Vec2 Original) : Drag(StartWorld);
    private sealed record EdgeDrag(Vec2 StartWorld, ShellVertex A, ShellVertex B, Vec2 OrigA, Vec2 OrigB) : Drag(StartWorld);
    private sealed record WallMoveDrag(Vec2 StartWorld, InteriorWall Wall, Vec2 OrigStart, Vec2 OrigEnd) : Drag(StartWorld);
    private sealed record WallEndDrag(Vec2 StartWorld, InteriorWall Wall, bool IsStart) : Drag(StartWorld);
    private sealed record BulgeDrag(Vec2 StartWorld, ShellVertex? Vertex, InteriorWall? Wall) : Drag(StartWorld);
    private sealed record OpeningDrag(Vec2 StartWorld, Opening Opening) : Drag(StartWorld);
    private sealed record ConnectorDrag(Vec2 StartWorld, ModuleConnector Connector) : Drag(StartWorld);
    private sealed record EquipmentDrag(Vec2 StartWorld, EquipmentPlacement Item, Vec2 Original) : Drag(StartWorld);

    /// <summary>Snapped point for drawing tools, honoring Shift for 45° constraints from the previous point.</summary>
    private Vec2 ToolPoint(Vec2 raw)
    {
        var anchor = Tool switch
        {
            ModuleTool.Shell when _pendingShell.Count > 0 => _pendingShell[^1],
            ModuleTool.Wall or ModuleTool.ArcWall when _arcChord is null => _wallStart,
            _ => (Vec2?)null,
        };
        var p = SnapToFeatures(raw) ?? Snap(raw);
        if (anchor is { } a && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var d = raw - a;
            var angle = Math.Round(d.Angle / (Math.PI / 4)) * (Math.PI / 4);
            var len = Snap(new Vec2(d.Length, 0)).X;
            p = a + Vec2.FromAngle(angle, len);
        }
        return p;
    }

    /// <summary>Snaps to existing shell corners and wall endpoints within a few pixels.</summary>
    private Vec2? SnapToFeatures(Vec2 raw, object? exclude = null)
    {
        if (Module is not { } m || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return null;
        var candidates = m.Shell.Where(v => !ReferenceEquals(v, exclude)).Select(v => v.Position)
            .Concat(m.InteriorWalls.Where(w => !ReferenceEquals(w, exclude)).SelectMany(w => new[] { w.Start, w.End }))
            .Concat(_pendingShell);
        foreach (var c in candidates)
            if (NearScreen(c, raw, HitTolerance * 1.4)) return c;
        return null;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (Module is not { } m) return;
        if (TryBeginPan(e)) { e.Handled = true; return; }

        var w = ToWorld(e.GetPosition(this));
        _hover = w;
        _hoverValid = true;

        if (e.ChangedButton == MouseButton.Right)
        {
            if (_pendingShell.Count > 0 || _wallStart is not null || _arcChord is not null) CancelPending();
            else if (Tool != ModuleTool.Select) Tool = ModuleTool.Select;
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        switch (Tool)
        {
            case ModuleTool.Select:
                BeginSelectDrag(m, w, e.ClickCount);
                break;
            case ModuleTool.Shell:
                ShellClick(m, ToolPoint(w), e.ClickCount);
                break;
            case ModuleTool.Wall:
                WallClick(m, ToolPoint(w));
                break;
            case ModuleTool.ArcWall:
                ArcClick(m, ToolPoint(w), w);
                break;
            case ModuleTool.Door:
            case ModuleTool.Window:
                PlaceOpening(m, w, Tool == ModuleTool.Door ? OpeningKind.Door : OpeningKind.Window);
                break;
            case ModuleTool.Connector:
                PlaceConnector(m, w);
                break;
            case ModuleTool.Equipment:
                if (PlacingEquipmentId is { } id) PlaceEquipment(m, id, w);
                break;
        }
        InvalidateVisual();
        e.Handled = true;
    }

    private void BeginSelectDrag(ModuleDefinition m, Vec2 w, int clickCount)
    {
        var catalog = Catalog ?? new EquipmentLookup();

        // Handles of the current selection take priority.
        switch (Selection)
        {
            case EdgeSelection es when ModuleGeometry.FindEdge(m, es.Start.Id) is { } edge
                                      && NearScreen(edge.Segment.PointAt(edge.Segment.Length / 2), w):
                _drag = new BulgeDrag(w, es.Start, null);
                CaptureMouse();
                return;
            case InteriorWall wall when NearScreen(wall.Segment.PointAt(wall.Segment.Length / 2), w):
                _drag = new BulgeDrag(w, null, wall);
                CaptureMouse();
                return;
            case InteriorWall wall when NearScreen(wall.Start, w) || NearScreen(wall.End, w):
                _drag = new WallEndDrag(w, wall, NearScreen(wall.Start, w));
                CaptureMouse();
                return;
        }

        var hit = HitTest(m, w, catalog);

        if (clickCount == 2 && hit is EdgeSelection dbl)
        {
            InsertVertex(m, dbl.Start, w);
            return;
        }

        Selection = hit;
        _drag = hit switch
        {
            ShellVertex v => new VertexDrag(w, v, v.Position),
            EdgeSelection es => EdgeDragFor(m, es, w),
            InteriorWall wall => new WallMoveDrag(w, wall, wall.Start, wall.End),
            Opening o => new OpeningDrag(w, o),
            ModuleConnector c => new ConnectorDrag(w, c),
            EquipmentPlacement item => new EquipmentDrag(w, item, item.Position),
            _ => null,
        };
        if (_drag is not null) CaptureMouse();
        else BeginPan(ToScreen(w)); // Dragging empty space pans.
    }

    private static EdgeDrag EdgeDragFor(ModuleDefinition m, EdgeSelection es, Vec2 w)
    {
        var i = m.Shell.IndexOf(es.Start);
        var next = m.Shell[(i + 1) % m.Shell.Count];
        return new EdgeDrag(w, es.Start, next, es.Start.Position, next.Position);
    }

    private void InsertVertex(ModuleDefinition m, ShellVertex start, Vec2 w)
    {
        if (ModuleGeometry.FindEdge(m, start.Id) is not { } edge) return;
        var seg = edge.Segment;
        var s = seg.ProjectDistance(w);
        if (s < 0.05 || s > seg.Length - 0.05) return;
        var t = s / seg.Length;
        var sweep = seg.Sweep;
        var newVertex = new ShellVertex { Position = seg.PointAt(s), Bulge = seg.IsArc ? Math.Tan(sweep * (1 - t) / 4) : 0 };
        if (seg.IsArc) start.Bulge = Math.Tan(sweep * t / 4);

        // Openings and connectors past the split move to the new edge.
        foreach (var o in m.Openings.Where(o => o.HostId == start.Id && o.Offset > s).ToList())
        {
            o.HostId = newVertex.Id;
            o.Offset -= s;
        }
        foreach (var c in m.Connectors.Where(c => c.EdgeId == start.Id && c.Offset > s).ToList())
        {
            c.EdgeId = newVertex.Id;
            c.Offset -= s;
        }
        m.Shell.Insert(m.Shell.IndexOf(start) + 1, newVertex);
        Selection = newVertex;
        Complete();
    }

    private void ShellClick(ModuleDefinition m, Vec2 p, int clickCount)
    {
        var closing = _pendingShell.Count >= 3 && (NearScreen(p, _pendingShell[0], HitTolerance * 1.5) || clickCount == 2);
        if (closing)
        {
            CommitShell(m);
            return;
        }
        if (_pendingShell.Count == 0 || Vec2.Distance(_pendingShell[^1], p) > 1e-6) _pendingShell.Add(p);
    }

    private void CommitShell(ModuleDefinition m)
    {
        if (_pendingShell.Count < 3) return;
        m.Shell.Clear();
        foreach (var p in _pendingShell) m.Shell.Add(new ShellVertex { Position = p });
        _pendingShell.Clear();
        Tool = ModuleTool.Select;
        Complete();
    }

    private void WallClick(ModuleDefinition m, Vec2 p)
    {
        if (_wallStart is { } start)
        {
            if (Vec2.Distance(start, p) > 0.05)
            {
                var wall = new InteriorWall { Start = start, End = p };
                m.InteriorWalls.Add(wall);
                Complete();
            }
            _wallStart = p; // Keep chaining from the last point.
        }
        else
        {
            _wallStart = p;
        }
    }

    private void ArcClick(ModuleDefinition m, Vec2 p, Vec2 raw)
    {
        if (_arcChord is { } chord)
        {
            var wall = new InteriorWall { Start = chord.A, End = chord.B, Bulge = Segment.BulgeFromSagitta(chord.A, chord.B, raw) };
            m.InteriorWalls.Add(wall);
            _arcChord = null;
            _wallStart = null;
            Selection = wall;
            Complete();
        }
        else if (_wallStart is { } start)
        {
            if (Vec2.Distance(start, p) > 0.05) _arcChord = (start, p);
        }
        else
        {
            _wallStart = p;
        }
    }

    private void PlaceOpening(ModuleDefinition m, Vec2 w, OpeningKind kind)
    {
        if (NearestHost(m, w) is not { } h) return;
        var o = Opening.Create(kind, h.HostId, h.S);
        o.Width = Math.Min(o.Width, h.Seg.Length);
        o.Offset = Math.Clamp(h.S, o.Width / 2, h.Seg.Length - o.Width / 2);
        m.Openings.Add(o);
        Selection = o;
        Complete();
    }

    private void PlaceConnector(ModuleDefinition m, Vec2 w)
    {
        if (NearestShellEdge(m, w) is not { } h) return;
        var c = new ModuleConnector { EdgeId = h.HostId, Name = $"C{m.Connectors.Count + 1}" };
        c.Width = Math.Min(c.Width, h.Seg.Length);
        c.Offset = Math.Clamp(h.S, c.Width / 2, h.Seg.Length - c.Width / 2);
        m.Connectors.Add(c);

        // A personnel connector needs a way through the wall; add a door unless one is already there.
        var hasOpening = m.Openings.Any(o => o.HostId == h.HostId && Math.Abs(o.Offset - c.Offset) < (o.Width + c.Width) / 2);
        if (!hasOpening)
        {
            var door = Opening.Create(OpeningKind.Door, h.HostId, c.Offset);
            door.Width = Math.Min(door.Width, c.Width);
            m.Openings.Add(door);
        }
        Selection = c;
        Complete();
    }

    private void PlaceEquipment(ModuleDefinition m, string id, Vec2 w)
    {
        var item = new EquipmentPlacement { DefinitionId = id, Position = Snap(w), RotationDegrees = _ghostRotation };
        m.Equipment.Add(item);
        Selection = item;
        Complete();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsPanning || Module is not { } m) return;
        var w = ToWorld(e.GetPosition(this));
        _hover = w;
        _hoverValid = true;

        if (_drag is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            ApplyDrag(m, _drag, w);
            _drag.Moved = true;
        }
        UpdateCursor(m, w);
        InvalidateVisual();
    }

    private void ApplyDrag(ModuleDefinition m, Drag drag, Vec2 w)
    {
        var delta = w - drag.StartWorld;
        switch (drag)
        {
            case VertexDrag d:
                d.Vertex.Position = SnapToFeatures(w, d.Vertex) ?? Snap(d.Original + delta);
                break;
            case EdgeDrag d:
            {
                var moved = Snap(d.OrigA + delta);
                var step = moved - d.OrigA;
                d.A.Position = d.OrigA + step;
                d.B.Position = d.OrigB + step;
                break;
            }
            case WallMoveDrag d:
            {
                var moved = Snap(d.OrigStart + delta);
                var step = moved - d.OrigStart;
                d.Wall.Start = d.OrigStart + step;
                d.Wall.End = d.OrigEnd + step;
                break;
            }
            case WallEndDrag d:
            {
                var p = SnapToFeatures(w, d.Wall) ?? Snap(w);
                if (d.IsStart) d.Wall.Start = p; else d.Wall.End = p;
                break;
            }
            case BulgeDrag d:
            {
                if (d.Vertex is { } v && ModuleGeometry.FindEdge(m, v.Id) is { } edge)
                    v.Bulge = SnapBulge(Segment.BulgeFromSagitta(edge.Segment.Start, edge.Segment.End, w));
                else if (d.Wall is { } wall)
                    wall.Bulge = SnapBulge(Segment.BulgeFromSagitta(wall.Start, wall.End, w));
                break;
            }
            case OpeningDrag d:
            {
                // Slide along the current host, or hop to another wall if the cursor is closer to it.
                if (NearestHost(m, w) is { } h)
                {
                    d.Opening.HostId = h.HostId;
                    d.Opening.Offset = Math.Clamp(SnapLength(h.S), d.Opening.Width / 2, Math.Max(d.Opening.Width / 2, h.Seg.Length - d.Opening.Width / 2));
                }
                break;
            }
            case ConnectorDrag d:
            {
                if (NearestShellEdge(m, w) is { } h)
                {
                    var oldEdge = d.Connector.EdgeId;
                    var oldOffset = d.Connector.Offset;
                    d.Connector.EdgeId = h.HostId;
                    d.Connector.Offset = Math.Clamp(SnapLength(h.S), d.Connector.Width / 2, Math.Max(d.Connector.Width / 2, h.Seg.Length - d.Connector.Width / 2));
                    // Carry the connector's door with it.
                    foreach (var o in m.Openings.Where(o => o.HostId == oldEdge && Math.Abs(o.Offset - oldOffset) < 1e-6 && o.Kind == OpeningKind.Door))
                    {
                        o.HostId = h.HostId;
                        o.Offset = d.Connector.Offset;
                    }
                }
                break;
            }
            case EquipmentDrag d:
                d.Item.Position = Snap(d.Original + delta);
                break;
        }
    }

    private double SnapLength(double s)
    {
        if (!SnapEnabled || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || GridStep <= 0) return s;
        var step = GridStep / 2;
        return Math.Round(s / step) * step;
    }

    /// <summary>Snaps nearly-straight walls flat and nearly-semicircular ones to exactly ±1.</summary>
    private static double SnapBulge(double b)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return b;
        if (Math.Abs(b) < 0.03) return 0;
        if (Math.Abs(Math.Abs(b) - 1) < 0.03) return Math.Sign(b);
        if (Math.Abs(Math.Abs(b) - Math.Tan(Math.PI / 8)) < 0.02) return Math.Sign(b) * Math.Tan(Math.PI / 8);
        return b;
    }

    private void UpdateCursor(ModuleDefinition m, Vec2 w)
    {
        if (_drag is not null) return;
        Cursor = Tool switch
        {
            ModuleTool.Select => HitTest(m, w, Catalog ?? new EquipmentLookup()) is null ? Cursors.Arrow : Cursors.SizeAll,
            ModuleTool.Door or ModuleTool.Window => NearestHost(m, w) is null ? Cursors.No : Cursors.Hand,
            ModuleTool.Connector => NearestShellEdge(m, w) is null ? Cursors.No : Cursors.Hand,
            _ => Cursors.Cross,
        };
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (IsPanning) { base.OnMouseUp(e); return; }
        if (_drag is not null)
        {
            var moved = _drag.Moved;
            _drag = null;
            ReleaseMouseCapture();
            if (moved) Complete();
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
        if (e.Handled || Module is not { } m) return;

        switch (e.Key)
        {
            case Key.Escape:
                if (_pendingShell.Count > 0 || _wallStart is not null || _arcChord is not null) CancelPending();
                else if (Tool != ModuleTool.Select) Tool = ModuleTool.Select;
                else Selection = null;
                e.Handled = true;
                break;
            case Key.Enter when Tool == ModuleTool.Shell:
                CommitShell(m);
                e.Handled = true;
                break;
            case Key.Back when Tool == ModuleTool.Shell && _pendingShell.Count > 0:
                _pendingShell.RemoveAt(_pendingShell.Count - 1);
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
            case Key.Left or Key.Right or Key.Up or Key.Down:
                Nudge(e.Key);
                e.Handled = true;
                break;
        }
    }

    public void RotateSelection(int direction, double degrees = 90)
    {
        if (Tool == ModuleTool.Equipment)
        {
            _ghostRotation = Core.Geometry.Pose2.NormalizeDegrees(_ghostRotation + direction * degrees);
            InvalidateVisual();
            return;
        }
        if (Selection is EquipmentPlacement item)
        {
            item.RotationDegrees = Core.Geometry.Pose2.NormalizeDegrees(item.RotationDegrees + direction * degrees);
            Complete();
        }
    }

    private void Nudge(Key key)
    {
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? GridStep * 4 : GridStep;
        var d = key switch
        {
            Key.Left => new Vec2(-step, 0),
            Key.Right => new Vec2(step, 0),
            Key.Up => new Vec2(0, step),
            _ => new Vec2(0, -step),
        };
        switch (Selection)
        {
            case EquipmentPlacement item: item.Position += d; break;
            case ShellVertex v: v.Position += d; break;
            case InteriorWall w: w.Start += d; w.End += d; break;
            default: return;
        }
        Complete();
    }

    public void DeleteSelection()
    {
        if (Module is not { } m) return;
        switch (Selection)
        {
            case ShellVertex v when m.Shell.Count > 3:
            {
                // Openings/connectors on the removed vertex's edge go with it.
                m.Shell.Remove(v);
                break;
            }
            case InteriorWall w:
                m.InteriorWalls.Remove(w);
                foreach (var o in m.Openings.Where(o => o.HostId == w.Id).ToList()) m.Openings.Remove(o);
                break;
            case Opening o:
                m.Openings.Remove(o);
                break;
            case ModuleConnector c:
                m.Connectors.Remove(c);
                break;
            case EquipmentPlacement item:
                m.Equipment.Remove(item);
                break;
            case EdgeSelection es:
                es.Start.Bulge = 0; // "Deleting" an edge's curve straightens it.
                break;
            default:
                return;
        }
        Selection = null;
        Complete();
    }

    #endregion

    #region Drag and drop from the equipment palette

    protected override void OnDragEnter(DragEventArgs e) => OnDragOver(e);

    protected override void OnDragOver(DragEventArgs e)
    {
        if (e.Data.GetData(EquipmentDragFormat) is string id && Module is not null)
        {
            e.Effects = DragDropEffects.Copy;
            _dragOverEquipment = id;
            _hover = ToWorld(e.GetPosition(this));
            _hoverValid = true;
            var previousTool = Tool;
            if (previousTool != ModuleTool.Equipment) Tool = ModuleTool.Equipment;
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
        _dragOverEquipment = null;
        InvalidateVisual();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetData(EquipmentDragFormat) is string id && Module is { } m)
        {
            PlaceEquipment(m, id, ToWorld(e.GetPosition(this)));
            Tool = ModuleTool.Select;
            Focus();
        }
        _dragOverEquipment = null;
        e.Handled = true;
    }

    #endregion
}

internal static class PointExtensions
{
    public static Vec2 ToVec(this Point p) => new(p.X, p.Y);
}
