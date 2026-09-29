using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PredatorCore;

/// <summary>
///     PredatorSense-style 270° arc gauge. Draws only the track and value arc;
///     the numeric readout is layered on top in XAML so it inherits text styles.
///     The arc colour shifts from accent to warning to critical as it fills.
/// </summary>
public class ArcGauge : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ArcGauge, double>(nameof(Value));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ArcGauge, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> WarnAtProperty =
        AvaloniaProperty.Register<ArcGauge, double>(nameof(WarnAt), 0.75);

    public static readonly StyledProperty<double> CriticalAtProperty =
        AvaloniaProperty.Register<ArcGauge, double>(nameof(CriticalAt), 0.9);

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ArcGauge, double>(nameof(Thickness), 10);

    public static readonly StyledProperty<Color> AccentProperty =
        AvaloniaProperty.Register<ArcGauge, Color>(nameof(Accent), Color.Parse("#00E0FF"));

    private static readonly Color Track = Color.Parse("#1C2229");
    private static readonly Color Warn = Color.Parse("#FFB020");
    private static readonly Color Critical = Color.Parse("#FF3B4E");

    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    static ArcGauge()
    {
        AffectsRender<ArcGauge>(ValueProperty, MaximumProperty, AccentProperty, ThicknessProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Fraction of Maximum at which the arc turns amber.</summary>
    public double WarnAt
    {
        get => GetValue(WarnAtProperty);
        set => SetValue(WarnAtProperty, value);
    }

    /// <summary>Fraction of Maximum at which the arc turns red.</summary>
    public double CriticalAt
    {
        get => GetValue(CriticalAtProperty);
        set => SetValue(CriticalAtProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Color Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= Thickness * 2) return;

        var radius = (size - Thickness) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        DrawArc(context, center, radius, SweepAngle, new Pen(new SolidColorBrush(Track), Thickness,
            lineCap: PenLineCap.Round));

        var fraction = Maximum > 0 ? Math.Clamp(Value / Maximum, 0, 1) : 0;
        if (fraction <= 0.002) return;

        var color = fraction >= CriticalAt ? Critical : fraction >= WarnAt ? Warn : Accent;

        // Soft glow underneath, then the crisp value arc.
        DrawArc(context, center, radius, SweepAngle * fraction,
            new Pen(new SolidColorBrush(color, 0.18), Thickness * 2.2, lineCap: PenLineCap.Round));
        DrawArc(context, center, radius, SweepAngle * fraction,
            new Pen(new SolidColorBrush(color), Thickness, lineCap: PenLineCap.Round));
    }

    private static void DrawArc(DrawingContext context, Point center, double radius, double sweep, IPen pen)
    {
        var start = PointOnCircle(center, radius, StartAngle);
        var end = PointOnCircle(center, radius, StartAngle + sweep);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var rad = angleDegrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }
}
