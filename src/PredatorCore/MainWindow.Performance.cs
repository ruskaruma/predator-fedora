using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PredatorCore;

/// <summary>CPU power limits and fan-curve mode on the Performance page.</summary>
public partial class MainWindow
{
    private static readonly Dictionary<string, (List<int[]> cpu, List<int[]> gpu)> FanCurvePresets = new()
    {
        ["silent"] = (Curve((55, 0), (70, 30), (80, 50), (88, 75), (93, 100)),
            Curve((55, 0), (68, 30), (76, 50), (83, 75), (88, 100))),
        ["balanced"] = (Curve((50, 0), (65, 35), (75, 55), (85, 80), (92, 100)),
            Curve((50, 0), (65, 35), (72, 55), (80, 80), (87, 100))),
        ["aggressive"] = (Curve((45, 25), (60, 45), (70, 65), (80, 85), (88, 100)),
            Curve((45, 25), (58, 45), (67, 65), (76, 85), (84, 100)))
    };

    private DispatcherTimer? _fanCurveLiveTimer;
    private bool _updatingPowerSliders;

    private static List<int[]> Curve(params (int t, int p)[] points) =>
        points.Select(x => new[] { x.t, x.p }).ToList();

    /// <summary>Called at the end of ApplySettingsToUI.</summary>
    private void ApplyPerformanceExtras()
    {
        var hasPowerLimits = _settings?.PowerLimits != null && _client.IsFeatureAvailable("power_limits");
        var hasFanCurve = _settings?.FanCurve != null && _client.IsFeatureAvailable("fan_curve");

        if (this.FindControl<Border>("PowerLimitsPanel") is { } powerPanel) powerPanel.IsVisible = hasPowerLimits;
        if (this.FindControl<RadioButton>("CurveFanRadioButton") is { } curveRadio)
        {
            curveRadio.IsVisible = hasFanCurve;
            if (hasFanCurve && _settings!.FanCurve!.Enabled)
            {
                // A running curve changes fan_speed constantly, which the base logic reads as "Custom".
                curveRadio.IsChecked = true;
                if (_manualFanSpeedRadioButton != null) _manualFanSpeedRadioButton.IsChecked = false;
                if (_autoFanSpeedRadioButton != null) _autoFanSpeedRadioButton.IsChecked = false;
                if (_maxFanSpeedRadioButton != null) _maxFanSpeedRadioButton.IsChecked = false;
                _isManualFanControl = false;
            }
        }

        if (hasPowerLimits) ShowPowerLimits(_settings!.PowerLimits!);

        if (hasFanCurve && this.FindControl<FanCurveEditor>("CpuCurveEditor") is { } cpuEditor &&
            this.FindControl<FanCurveEditor>("GpuCurveEditor") is { } gpuEditor)
        {
            cpuEditor.Points = _settings!.FanCurve!.Cpu;
            gpuEditor.Points = _settings.FanCurve.Gpu;
            cpuEditor.SafetyTemp = gpuEditor.SafetyTemp = _settings.FanCurve.SafetyTemp;
        }

        EnsurePerformanceHandlers();

        ApplyAutomationSettings();

        if (this.FindControl<Dashboard>("DashboardView") is { } dashboard)
            dashboard.AttachDaemon(_client, _client.IsFeatureAvailable("gpu_power") ? _settings?.GpuPower : null);
    }

