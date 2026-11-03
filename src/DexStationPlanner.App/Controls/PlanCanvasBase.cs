using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DexStationPlanner.App.Services;
using DexStationPlanner.Core.Geometry;

namespace DexStationPlanner.App.Controls;

/// <summary>
/// A pannable, zoomable 2D plan view in meters (X right, Y up) drawn with a <see cref="DrawingContext"/>.
/// Middle-drag or Space+drag pans, the wheel zooms about the cursor.
/// </summary>
public abstract class PlanCanvasBase : FrameworkElement
{
    private bool _panning;
    private Point _panStart;
    private Point _panOrigin;
    private bool _spaceDown;
    private bool _initialFitDone;

    protected PlanCanvasBase()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        Loaded += (_, _) => ThemeService.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeService.ThemeChanged -= OnThemeChanged;
        SizeChanged += (_, e) =>
        {
            if (!_initialFitDone && e.NewSize.Width > 0) { _initialFitDone = true; ZoomToFit(); }
            InvalidateVisual();
        };
    }

    /// <summary>Pixels per meter.</summary>
    public double Scale { get; private set; } = 40;

    /// <summary>Screen position of the world origin.</summary>
    public Point Origin { get; private set; } = new(200, 200);

    protected double MinScale { get; init; } = 2;
    protected double MaxScale { get; init; } = 400;

    public static readonly DependencyProperty GridStepProperty = DependencyProperty.Register(
        nameof(GridStep), typeof(double), typeof(PlanCanvasBase),
        new FrameworkPropertyMetadata(0.25, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Snap and minor grid spacing in meters.</summary>
    public double GridStep
    {
        get => (double)GetValue(GridStepProperty);
        set => SetValue(GridStepProperty, value);
    }

    public static readonly DependencyProperty SnapEnabledProperty = DependencyProperty.Register(
        nameof(SnapEnabled), typeof(bool), typeof(PlanCanvasBase), new PropertyMetadata(true));

    public bool SnapEnabled
    {
        get => (bool)GetValue(SnapEnabledProperty);
        set => SetValue(SnapEnabledProperty, value);
    }

    /// <summary>Major grid line spacing in meters (before zoom adaptation).</summary>
    protected double MajorStep { get; init; } = 1;

    /// <summary>Raised with the world position under the cursor.</summary>
    public event EventHandler<Vec2>? PointerWorldMoved;

    public Point ToScreen(Vec2 w) => new(Origin.X + w.X * Scale, Origin.Y - w.Y * Scale);
    public Vec2 ToWorld(Point p) => new((p.X - Origin.X) / Scale, (Origin.Y - p.Y) / Scale);

    /// <summary>Rounds to the grid unless snapping is off or Alt is held.</summary>
    public Vec2 Snap(Vec2 w)
    {
        if (!SnapEnabled || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || GridStep <= 0) return w;
        return new Vec2(Math.Round(w.X / GridStep) * GridStep, Math.Round(w.Y / GridStep) * GridStep);
    }

    /// <summary>Content bounds in world meters, used by <see cref="ZoomToFit"/>.</summary>
    protected abstract (Vec2 Min, Vec2 Max)? ContentBounds();

    public void ZoomToFit()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var b = ContentBounds() ?? (new Vec2(-5, -5), new Vec2(5, 5));
        var w = Math.Max(1, b.Max.X - b.Min.X);
        var h = Math.Max(1, b.Max.Y - b.Min.Y);
        const double margin = 60;
        Scale = Math.Clamp(Math.Min((ActualWidth - 2 * margin) / w, (ActualHeight - 2 * margin) / h), MinScale, MaxScale);
        var center = Vec2.Lerp(b.Min, b.Max, 0.5);
        Origin = new Point(ActualWidth / 2 - center.X * Scale, ActualHeight / 2 + center.Y * Scale);
        InvalidateVisual();
    }

    public void ZoomBy(double factor, Point about)
    {
        var world = ToWorld(about);
        Scale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        Origin = new Point(about.X - world.X * Scale, about.Y + world.Y * Scale);
        InvalidateVisual();
    }

    protected void PanBy(Vector delta)
    {
        Origin += delta;
        InvalidateVisual();
    }

    protected Brush ThemeBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Magenta;

    protected Pen ThemePen(string key, double thickness, DashStyle? dash = null)
    {
        var pen = new Pen(ThemeBrush(key), thickness) { DashStyle = dash ?? DashStyles.Solid };
        pen.Freeze();
        return pen;
    }

    protected static Brush BrushFromHex(string hex, byte alpha = 255)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
        catch (FormatException)
        {
            return Brushes.Gray;
        }
    }

    protected FormattedText MakeText(string text, Brush brush, double size = 11, bool bold = false) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Draws centered text only if it fits within <paramref name="maxWidth"/> pixels.</summary>
    protected bool DrawTextIfFits(DrawingContext dc, string text, Point center, Brush brush, double size, double maxWidth)
    {
        var ft = MakeText(text, brush, size);
        if (ft.Width > maxWidth) return false;
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
        return true;
    }

    protected void DrawText(DrawingContext dc, string text, Point at, Brush brush, double size = 11,
        HorizontalAlignment align = HorizontalAlignment.Center, bool bold = false, Brush? halo = null)
    {
        var ft = MakeText(text, brush, size, bold);
        var x = align switch
        {
            HorizontalAlignment.Center => at.X - ft.Width / 2,
            HorizontalAlignment.Right => at.X - ft.Width,
            _ => at.X,
        };
        var origin = new Point(x, at.Y - ft.Height / 2);
        if (halo is not null)
            dc.DrawRoundedRectangle(halo, null, new Rect(origin.X - 3, origin.Y - 1, ft.Width + 6, ft.Height + 2), 3, 3);
        dc.DrawText(ft, origin);
    }

    protected StreamGeometry PolylineGeometry(IReadOnlyList<Vec2> pts, bool closed, bool filled = false)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            if (pts.Count > 0)
            {
                ctx.BeginFigure(ToScreen(pts[0]), filled, closed);
                for (var i = 1; i < pts.Count; i++) ctx.LineTo(ToScreen(pts[i]), true, true);
            }
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(ThemeBrush("Dex.Canvas.Background"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        DrawGrid(dc);
        RenderContent(dc);
        DrawScaleBar(dc);
    }

    protected abstract void RenderContent(DrawingContext dc);

    private void DrawGrid(DrawingContext dc)
    {
        var minor = GridStep > 1e-6 ? GridStep : 1;
        while (minor * Scale < 8) minor *= minor * Scale < 2 ? 10 : 2;
        var major = Math.Max(MajorStep, minor);
        while (major * Scale < 40) major *= major * Scale < 10 ? 10 : 2;

        var tl = ToWorld(new Point(0, 0));
        var br = ToWorld(new Point(ActualWidth, ActualHeight));
        var minorPen = ThemePen("Dex.Canvas.GridMinor", 1);
        var majorPen = ThemePen("Dex.Canvas.GridMajor", 1);
        var axisPen = ThemePen("Dex.Canvas.Axis", 1);

        void Lines(double step, Pen pen)
        {
            for (var x = Math.Floor(tl.X / step) * step; x <= br.X; x += step)
            {
                var sx = Math.Round(ToScreen(new Vec2(x, 0)).X) + 0.5;
                dc.DrawLine(pen, new Point(sx, 0), new Point(sx, ActualHeight));
            }
            for (var y = Math.Floor(br.Y / step) * step; y <= tl.Y; y += step)
            {
                var sy = Math.Round(ToScreen(new Vec2(0, y)).Y) + 0.5;
                dc.DrawLine(pen, new Point(0, sy), new Point(ActualWidth, sy));
            }
        }

        if (minor < major) Lines(minor, minorPen);
        Lines(major, majorPen);
        var o = ToScreen(Vec2.Zero);
        dc.DrawLine(axisPen, new Point(Math.Round(o.X) + 0.5, 0), new Point(Math.Round(o.X) + 0.5, ActualHeight));
        dc.DrawLine(axisPen, new Point(0, Math.Round(o.Y) + 0.5), new Point(ActualWidth, Math.Round(o.Y) + 0.5));
    }

    /// <summary>Formats a scale-bar length; overridden for imperial display.</summary>
    protected virtual (double Meters, string Label) ScaleBarLength(double targetMeters)
    {
        var steps = new[] { 0.1, 0.25, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 };
        var m = steps.FirstOrDefault(s => s >= targetMeters, 1000);
        return (m, m < 1 ? $"{m * 100:0} cm" : $"{m:0} m");
    }

    private void DrawScaleBar(DrawingContext dc)
    {
        var (meters, label) = ScaleBarLength(100 / Scale);
        var px = meters * Scale;
        var y = ActualHeight - 18;
        var x = 16.0;
        var pen = ThemePen("Dex.Canvas.Text", 2);
        dc.DrawLine(pen, new Point(x, y), new Point(x + px, y));
        dc.DrawLine(pen, new Point(x, y - 5), new Point(x, y + 1));
        dc.DrawLine(pen, new Point(x + px, y - 5), new Point(x + px, y + 1));
        DrawText(dc, label, new Point(x + px + 8, y - 2), ThemeBrush("Dex.Canvas.Text"), 11, HorizontalAlignment.Left);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15, e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>True when the press should pan instead of edit (middle button, or Space held).</summary>
    protected bool TryBeginPan(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && _spaceDown))
        {
            BeginPan(e.GetPosition(this));
            return true;
        }
        return false;
    }

    protected void BeginPan(Point at)
    {
        _panning = true;
        _panStart = at;
        _panOrigin = Origin;
        Cursor = Cursors.ScrollAll;
        CaptureMouse();
    }

    protected bool IsPanning => _panning;

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        PointerWorldMoved?.Invoke(this, ToWorld(p));
        if (_panning)
        {
            Origin = _panOrigin + (p - _panStart);
            InvalidateVisual();
            return;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_panning)
        {
            _panning = false;
            Cursor = null;
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space) { _spaceDown = true; e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space) { _spaceDown = false; e.Handled = true; return; }
        base.OnKeyUp(e);
    }
}
