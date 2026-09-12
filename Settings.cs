using System;
using System.IO;
using System.Text.Json;

namespace ClaudeUsageWidget;

public class Settings
{
    public string Url { get; set; } = "https://claude.ai/new";
    public int RefreshSeconds { get; set; } = 60;
    public bool LaunchAtStartup { get; set; } = false;

    // Hex accent colour ("#4C9BE8"); empty means "derive it from the profile name".
    public string AccentColor { get; set; } = "";
    public double WindowLeft { get; set; } = 100;
    public double WindowTop { get; set; } = 100;

    private static string FilePath => AppProfile.SettingsPath;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<Settings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // fall through to defaults if the file is missing or corrupt
        }
        return NewForFreshProfile();
    }

    // Settings for a profile being seen for the first time. A new profile inherits
    // which page to read and how often from the default profile, since those are
    // account-independent and the factory default URL is rarely the one you want;
    // the login, position, colour and startup entry stay per-profile.
    private static Settings NewForFreshProfile()
    {
        var settings = new Settings();
        if (AppProfile.IsDefault) return settings;

        var inherited = LoadFrom(AppProfile.DefaultProfileSettingsPath);
        if (inherited != null)
        {
            settings.Url = inherited.Url;
            settings.RefreshSeconds = inherited.RefreshSeconds;
        }

        // Spread named profiles down the screen by name rather than stacking them
        // all on the same spot. Each keeps wherever you drag it afterwards.
        var slot = AppProfile.StableHash(AppProfile.Name) % 4;
        settings.WindowTop += 95 * (slot + 1);
        return settings;
    }

    private static Settings? LoadFrom(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