    private void EnsurePerformanceHandlers()
    {
        if (_fanCurveLiveTimer != null) return;

        if (this.FindControl<Slider>("Pl1Slider") is { } pl1 && this.FindControl<Slider>("Pl2Slider") is { } pl2)
        {
            pl1.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                SetText(this.FindControl<TextBlock>("Pl1Text"), $"{pl1.Value:0} W");
                if (!_updatingPowerSliders && pl2.Value < pl1.Value) pl2.Value = pl1.Value; // PL2 can't be below PL1
            };
            pl2.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                SetText(this.FindControl<TextBlock>("Pl2Text"), $"{pl2.Value:0} W");
                if (!_updatingPowerSliders && pl2.Value < pl1.Value) pl1.Value = pl2.Value;
            };
        }

        // Live temperature marker on the curve editors, only while the curve panel is actually visible.
        _fanCurveLiveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _fanCurveLiveTimer.Tick += async (_, _) =>
        {
            if (!_isConnected || WindowState == WindowState.Minimized ||
                this.FindControl<RadioButton>("CurveFanRadioButton") is not { IsChecked: true, IsEffectivelyVisible: true })
                return;
            try
            {
                var status = await _client.GetFanCurveAsync();
                if (status != null) ShowFanCurveLive(status);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fan curve status error: {ex.Message}");
            }
        };
        _fanCurveLiveTimer.Start();
    }

    private void ShowPowerLimits(PowerLimitSettings limits)
    {
        _updatingPowerSliders = true;
        if (this.FindControl<Slider>("Pl1Slider") is { } pl1)
        {
            pl1.Minimum = limits.Pl1Min;
            pl1.Maximum = limits.Pl1Max;
            pl1.Value = limits.Enabled ? limits.Pl1 : limits.EffectivePl1 ?? limits.Pl1;
        }

        if (this.FindControl<Slider>("Pl2Slider") is { } pl2)
        {
            pl2.Maximum = limits.Pl2Max;
            pl2.Value = limits.Enabled ? limits.Pl2 : limits.EffectivePl2 ?? limits.Pl2;
        }

        _updatingPowerSliders = false;

        var source = limits.Enabled ? "CUSTOM" : "STOCK";
        SetText(this.FindControl<TextBlock>("PowerLimitsStatusText"),
            $"{source} · NOW {limits.EffectivePl1?.ToString() ?? "?"} / {limits.EffectivePl2?.ToString() ?? "?"} W");
    }

    private void ShowFanCurveLive(FanCurveSettings status)
    {
        if (this.FindControl<FanCurveEditor>("CpuCurveEditor") is { } cpu)
            cpu.CurrentTemp = status.CpuTemp ?? double.NaN;
        if (this.FindControl<FanCurveEditor>("GpuCurveEditor") is { } gpu)
            gpu.CurrentTemp = status.GpuTemp ?? status.CpuTemp ?? double.NaN;

        string Describe(double? temp, int pct) =>
            temp is null ? "—" : $"{temp:0}°C → {(pct == 0 ? "AUTO" : pct + "%")}";

        SetText(this.FindControl<TextBlock>("CpuCurveLiveText"), Describe(status.CpuTemp, status.CpuPercent));
        SetText(this.FindControl<TextBlock>("GpuCurveLiveText"),
            status.GpuTemp is null && status.CpuTemp is not null
                ? $"GPU ASLEEP · FOLLOWS CPU → {(status.GpuPercent == 0 ? "AUTO" : status.GpuPercent + "%")}"
                : Describe(status.GpuTemp, status.GpuPercent));
    }

    private void PowerPreset_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var parts = tag.Split(',').Select(int.Parse).ToArray();
        _updatingPowerSliders = true;
        if (this.FindControl<Slider>("Pl2Slider") is { } pl2) pl2.Value = parts[1];
        if (this.FindControl<Slider>("Pl1Slider") is { } pl1) pl1.Value = parts[0];
        _updatingPowerSliders = false;
    }

    private async void ApplyPowerLimits_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_isConnected) return;
        var pl1 = (int)Math.Round(this.FindControl<Slider>("Pl1Slider")?.Value ?? 65);
        var pl2 = (int)Math.Round(this.FindControl<Slider>("Pl2Slider")?.Value ?? 157);
        try
        {
            if (!await _client.SetPowerLimitsAsync(pl1, pl2))
                await ShowMessageBox("Power limits", "The daemon rejected these power limits.");
        }
        catch (Exception ex)
        {
            await ShowMessageBox("Power limits", $"Failed to set power limits: {ex.Message}");
        }

        await LoadSettingsAsync();
    }

    private async void ResetPowerLimits_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_isConnected) return;
        try
        {
            await _client.ResetPowerLimitsAsync();
        }
        catch (Exception ex)
        {
            await ShowMessageBox("Power limits", $"Failed to reset power limits: {ex.Message}");
        }

        await LoadSettingsAsync();
    }

    private async void CurveFanRadioButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _isManualFanControl = false;
        await ApplyFanCurveAsync(true);
    }

    private void FanCurvePreset_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || !FanCurvePresets.TryGetValue(key, out var preset)) return;
        if (this.FindControl<FanCurveEditor>("CpuCurveEditor") is { } cpu) cpu.Points = preset.cpu;
        if (this.FindControl<FanCurveEditor>("GpuCurveEditor") is { } gpu) gpu.Points = preset.gpu;
    }

    private async void ApplyFanCurve_OnClick(object? sender, RoutedEventArgs e) => await ApplyFanCurveAsync(true);

    private async System.Threading.Tasks.Task ApplyFanCurveAsync(bool enabled)
    {
        if (!_isConnected) return;
        var cpu = this.FindControl<FanCurveEditor>("CpuCurveEditor")?.Points;
        var gpu = this.FindControl<FanCurveEditor>("GpuCurveEditor")?.Points;
        if (cpu == null || gpu == null) return;

        try
        {
            var (ok, error) = await _client.SetFanCurveAsync(enabled, cpu, gpu);
            if (!ok) await ShowMessageBox("Fan curve", error ?? "The daemon rejected this fan curve.");
        }
        catch (Exception ex)
        {
            await ShowMessageBox("Fan curve", $"Failed to apply fan curve: {ex.Message}");
        }

        await LoadSettingsAsync();
    }
}
