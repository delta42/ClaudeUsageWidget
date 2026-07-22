using System;
using System.IO;
using System.Text.Json;

namespace ClaudeUsageWidget;

public class Settings
{
    public string Url { get; set; } = "https://claude.ai/new";
    public int RefreshSeconds { get; set; } = 60;
    public bool LaunchAtStartup { get; set; } = false;
    public double WindowLeft { get; set; } = 100;
    public double WindowTop { get; set; } = 100;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageWidget", "settings.json");

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
        return new Settings();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
