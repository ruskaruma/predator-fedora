using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;

namespace PredatorCore;

/// <summary>Automation page, hotkey setup and the optional tray icon.</summary>
public partial class MainWindow
{
    private const string HotkeyBinding = "<Super>F5";
    private static readonly string[] ProfileIds = { "low-power", "quiet", "balanced", "balanced-performance", "performance" };
    private static readonly string[] ProfileNames = { "Eco", "Quiet", "Balanced", "Performance", "Turbo" };

    private readonly AppPreferences _prefs = AppPreferences.Load();
    private TrayIcon? _tray;
    private DispatcherTimer? _trayTimer;
    private bool _quitting;
    private bool _closeHandlerAttached;

    /// <summary>Called after every settings load.</summary>
    private void ApplyAutomationSettings()
    {
        var auto = _settings?.Automation;
        if (this.FindControl<TabItem>("AutomationPanel") is { } page) page.IsVisible = auto != null;
        if (auto == null) return;

        SetToggle("AutoBatteryEnabled", auto.BatteryEnabled);
        SelectByTag("AutoBatteryProfile", auto.BatteryProfile);
        SetCheck("AutoBatteryDim", auto.BatteryDimKeyboard);
        if (this.FindControl<Slider>("AutoBatteryBrightness") is { } dim) dim.Value = auto.BatteryKeyboardBrightness;
        SetCheck("AutoAcRestore", auto.AcRestoreProfile);

        SetToggle("AutoGameEnabled", auto.GameEnabled);
        SelectByTag("AutoGameProfile", auto.GameProfile);
        SelectByTag("AutoGameFans", auto.GameFans);
        if (this.FindControl<TextBox>("AutoGameProcesses") is { } procs) procs.Text = auto.GameProcesses;
        SetText(this.FindControl<TextBlock>("AutoGameStatus"),
            string.IsNullOrEmpty(auto.ActiveGame) ? "No game running" : $"Active now: {auto.ActiveGame}");

        SetToggle("AutoHeatEnabled", auto.HeatGuardEnabled);
        if (this.FindControl<Slider>("AutoHeatTemp") is { } heat) heat.Value = auto.HeatGuardTemp;
        SetCheck("AutoHeatStepDown", auto.HeatGuardStepDown);
        SetCheck("AutoNotify", auto.NotifyModeChanges);

        SetCheck("TrayEnabledCheckBox", _prefs.TrayEnabled);
        SetCheck("CloseToTrayCheckBox", _prefs.CloseToTray);
        ShowHotkeyStatus();
    }

    private async void SaveAutomation_OnClick(object? sender, RoutedEventArgs e)
    {
        _prefs.TrayEnabled = IsChecked("TrayEnabledCheckBox");
        _prefs.CloseToTray = _prefs.TrayEnabled && IsChecked("CloseToTrayCheckBox");
        _prefs.Save();
        UpdateTray();

        if (!_isConnected) return;
        var changes = new Dictionary<string, object>
        {
            ["battery_enabled"] = IsToggled("AutoBatteryEnabled"),
            ["battery_profile"] = SelectedTag("AutoBatteryProfile") ?? "low-power",
            ["battery_dim_keyboard"] = IsChecked("AutoBatteryDim"),
            ["battery_keyboard_brightness"] = (int)Math.Round(this.FindControl<Slider>("AutoBatteryBrightness")?.Value ?? 30),
            ["ac_restore_profile"] = IsChecked("AutoAcRestore"),
            ["game_enabled"] = IsToggled("AutoGameEnabled"),
            ["game_profile"] = SelectedTag("AutoGameProfile") ?? "performance",
            ["game_fans"] = SelectedTag("AutoGameFans") ?? "keep",
            ["game_processes"] = this.FindControl<TextBox>("AutoGameProcesses")?.Text?.Trim() ?? "",
            ["heat_guard_enabled"] = IsToggled("AutoHeatEnabled"),
            ["heat_guard_temp"] = (int)Math.Round(this.FindControl<Slider>("AutoHeatTemp")?.Value ?? 95),
            ["heat_guard_step_down"] = IsChecked("AutoHeatStepDown"),
            ["notify_mode_changes"] = IsChecked("AutoNotify")
        };
        try
        {
            var (ok, error) = await _client.SetAutomationAsync(changes);
            if (!ok) await ShowMessageBox("Automation", error ?? "The daemon rejected these settings.");
        }
        catch (Exception ex)
        {
            await ShowMessageBox("Automation", $"Failed to save: {ex.Message}");
        }

        await LoadSettingsAsync();
    }

