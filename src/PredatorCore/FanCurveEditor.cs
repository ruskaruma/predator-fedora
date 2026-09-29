using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace PredatorCore;

/// <summary>
///     Temperature → fan-speed curve editor. Drag points to edit, double-click to add a point,
///     right-click a point to remove it. Points stay sorted, temperatures distinct and speeds
///     non-decreasing, matching what the daemon accepts.
/// </summary>
public class FanCurveEditor : Control
{
    public static readonly StyledProperty<Color> AccentProperty =
        AvaloniaProperty.Register<FanCurveEditor, Color>(nameof(Accent), Color.Parse("#00E0FF"));

    public static readonly StyledProperty<double> CurrentTempProperty =
        AvaloniaProperty.Register<FanCurveEditor, double>(nameof(CurrentTemp), double.NaN);

    private const double MinTemp = 30, MaxTemp = 100;
    private const double PadLeft = 44, PadRight = 16, PadTop = 14, PadBottom = 30;
    private const double HitRadius = 12;

    private static readonly Typeface LabelFace =
        new(new FontFamily("avares://PredatorCore/Assets/Fonts/Oxanium.ttf#Oxanium"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#7A8694"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#1C2229")), 1);
    private static readonly IPen DangerPen =
        new Pen(new SolidColorBrush(Color.Parse("#FF3B4E"), 0.6), 1, new DashStyle(new double[] { 4, 4 }, 0));

    private List<int[]> _points = new() { new[] { 50, 0 }, new[] { 90, 100 } };
    private int _dragIndex = -1;

    static FanCurveEditor()
    {
        AffectsRender<FanCurveEditor>(AccentProperty, CurrentTempProperty);
        FocusableProperty.OverrideDefaultValue<FanCurveEditor>(true);
    }

    public Color Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Live temperature marker; NaN hides it.</summary>
    public double CurrentTemp
    {
        get => GetValue(CurrentTempProperty);
        set => SetValue(CurrentTempProperty, value);
    }

    public int SafetyTemp { get; set; } = 95;

    public List<int[]> Points
    {
        get => _points.Select(p => new[] { p[0], p[1] }).ToList();
        set
        {
            _points = (value ?? new List<int[]>()).Select(p => new[] { p[0], p[1] }).OrderBy(p => p[0]).ToList();
            if (_points.Count < 2) _points = new List<int[]> { new[] { 50, 0 }, new[] { 90, 100 } };
            InvalidateVisual();
        }
    }

    public event EventHandler? CurveChanged;

    private Rect Plot => new(PadLeft, PadTop, Math.Max(1, Bounds.Width - PadLeft - PadRight),
        Math.Max(1, Bounds.Height - PadTop - PadBottom));

    private Point ToScreen(double temp, double pct)
    {
        var r = Plot;
        return new Point(r.X + (temp - MinTemp) / (MaxTemp - MinTemp) * r.Width, r.Bottom - pct / 100 * r.Height);
    }

    private (int temp, int pct) FromScreen(Point p)
    {
        var r = Plot;
        var temp = MinTemp + (p.X - r.X) / r.Width * (MaxTemp - MinTemp);
        var pct = (r.Bottom - p.Y) / r.Height * 100;
        return ((int)Math.Round(Math.Clamp(temp, MinTemp, MaxTemp)), (int)Math.Round(Math.Clamp(pct, 0, 100)));
    }

    public override void Render(DrawingContext ctx)
    {
        var r = Plot;
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#0A0D11"), 0.55), new Rect(Bounds.Size), 8);

        for (var pct = 0; pct <= 100; pct += 25)
        {
            var y = ToScreen(MinTemp, pct).Y;
            ctx.DrawLine(GridPen, new Point(r.X, y), new Point(r.Right, y));
            DrawLabel(ctx, $"{pct}%", new Point(6, y - 7));
        }

        for (var t = (int)MinTemp; t <= MaxTemp; t += 10)
        {
            var x = ToScreen(t, 0).X;
            ctx.DrawLine(GridPen, new Point(x, r.Y), new Point(x, r.Bottom));
            DrawLabel(ctx, $"{t}°", new Point(x - 9, r.Bottom + 8));
        }

        // Safety zone: everything past the safety temperature runs at 100%.
        var sx = ToScreen(SafetyTemp, 0).X;
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#FF3B4E"), 0.07), new Rect(sx, r.Y, r.Right - sx, r.Height));
        ctx.DrawLine(DangerPen, new Point(sx, r.Y), new Point(sx, r.Bottom));

        var accent = Accent;
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        var first = ToScreen(MinTemp, _points[0][1]);
        var last = ToScreen(MaxTemp, _points[^1][1]);
        using (var g = line.Open())
        {
            g.BeginFigure(first, false);
            foreach (var p in _points) g.LineTo(ToScreen(p[0], p[1]));
            g.LineTo(last);
            g.EndFigure(false);
        }

        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(first.X, r.Bottom), true);
            g.LineTo(first);
            foreach (var p in _points) g.LineTo(ToScreen(p[0], p[1]));
            g.LineTo(last);
            g.LineTo(new Point(last.X, r.Bottom));
            g.EndFigure(true);
        }

        ctx.DrawGeometry(new SolidColorBrush(accent, 0.10), null, fill);
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 2.5), line);

        for (var i = 0; i < _points.Count; i++)
        {
            var c = ToScreen(_points[i][0], _points[i][1]);
            var active = i == _dragIndex;
            ctx.DrawEllipse(new SolidColorBrush(accent, active ? 0.35 : 0.18), null, c, 11, 11);
            ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#07090C")), new Pen(new SolidColorBrush(accent), 2), c, 5.5,
                5.5);
            if (active)
                DrawLabel(ctx, $"{_points[i][0]}°C → {_points[i][1]}%", new Point(c.X + 12, c.Y - 22), accent);
        }

        if (!double.IsNaN(CurrentTemp) && CurrentTemp >= MinTemp && CurrentTemp <= MaxTemp)
        {
            var x = ToScreen(CurrentTemp, 0).X;
            ctx.DrawLine(new Pen(new SolidColorBrush(Colors.White, 0.55), 1, new DashStyle(new double[] { 2, 3 }, 0)),
                new Point(x, r.Y), new Point(x, r.Bottom));
            DrawLabel(ctx, $"NOW {CurrentTemp:0}°", new Point(x + 4, r.Y), Colors.White);
        }
    }

    private static void DrawLabel(DrawingContext ctx, string text, Point at, Color? color = null)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, 10,
            color is { } c ? new SolidColorBrush(c) : LabelBrush);
        ctx.DrawText(ft, at);
    }

    private int HitTestPoint(Point pos)
    {
        for (var i = 0; i < _points.Count; i++)
        {
            var c = ToScreen(_points[i][0], _points[i][1]);
            if (Math.Abs(c.X - pos.X) <= HitRadius && Math.Abs(c.Y - pos.Y) <= HitRadius) return i;
        }

        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        var hit = HitTestPoint(pos);
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsRightButtonPressed)
        {
            if (hit >= 0 && _points.Count > 2)
            {
                _points.RemoveAt(hit);
                Changed();
            }

            return;
        }

        if (e.ClickCount == 2 && hit < 0 && _points.Count < 10)
        {
            var (t, p) = FromScreen(pos);
            if (_points.All(x => x[0] != t))
            {
                _points.Add(new[] { t, p });
                _points.Sort((a, b) => a[0].CompareTo(b[0]));
                FixMonotonic(_points.FindIndex(x => x[0] == t));
                Changed();
            }

            return;
        }

        if (hit >= 0)
        {
            _dragIndex = hit;
            e.Pointer.Capture(this);
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragIndex < 0) return;

        var (t, p) = FromScreen(e.GetPosition(this));
        var minT = _dragIndex > 0 ? _points[_dragIndex - 1][0] + 1 : (int)MinTemp;
        var maxT = _dragIndex < _points.Count - 1 ? _points[_dragIndex + 1][0] - 1 : (int)MaxTemp;
        var minP = _dragIndex > 0 ? _points[_dragIndex - 1][1] : 0;
        var maxP = _dragIndex < _points.Count - 1 ? _points[_dragIndex + 1][1] : 100;

        _points[_dragIndex][0] = Math.Clamp(t, minT, maxT);
        _points[_dragIndex][1] = Math.Clamp(p, minP, maxP);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragIndex < 0) return;
        _dragIndex = -1;
        e.Pointer.Capture(null);
        Changed();
    }

    private void FixMonotonic(int index)
    {
        if (index > 0) _points[index][1] = Math.Max(_points[index][1], _points[index - 1][1]);
        for (var i = index + 1; i < _points.Count; i++)
            _points[i][1] = Math.Max(_points[i][1], _points[i - 1][1]);
    }

    private void Changed()
    {
        InvalidateVisual();
        CurveChanged?.Invoke(this, EventArgs.Empty);
    }
}
