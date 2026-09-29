using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PredatorCore;

/// <summary>GUI-only preferences, stored in ~/.config/predatorcore/settings.json.</summary>
public sealed class AppPreferences
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "predatorcore", "settings.json");

    public bool TrayEnabled { get; set; }
    public bool CloseToTray { get; set; }

    public static AppPreferences Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(FilePath)) ?? new AppPreferences()
                : new AppPreferences();
        }
        catch
        {
            return new AppPreferences();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not save preferences: {ex.Message}");
        }
    }
}

/// <summary>Registers a GNOME custom keyboard shortcut (the same thing Settings → Keyboard does).</summary>
public static class GnomeShortcut
{
    private const string Schema = "org.gnome.settings-daemon.plugins.media-keys";
    private const string Path = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/predatorcore/";
    private const string Entry = "org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:" + Path;

    public static string? CurrentBinding()
    {
        var list = Gsettings("get", Schema, "custom-keybindings");
        if (list == null || !list.Contains(Path)) return null;
        var binding = Gsettings("get", Entry, "binding")?.Trim().Trim('\'');
        return string.IsNullOrEmpty(binding) ? null : binding;
    }

    public static bool Register(string binding, string command)
    {
        var list = Gsettings("get", Schema, "custom-keybindings");
        if (list == null) return false;
        var paths = Regex.Matches(list, "'([^']+)'").Select(m => m.Groups[1].Value).ToList();
        if (!paths.Contains(Path)) paths.Add(Path);
        var value = "[" + string.Join(", ", paths.Select(p => $"'{p}'")) + "]";

        return Gsettings("set", Schema, "custom-keybindings", value) != null &&
               Gsettings("set", Entry, "name", "PredatorCore: next mode") != null &&
               Gsettings("set", Entry, "command", command) != null &&
               Gsettings("set", Entry, "binding", binding) != null;
    }

    /// <summary>Human-readable form, e.g. "&lt;Super&gt;F5" → "Super+F5".</summary>
    public static string Pretty(string binding) =>
        Regex.Replace(binding, "<([^>]+)>", "$1+").Replace("Primary", "Ctrl").Replace("Control", "Ctrl");

    private static string? Gsettings(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("gsettings") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return p.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
