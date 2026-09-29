using System;
using Avalonia;

namespace PredatorCore;

internal class Program
{
    // Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant code before
    // AppMain is called: things aren't initialized yet, and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Matches StartupWMClass in the desktop entry so the dock shows the right icon.
            .With(new X11PlatformOptions { WmClass = "PredatorCore" })
            .WithInterFont()
            .LogToTrace();
}
