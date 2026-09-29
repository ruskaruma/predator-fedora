using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace PredatorCore;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var accentColor = Color.Parse("#00E0FF");
        Application.Current.Resources["SystemAccentColor"] = accentColor;
        Application.Current.Resources["SystemAccentBrush"] = new SolidColorBrush(accentColor);

    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();


        base.OnFrameworkInitializationCompleted();
    }
}