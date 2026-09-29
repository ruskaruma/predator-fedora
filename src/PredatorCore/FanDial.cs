using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PredatorCore;

/// <summary>
///     Static fan graphic: a 7-blade impeller inside a ring that fills with the fan's duty
///     (percent of maximum RPM). No animation; it only redraws when Percent changes.
/// </summary>
public class FanDial : Control
{
    public static readonly StyledProperty<double> PercentProperty =
        AvaloniaProperty.Register<FanDial, double>(nameof(Percent));

    public static readonly StyledProperty<Color> AccentProperty =
        AvaloniaProperty.Register<FanDial, Color>(nameof(Accent), Color.Parse("#00E0FF"));

    private const int Blades = 7;
    private static readonly Color Track = Color.Parse("#1C2229");
    private static readonly Color Hub = Color.Parse("#0A0D11");

    static FanDial()
    {
        AffectsRender<FanDial>(PercentProperty, AccentProperty);
    }

    public double Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public Color Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 20) return;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var ringR = size / 2 - 5;
        var fraction = Math.Clamp(Percent / 100, 0, 1);
        var accent = Accent;

        // duty ring: full track, then the filled arc from the top, clockwise
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Track), 5), c, ringR, ringR);
        if (fraction > 0.005)
        {
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(OnCircle(c, ringR, -90), false);
                if (fraction >= 0.999)
                {
                    g.ArcTo(OnCircle(c, ringR, 90), new Size(ringR, ringR), 0, false, SweepDirection.Clockwise);
                    g.ArcTo(OnCircle(c, ringR, 269.9), new Size(ringR, ringR), 0, false, SweepDirection.Clockwise);
                }
                else
                {
                    g.ArcTo(OnCircle(c, ringR, -90 + 360 * fraction), new Size(ringR, ringR), 0, fraction > 0.5,
                        SweepDirection.Clockwise);
                }

                g.EndFigure(false);
            }

            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(accent, 0.22), 11, lineCap: PenLineCap.Round), arc);
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 5, lineCap: PenLineCap.Round), arc);
        }

        // shroud
        var shroudR = ringR - 9;
        ctx.DrawEllipse(new SolidColorBrush(Hub), new Pen(new SolidColorBrush(Track), 1.5), c, shroudR, shroudR);

        // swept blades: brighter as the fan works harder
        var bladeBrush = new SolidColorBrush(accent, 0.35 + 0.55 * fraction);
        var bladeEdge = new Pen(new SolidColorBrush(accent, 0.9), 1);
        var hubR = shroudR * 0.26;
        var tipR = shroudR * 0.92;
        for (var i = 0; i < Blades; i++)
        {
            var a = i * 360.0 / Blades;
            var blade = new StreamGeometry();
            using (var g = blade.Open())
            {
                g.BeginFigure(OnCircle(c, hubR, a - 14), true);
                g.QuadraticBezierTo(OnCircle(c, tipR * 0.7, a + 6), OnCircle(c, tipR, a + 28));
                g.QuadraticBezierTo(OnCircle(c, tipR * 0.98, a + 44), OnCircle(c, tipR * 0.9, a + 50));
                g.QuadraticBezierTo(OnCircle(c, tipR * 0.55, a + 30), OnCircle(c, hubR, a + 22));
                g.EndFigure(true);
            }

            ctx.DrawGeometry(bladeBrush, bladeEdge, blade);
        }

        // hub cap
        ctx.DrawEllipse(new SolidColorBrush(Hub), new Pen(new SolidColorBrush(accent), 2), c, hubR, hubR);
        ctx.DrawEllipse(new SolidColorBrush(accent, 0.8), null, c, hubR * 0.35, hubR * 0.35);
    }

    private static Point OnCircle(Point c, double r, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }
}
