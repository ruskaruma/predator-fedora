using System;
using Avalonia;

namespace PredatorCore;

internal class Program
{
    // Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant code before
    // AppMain is called: things aren't initialized yet, and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless command for keyboard shortcuts: switch to the next mode and exit without opening a window.
        if (args.Length > 0 && args[0] == "--cycle-mode") return CycleMode();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static int CycleMode()
    {
        try
        {
            using var client = new DaemonClient();
            if (!client.ConnectAsync().GetAwaiter().GetResult()) return 1;
            var reply = client.SendCommandAsync("cycle_thermal_profile").GetAwaiter().GetResult();
            return reply.RootElement.GetProperty("success").GetBoolean() ? 0 : 1; // the daemon shows the popup
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"predatorcore: {ex.Message}");
            return 1;
        }
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Matches StartupWMClass in the desktop entry so the dock shows the right icon.
            .With(new X11PlatformOptions { WmClass = "PredatorCore" })
            .WithInterFont()
            .LogToTrace();
}