    private async void TryHotkey_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_isConnected) return;
        await _client.CycleThermalProfileAsync();
        await LoadSettingsAsync();
    }

    private async void SetupHotkey_OnClick(object? sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath ?? "/opt/predatorcore/PredatorCore";
        if (!GnomeShortcut.Register(HotkeyBinding, $"{exe} --cycle-mode"))
            await ShowMessageBox("Hotkey", "Couldn't register the shortcut. Add it in Settings → Keyboard → Custom Shortcuts " +
                                           $"with the command:\n{exe} --cycle-mode");
        ShowHotkeyStatus();
    }

    private void ShowHotkeyStatus()
    {
        var binding = GnomeShortcut.CurrentBinding();
        SetText(this.FindControl<TextBlock>("HotkeyStatusText"),
            binding == null ? "No mode-switch shortcut set up yet" : $"Press {GnomeShortcut.Pretty(binding)} to switch modes");
        if (this.FindControl<Button>("SetupHotkeyButton") is { } button)
            button.Content = binding == null ? "SET UP SUPER+F5" : "RESET TO SUPER+F5";
    }

    // ---------- tray ----------

    private void InitTray()
    {
        if (!_closeHandlerAttached)
        {
            _closeHandlerAttached = true;
            Closing += (_, args) =>
            {
                if (_quitting || !_prefs.CloseToTray || _tray == null) return;
                args.Cancel = true; // keep running in the tray
                Hide();
            };
        }

        UpdateTray();
    }

    private void UpdateTray()
    {
        if (!_prefs.TrayEnabled)
        {
            _trayTimer?.Stop();
            if (_tray != null) _tray.IsVisible = false;
            return;
        }

        EnableGnomeTraySupport();
        if (_tray == null)
        {
            _tray = new TrayIcon { Icon = LoadTrayIcon(), ToolTipText = "PredatorCore", Menu = BuildTrayMenu() };
            _tray.Clicked += (_, _) => ShowFromTray();
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
            _trayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _trayTimer.Tick += (_, _) => UpdateTrayTooltip();
        }

        _tray.IsVisible = true;
        UpdateTrayTooltip();
        _trayTimer?.Start();
    }

    private NativeMenu BuildTrayMenu()
    {
        var menu = new NativeMenu();
        menu.Add(MenuItem("Open PredatorCore", ShowFromTray));
        menu.Add(new NativeMenuItemSeparator());

        var modes = new NativeMenuItem("Mode") { Menu = new NativeMenu() };
        for (var i = 0; i < ProfileIds.Length; i++)
        {
            var id = ProfileIds[i];
            modes.Menu.Add(MenuItem(ProfileNames[i], async () =>
            {
                if (_isConnected) await _client.SetThermalProfileAsync(id);
                await LoadSettingsAsync();
            }));
        }

        menu.Add(modes);
        menu.Add(MenuItem("Next mode", async () =>
        {
            if (_isConnected) await _client.CycleThermalProfileAsync();
            await LoadSettingsAsync();
        }));

        var fans = new NativeMenuItem("Fans") { Menu = new NativeMenu() };
        fans.Menu.Add(MenuItem("Auto", async () => { if (_isConnected) await _client.SetFanSpeedAsync(0, 0); await LoadSettingsAsync(); }));
        fans.Menu.Add(MenuItem("Max", async () => { if (_isConnected) await _client.SetFanSpeedAsync(100, 100); await LoadSettingsAsync(); }));
        fans.Menu.Add(MenuItem("My curve", async () => await ApplyFanCurveAsync(true)));
        menu.Add(fans);

        menu.Add(new NativeMenuItemSeparator());
        menu.Add(MenuItem("Quit", () =>
        {
            _quitting = true;
            if (_tray != null) _tray.IsVisible = false;
            Close();
        }));
        return menu;
    }

    private static NativeMenuItem MenuItem(string header, Action onClick)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => onClick();
        return item;
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Tooltip from sysfs only (no daemon call, no GPU access), so it costs next to nothing.</summary>
    private void UpdateTrayTooltip()
    {
        if (_tray == null) return;
        var profile = ReadFile("/sys/firmware/acpi/platform_profile");
        var name = Array.IndexOf(ProfileIds, profile) is var i and >= 0 ? ProfileNames[i] : profile ?? "?";
        var temp = Directory.Exists("/sys/class/hwmon")
            ? Directory.GetDirectories("/sys/class/hwmon")
                .Where(d => ReadFile($"{d}/name") == "coretemp")
                .Select(d => ReadFile($"{d}/temp1_input")).FirstOrDefault()
            : null;
        var cpu = int.TryParse(temp, out var milli) ? $"CPU {milli / 1000}°C · " : "";
        _tray.ToolTipText = $"PredatorCore · {cpu}{name}";
    }

    private static WindowIcon LoadTrayIcon()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "predatorcore", "icons", "icon.png");
        return File.Exists(local)
            ? new WindowIcon(local)
            : new WindowIcon(AssetLoader.Open(new Uri("avares://PredatorCore/icon.png")));
    }

    /// <summary>Stock GNOME hides tray icons; the AppIndicator extension (installed on Fedora) shows them.</summary>
    private static void EnableGnomeTraySupport()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("gnome-extensions",
                "enable appindicatorsupport@rgcjonas.gmail.com") { RedirectStandardError = true });
            p?.WaitForExit(3000);
        }
        catch
        {
            // not GNOME, or the extension isn't installed: the tray may still work elsewhere
        }
    }

    private static string? ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }

    // ---------- small control helpers ----------

    private void SetToggle(string name, bool value)
    {
        if (this.FindControl<ToggleSwitch>(name) is { } t) t.IsChecked = value;
    }

    private void SetCheck(string name, bool value)
    {
        if (this.FindControl<CheckBox>(name) is { } c) c.IsChecked = value;
    }

    private bool IsToggled(string name) => this.FindControl<ToggleSwitch>(name)?.IsChecked == true;
    private bool IsChecked(string name) => this.FindControl<CheckBox>(name)?.IsChecked == true;

    private void SelectByTag(string name, string tag)
    {
        if (this.FindControl<ComboBox>(name) is not { } combo) return;
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == tag)
                             ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private string? SelectedTag(string name) =>
        (this.FindControl<ComboBox>(name)?.SelectedItem as ComboBoxItem)?.Tag as string;
}
