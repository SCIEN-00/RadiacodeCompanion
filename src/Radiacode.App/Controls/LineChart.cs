using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Radiacode.App.Controls;

/// <summary>Vertical marker on a <see cref="LineChart"/> (in X-axis units, e.g. keV).</summary>
public sealed record ChartMarker(double X, string Label, bool Emphasized = false);

/// <summary>
/// Minimal dependency-free line/area chart with a labelled Y axis, an optional X axis, vertical markers,
/// an optional overlay series, a hover cursor with read-out, and (optionally) wheel zoom / drag pan.
/// Works identically on WinUI (Windows) and Uno Skia (Linux).
/// </summary>
public sealed partial class LineChart : Grid
{
    private const double AxisWidth = 50;
    private const double TopPad = 18;
    private readonly Canvas _canvas = new();
    private readonly Canvas _cursor = new() { IsHitTestVisible = false };

    // Visible window in (fractional) point indices; NaN = whole series.
    private double _v0 = double.NaN, _v1 = double.NaN;
    private int _lastCount;

    // Geometry of the last draw, used by the cursor.
    private double _w, _h, _min, _max;
    private Func<double, double> _map = v => v;

    private Point? _dragFrom;
    private double _dragV0, _dragV1;

    public LineChart()
    {
        Background = new SolidColorBrush(Colors.Transparent); // receive pointer input everywhere
        Children.Add(_canvas);
        Children.Add(_cursor);
        SizeChanged += (_, e) =>
        {
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            Redraw();
        };
        ActualThemeChanged += (_, _) => Redraw();
        PointerMoved += OnPointerMoved;
        PointerExited += (_, _) => { _cursor.Children.Clear(); _dragFrom = null; };
        PointerPressed += OnPointerPressed;
        PointerReleased += (_, e) => { _dragFrom = null; ReleasePointerCapture(e.Pointer); };
        PointerWheelChanged += OnPointerWheelChanged;
        DoubleTapped += (_, _) => ResetZoom();
    }

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(LineChart), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty OverlayProperty = DependencyProperty.Register(
        nameof(Overlay), typeof(IReadOnlyList<double>), typeof(LineChart), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(LineChart), new PropertyMetadata(new SolidColorBrush(Colors.Orange), OnChanged));

