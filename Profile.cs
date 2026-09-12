using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ClaudeUsageWidget;

// Each instance of the widget runs under a named profile, which owns its own
// settings file and its own WebView2 user-data folder (and therefore its own
// claude.ai login). Pass --profile <name> on the command line to pick one;
// with no switch the app uses the "default" profile, whose data stays at the
// original %AppData%\ClaudeUsageWidget paths so existing installs keep their
// settings and session.
public static class AppProfile
{
    public const string DefaultName = "default";

    public static string Name { get; } = Sanitize(ParseFromCommandLine());

    public static bool IsDefault => string.Equals(Name, DefaultName, StringComparison.OrdinalIgnoreCase);

    private static string RootDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageWidget");

    // The default profile keeps the legacy layout; named profiles get a subfolder.
    public static string DataDir => IsDefault ? RootDir : Path.Combine(RootDir, "Profiles", Name);

    public static string SettingsPath => Path.Combine(DataDir, "settings.json");

    public static string WebView2DataDir => Path.Combine(DataDir, "WebView2Data");

    // A new profile seeds its page/refresh settings from the default profile.
    public static string DefaultProfileSettingsPath => Path.Combine(RootDir, "settings.json");

    private static string ParseFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--profile", StringComparison.OrdinalIgnoreCase) &&
                !arg.StartsWith("-p", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var eq = arg.IndexOf('=');
            if (eq >= 0) return arg[(eq + 1)..];
            if (i + 1 < args.Length) return args[i + 1];
        }
        return DefaultName;
    }

    // string.GetHashCode is randomised per process, which would hand the same profile
    // a different colour and screen position on every launch — so hash names here.
    public static uint StableHash(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value.ToLowerInvariant())
        {
            hash = (hash ^ c) * 16777619u;
        }
        return hash;
    }

    // Profile names end up as a folder name and a registry value name, so keep
    // them to something safe and bounded rather than trusting the argument.
    private static string Sanitize(string name)
    {
        name = name.Trim().Trim('"');
        if (string.IsNullOrEmpty(name)) return DefaultName;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }

        var cleaned = sb.ToString().Trim('.', ' ');
        if (cleaned.Length == 0) return DefaultName;
        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }
}
