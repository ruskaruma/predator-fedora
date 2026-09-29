using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PredatorCore;

/// <summary>
///     Live preview of the 4-zone RGB keyboard. Keys are split into four equal
///     vertical zones (left to right), matching how the Predator firmware maps
///     per_zone_mode colours. Brightness (0-100) dims the backlight glow.
/// </summary>
public class KeyboardPreview : Control
{
    public static readonly StyledProperty<Color> Zone1Property =
        AvaloniaProperty.Register<KeyboardPreview, Color>(nameof(Zone1), Colors.Cyan);

    public static readonly StyledProperty<Color> Zone2Property =
        AvaloniaProperty.Register<KeyboardPreview, Color>(nameof(Zone2), Colors.Cyan);

    public static readonly StyledProperty<Color> Zone3Property =
        AvaloniaProperty.Register<KeyboardPreview, Color>(nameof(Zone3), Colors.Cyan);

    public static readonly StyledProperty<Color> Zone4Property =
        AvaloniaProperty.Register<KeyboardPreview, Color>(nameof(Zone4), Colors.Cyan);

    public static readonly StyledProperty<double> BrightnessProperty =
        AvaloniaProperty.Register<KeyboardPreview, double>(nameof(Brightness), 100);

    // Relative key widths per row, roughly a 16" laptop layout without numpad detail.
    private static readonly double[][] Rows =
    {
        new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1, 1 },
        new double[] { 1.5, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1.5, 1, 1 },
        new double[] { 1.8, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2.2, 1, 1 },
        new double[] { 2.3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2.7, 1, 1 },
        new double[] { 1.3, 1, 1, 1.3, 6, 1.3, 1, 1, 1, 1, 1 }
    };

    static KeyboardPreview()
    {
        AffectsRender<KeyboardPreview>(Zone1Property, Zone2Property, Zone3Property, Zone4Property,
            BrightnessProperty);
    }

    public Color Zone1 { get => GetValue(Zone1Property); set => SetValue(Zone1Property, value); }
    public Color Zone2 { get => GetValue(Zone2Property); set => SetValue(Zone2Property, value); }
    public Color Zone3 { get => GetValue(Zone3Property); set => SetValue(Zone3Property, value); }
    public Color Zone4 { get => GetValue(Zone4Property); set => SetValue(Zone4Property, value); }
    public double Brightness { get => GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 50 || bounds.Height < 30) return;

        context.DrawRectangle(new SolidColorBrush(Color.Parse("#0A0D11"), 0.55),
            new Pen(new SolidColorBrush(Colors.White, 0.1), 1), bounds, 12, 12);

        const double pad = 14, gap = 5;
        var inner = bounds.Deflate(pad);
        var keyHeight = (inner.Height - gap * (Rows.Length - 1)) / Rows.Length;
        var glow = Math.Clamp(Brightness / 100.0, 0, 1);
        var zones = new[] { Zone1, Zone2, Zone3, Zone4 };
        var keyFill = new SolidColorBrush(Color.Parse("#12161C"));

        for (var r = 0; r < Rows.Length; r++)
        {
            var row = Rows[r];
            var totalUnits = 0.0;
            foreach (var w in row) totalUnits += w;
            var unit = (inner.Width - gap * (row.Length - 1)) / totalUnits;
            var x = inner.X;
            var y = inner.Y + r * (keyHeight + gap);

            foreach (var w in row)
            {
                var width = w * unit;
                var key = new Rect(x, y, width, keyHeight);
                var centre = (key.Center.X - inner.X) / inner.Width;
                var zone = zones[Math.Clamp((int)(centre * 4), 0, 3)];

                if (glow > 0)
                    context.DrawRectangle(new SolidColorBrush(zone, 0.28 * glow), null, key.Inflate(2), 5, 5);
                context.DrawRectangle(keyFill, new Pen(new SolidColorBrush(zone, 0.25 + 0.75 * glow), 1.2), key, 4,
                    4);
                if (glow > 0)
                    context.DrawRectangle(new SolidColorBrush(zone, 0.55 * glow), null,
                        new Rect(key.X + width * 0.3, key.Y + keyHeight * 0.42, width * 0.4, 2), 1, 1);

                x += width + gap;
            }
        }

        // Zone dividers
        var divider = new Pen(new SolidColorBrush(Color.Parse("#2A3440")), 1, new DashStyle(new double[] { 3, 3 }, 0));
        for (var i = 1; i < 4; i++)
        {
            var dx = inner.X + inner.Width * i / 4;
            context.DrawLine(divider, new Point(dx, bounds.Y + 4), new Point(dx, bounds.Bottom - 4));
        }
    }
}