    public static readonly DependencyProperty LogScaleProperty = DependencyProperty.Register(
        nameof(LogScale), typeof(bool), typeof(LineChart), new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(LineChart), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty ReferenceValueProperty = DependencyProperty.Register(
        nameof(ReferenceValue), typeof(double), typeof(LineChart), new PropertyMetadata(double.NaN, OnChanged));

    public static readonly DependencyProperty XValueProperty = DependencyProperty.Register(
        nameof(XValue), typeof(Func<int, double>), typeof(LineChart), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty XFormatProperty = DependencyProperty.Register(
        nameof(XFormat), typeof(Func<double, string>), typeof(LineChart), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register(
        nameof(Markers), typeof(IReadOnlyList<ChartMarker>), typeof(LineChart), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty ZoomableProperty = DependencyProperty.Register(
        nameof(Zoomable), typeof(bool), typeof(LineChart), new PropertyMetadata(false));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    /// <summary>Optional second series drawn as a thin grey line (e.g. a scaled background spectrum).</summary>
    public IReadOnlyList<double>? Overlay { get => (IReadOnlyList<double>?)GetValue(OverlayProperty); set => SetValue(OverlayProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public bool LogScale { get => (bool)GetValue(LogScaleProperty); set => SetValue(LogScaleProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    /// <summary>Optional dashed horizontal reference line (e.g. Cs-137 hardness 1.45).</summary>
    public double ReferenceValue { get => (double)GetValue(ReferenceValueProperty); set => SetValue(ReferenceValueProperty, value); }
    /// <summary>Maps a point index to its X value (e.g. channel → keV, sample → minutes ago). Enables the X axis.</summary>
    public Func<int, double>? XValue { get => (Func<int, double>?)GetValue(XValueProperty); set => SetValue(XValueProperty, value); }
    /// <summary>Formats X tick labels; default "0.##".</summary>
    public Func<double, string>? XFormat { get => (Func<double, string>?)GetValue(XFormatProperty); set => SetValue(XFormatProperty, value); }
    /// <summary>Vertical markers, positioned via <see cref="XValue"/>.</summary>
    public IReadOnlyList<ChartMarker>? Markers { get => (IReadOnlyList<ChartMarker>?)GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
    /// <summary>Mouse wheel zooms around the cursor, drag pans, double-click resets.</summary>
    public bool Zoomable { get => (bool)GetValue(ZoomableProperty); set => SetValue(ZoomableProperty, value); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LineChart)d).Redraw();

    private static FontFamily CardFont => Application.Current.Resources.TryGetValue("CardFont", out var f) && f is FontFamily ff ? ff : new FontFamily("Calibri");
    private bool IsDark => ActualTheme == ElementTheme.Dark;
    private SolidColorBrush GridBrush => new(IsDark ? Windows.UI.Color.FromArgb(255, 0x23, 0x2A, 0x33) : Windows.UI.Color.FromArgb(255, 0xDD, 0xE1, 0xE6));
    private SolidColorBrush LabelBrush => new(IsDark ? Windows.UI.Color.FromArgb(255, 0xA3, 0xAC, 0xB8) : Windows.UI.Color.FromArgb(255, 0x5B, 0x65, 0x73));
    private SolidColorBrush OverlayBrush => new(IsDark ? Windows.UI.Color.FromArgb(200, 0xC8, 0xCE, 0xD6) : Windows.UI.Color.FromArgb(200, 0x6B, 0x74, 0x80));
    private SolidColorBrush MarkerBrush(bool strong) => new(strong
        ? (IsDark ? Windows.UI.Color.FromArgb(255, 0x9B, 0xF0, 0xFF) : Windows.UI.Color.FromArgb(255, 0x1F, 0x5F, 0xD6))
        : (IsDark ? Windows.UI.Color.FromArgb(110, 0xA3, 0xAC, 0xB8) : Windows.UI.Color.FromArgb(110, 0x5B, 0x65, 0x73)));
    private static readonly SolidColorBrush RefBrush = new(Windows.UI.Color.FromArgb(200, 0xF4, 0x72, 0xB6));

    public void ResetZoom()
    {
        _v0 = _v1 = double.NaN;
        Redraw();
    }

    private (double V0, double V1) View(int n)
    {
        if (double.IsNaN(_v0) || n != _lastCount) return (0, n - 1);
        return (_v0, _v1);
    }

    private void Redraw()
    {
        // A chart must never take the app down (e.g. values and X mapping briefly out of step).
        try { RedrawCore(); }
        catch (Exception ex) { _canvas.Children.Clear(); System.Diagnostics.Debug.WriteLine($"LineChart: {ex.Message}"); }
    }

    private static double At(IReadOnlyList<double> values, double i)
    {
        int a = Math.Clamp((int)Math.Floor(i), 0, values.Count - 1), b = Math.Min(a + 1, values.Count - 1);
        return values[a] + (values[b] - values[a]) * (i - a);
    }

    private static double XAt(Func<int, double> f, int n, double i)
    {
        int a = Math.Clamp((int)Math.Floor(i), 0, n - 1), b = Math.Min(a + 1, n - 1);
        return f(a) + (f(b) - f(a)) * (i - a);
    }

    private void RedrawCore()
    {
        _canvas.Children.Clear();
        _cursor.Children.Clear();
        var xv = XValue;
        double bottomPad = xv is null ? 4 : 26;
        double w = ActualWidth - AxisWidth, h = ActualHeight - bottomPad;
        var values = Values;
        if (w <= 10 || h <= 10 || values is null || values.Count < 2) return;
        int n = values.Count;
        if (n != _lastCount) { _v0 = _v1 = double.NaN; _lastCount = n; }
        var (v0, v1) = View(n);
        var overlay = Overlay is { } ov && ov.Count == n ? ov : null;

        int i0 = Math.Max(0, (int)Math.Floor(v0)), i1 = Math.Min(n - 1, (int)Math.Ceiling(v1));
        bool log = LogScale;
        Func<double, double> map = log ? v => Math.Log10(Math.Max(v, 0) + 1) : v => v;
        double max = 0, lowest = 0;
        for (int i = i0; i <= i1; i++)
        {
            max = Math.Max(max, map(values[i]));
            lowest = Math.Min(lowest, values[i]);
            if (overlay is not null) max = Math.Max(max, map(overlay[i]));
        }
        if (!double.IsNaN(ReferenceValue)) max = Math.Max(max, map(ReferenceValue));
        // Linear charts may go below zero (e.g. spectrum minus background); log charts start at 0.
        double min = log ? 0 : lowest * 1.08;
        if (max <= min) max = min + 1;
        max = max > 0 ? max * 1.08 : max + (max - min) * 0.08;
        (_w, _h, _min, _max, _map) = (w, h, min, max, map);

        double Y(double v) => TopPad + (h - TopPad) - (map(v) - min) / (max - min) * (h - TopPad);
        double X(double i) => AxisWidth + (i - v0) / (v1 - v0) * w;

        foreach (var (label, value) in YTicks(min, max, log, map))
        {
            double y = TopPad + (h - TopPad) - (value - min) / (max - min) * (h - TopPad);
            _canvas.Children.Add(new Line { X1 = AxisWidth, X2 = AxisWidth + w, Y1 = y, Y2 = y, Stroke = GridBrush, StrokeThickness = 1 });
            var tb = new TextBlock { FontFamily = CardFont, Text = label, FontSize = 14, Foreground = LabelBrush, Width = AxisWidth - 6, TextAlignment = TextAlignment.Right };
            Canvas.SetLeft(tb, 0);
            Canvas.SetTop(tb, y - 9);
            _canvas.Children.Add(tb);
        }

        if (xv is not null)
        {
            double a = XAt(xv, n, v0), b = XAt(xv, n, v1);
            double lo = Math.Min(a, b), hi = Math.Max(a, b);
            double step = NiceStep((hi - lo) / Math.Max(2, w / 110));
            var fmt = XFormat ?? (v => v.ToString("0.##"));
            _canvas.Children.Add(new Line { X1 = AxisWidth, X2 = AxisWidth + w, Y1 = h, Y2 = h, Stroke = GridBrush, StrokeThickness = 1 });
            for (double t = Math.Ceiling(lo / step) * step; t <= hi + step * 1e-6; t += step)
            {
                double x = X(IndexOf(xv, n, t));
                if (x < AxisWidth - 1 || x > AxisWidth + w + 1) continue;
                _canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = h, Y2 = h + 4, Stroke = LabelBrush, StrokeThickness = 1 });
                var tb = new TextBlock { FontFamily = CardFont, Text = fmt(Math.Abs(t) < step * 1e-6 ? 0 : t), FontSize = 14, Foreground = LabelBrush, Width = 90, TextAlignment = TextAlignment.Center };
                // Keep edge labels (e.g. "now" at the right end) inside the chart instead of clipping them.
                if (x + 45 > AxisWidth + w) { tb.TextAlignment = TextAlignment.Right; Canvas.SetLeft(tb, AxisWidth + w - 90); }
                else Canvas.SetLeft(tb, Math.Max(AxisWidth - 6, x - 45));
                Canvas.SetTop(tb, h + 5);
                _canvas.Children.Add(tb);
            }
        }

        if (!string.IsNullOrEmpty(Unit))
        {
            var unit = new TextBlock { FontFamily = CardFont, Text = Unit, FontSize = 14, Foreground = LabelBrush };
            Canvas.SetLeft(unit, AxisWidth + 6);
            Canvas.SetTop(unit, -2);
            _canvas.Children.Add(unit);
        }

        if (xv is not null && Markers is { Count: > 0 } markers)
        {
            double first = XAt(xv, n, v0), last = XAt(xv, n, v1);
            int k = 0;
            foreach (var m in markers.OrderBy(m => m.X))
            {
                if (m.X < Math.Min(first, last) || m.X > Math.Max(first, last)) continue;
                double x = X(IndexOf(xv, n, m.X));
                var brush = MarkerBrush(m.Emphasized);
                _canvas.Children.Add(new Line
                {
                    X1 = x, X2 = x, Y1 = 18, Y2 = h, Stroke = brush, StrokeThickness = m.Emphasized ? 1.5 : 1,
                    StrokeDashArray = new DoubleCollection { 2, 3 },
                });
                var tb = new TextBlock { FontFamily = CardFont, Text = m.Label, FontSize = 14, LineHeight = 16, Foreground = brush, FontWeight = m.Emphasized ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal };
                Canvas.SetLeft(tb, x + 3);
                Canvas.SetTop(tb, 20 + (k++ % 3) * 36);
                _canvas.Children.Add(tb);
            }
        }

        // Series, clipped exactly at the view edges.
        PointCollection Series(IReadOnlyList<double> s)
        {
            var pts = new PointCollection { new Point(X(v0), Y(At(s, v0))) };
            for (int i = (int)Math.Ceiling(v0); i <= (int)Math.Floor(v1); i++) pts.Add(new Point(X(i), Y(s[i])));
            pts.Add(new Point(X(v1), Y(At(s, v1))));
            return pts;
        }

        var line = Series(values);
        double baseY = Math.Clamp(Y(0), TopPad, h);
        var area = new PointCollection { new Point(X(v0), baseY) };
        foreach (var p in line) area.Add(p);
        area.Add(new Point(X(v1), baseY));

        var fill = Stroke is SolidColorBrush sb
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(48, sb.Color.R, sb.Color.G, sb.Color.B))
            : null;
        _canvas.Children.Add(new Polygon { Points = area, Fill = fill });
        if (overlay is not null)
            _canvas.Children.Add(new Polyline { Points = Series(overlay), Stroke = OverlayBrush, StrokeThickness = 1.2 });
        _canvas.Children.Add(new Polyline { Points = line, Stroke = Stroke, StrokeThickness = 1.5 });

        if (!double.IsNaN(ReferenceValue))
        {
            double y = Y(ReferenceValue);
            _canvas.Children.Add(new Line
            {
                X1 = AxisWidth, X2 = AxisWidth + w, Y1 = y, Y2 = y, Stroke = RefBrush, StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 3, 4 },
            });
        }
    }

    // ---- interaction ----------------------------------------------------------------------------

    private double IndexAtPointer(double px, int n)
    {
        var (v0, v1) = View(n);
        return v0 + (px - AxisWidth) / _w * (v1 - v0);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var values = Values;
        if (values is null || values.Count < 2 || _w <= 0) return;
        int n = values.Count;
        var pos = e.GetCurrentPoint(this).Position;

        if (_dragFrom is { } from && Zoomable)
        {
            double span = _dragV1 - _dragV0;
            double shift = -(pos.X - from.X) / _w * span;
            shift = Math.Clamp(shift, -_dragV0, n - 1 - _dragV1);
            _v0 = _dragV0 + shift;
            _v1 = _dragV1 + shift;
            Redraw();
        }
        DrawCursor(pos, values);
    }

    private void DrawCursor(Point pos, IReadOnlyList<double> values)
    {
        _cursor.Children.Clear();
        int n = values.Count;
        if (pos.X < AxisWidth || pos.X > AxisWidth + _w || pos.Y > _h) return;
        int i = (int)Math.Round(Math.Clamp(IndexAtPointer(pos.X, n), 0, n - 1));
        var (v0, v1) = View(n);
        double x = AxisWidth + (i - v0) / (v1 - v0) * _w;
        double y = TopPad + (_h - TopPad) - (_map(values[i]) - _min) / (_max - _min) * (_h - TopPad);

        var accent = MarkerBrush(true);
        _cursor.Children.Add(new Line { X1 = x, X2 = x, Y1 = TopPad, Y2 = _h, Stroke = accent, StrokeThickness = 1 });
        var dot = new Ellipse { Width = 8, Height = 8, Fill = Stroke };
        Canvas.SetLeft(dot, x - 4);
        Canvas.SetTop(dot, y - 4);
        _cursor.Children.Add(dot);

        var lines = new List<string>();
        if (XValue is { } xv)
        {
            double xValue = xv(i);
            lines.Add((XFormat ?? (v => v.ToString("0.##")))(xValue));
            if (Markers is { Count: > 0 } markers)
            {
                double tolerance = Math.Abs(XAt(xv, n, v1) - XAt(xv, n, v0)) * 0.015;
                var near = markers.Where(m => Math.Abs(m.X - xValue) <= tolerance).OrderBy(m => Math.Abs(m.X - xValue)).FirstOrDefault();
                if (near is not null) lines.Add(near.Label.Replace('\n', ' '));
            }
        }
        lines.Add($"{FormatValue(values[i])} {Unit}".Trim());
        if (Overlay is { } ov && ov.Count == n) lines.Add($"background {FormatValue(ov[i])}");

        var text = new TextBlock { FontFamily = CardFont, FontSize = 14, Text = string.Join("\n", lines) };
        var box = new Border
        {
            Child = text,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["AcrylicInAppFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["SurfaceStrokeColorFlyoutBrush"],
        };
        box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double left = x + 12;
        if (left + box.DesiredSize.Width > AxisWidth + _w) left = x - 12 - box.DesiredSize.Width;
        Canvas.SetLeft(box, left);
        Canvas.SetTop(box, Math.Clamp(pos.Y - box.DesiredSize.Height - 8, 0, Math.Max(0, _h - box.DesiredSize.Height)));
        _cursor.Children.Add(box);
    }

    private static string FormatValue(double v) => Math.Abs(v) >= 100 ? v.ToString("0") : v.ToString("0.##");

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!Zoomable || Values is not { Count: > 1 } values) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        (_dragV0, _dragV1) = View(values.Count);
        _dragFrom = point.Position;
        CapturePointer(e.Pointer);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!Zoomable || Values is not { Count: > 1 } values || _w <= 0) return;
        int n = values.Count;
        var point = e.GetCurrentPoint(this);
        int delta = point.Properties.MouseWheelDelta;
        if (delta == 0) return;
        var (v0, v1) = View(n);
        double anchor = Math.Clamp(IndexAtPointer(point.Position.X, n), v0, v1);
        double factor = delta > 0 ? 0.8 : 1.25;
        double span = Math.Clamp((v1 - v0) * factor, 16, n - 1);
        double nv0 = anchor - (anchor - v0) / (v1 - v0) * span;
        nv0 = Math.Clamp(nv0, 0, n - 1 - span);
        if (span >= n - 1) ResetZoom();
        else
        {
            _v0 = nv0;
            _v1 = nv0 + span;
            Redraw();
        }
        DrawCursor(point.Position, values);
        e.Handled = true;
    }

    /// <summary>Fractional index where a monotonic X mapping reaches <paramref name="target"/>.</summary>
    private static double IndexOf(Func<int, double> f, int n, double target)
    {
        bool increasing = f(n - 1) >= f(0);
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if ((f(mid) < target) == increasing) lo = mid; else hi = mid;
        }
        double vl = f(lo), vh = f(hi);
        return vh == vl ? lo : Math.Clamp(lo + (target - vl) / (vh - vl), 0, n - 1);
    }

    private static double NiceStep(double raw)
    {
        if (raw <= 0 || !double.IsFinite(raw)) return 1;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        return new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * mag).First(s => s >= raw);
    }

    private static IEnumerable<(string Label, double Mapped)> YTicks(double mappedMin, double mappedMax, bool log, Func<double, double> map)
    {
        if (log)
        {
            for (double v = 1; map(v) < mappedMax; v *= 10)
                yield return (v >= 1000 ? $"{v / 1000:0}k" : $"{v:0}", map(v));
            yield break;
        }
        double step = NiceStep((mappedMax - mappedMin) / 4);
        for (double v = Math.Ceiling(mappedMin / step) * step; v < mappedMax; v += step)
            yield return (step < 1 ? v.ToString("0.0#") : v.ToString("0"), v);
    }
}
