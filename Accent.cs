using System;
using System.Windows.Media;

namespace ClaudeUsageWidget;

// Per-profile accent colour, so several widgets running on different logins are
// distinguishable at a glance. By default a profile's colour is derived from its
// name (the same name always gets the same colour); Settings can override it.
public static class AccentPalette
{
    public record Choice(string Name, string Hex);

    // Mid-tone hues that stay legible as a thin stripe and as small text on the
    // widget's dark background. The first AutoPaletteCount of them are also the pool
    // that unconfigured profiles are hashed into, so new colours get appended rather
    // than inserted: a profile's automatic colour must not change under it.
    public static readonly Choice[] Named =
    [
        new("Blue", "#4C9BE8"),
        new("Amber", "#E8A33D"),
        new("Green", "#5FC98B"),
        new("Purple", "#BD82E0"),
        new("Red", "#E8655F"),
        new("Teal", "#49C5C5"),
        new("Pink", "#E87BB0"),
        new("Grey", "#9AA0A6"),
        new("Sky", "#5FD0F0"),
        new("Indigo", "#7B8CF0"),
        new("Lavender", "#B9A8F0"),
        new("Magenta", "#E066D0"),
        new("Rose", "#F0788C"),
        new("Orange", "#F0864C"),
        new("Gold", "#D9C24A"),
        new("Lime", "#A8D44C"),
        new("Mint", "#6FE0B4"),
        new("Slate", "#7D93A8"),
    ];

    private const int AutoPaletteCount = 8;

    // Returns the accent to paint, or null for "no accent" — the default profile
    // with no explicit override keeps the original, stripe-free look.
    public static Color? Resolve(string? configuredHex, string profileName)
    {
        if (TryParse(configuredHex, out var configured)) return configured;
        if (string.Equals(profileName, AppProfile.DefaultName, StringComparison.OrdinalIgnoreCase)) return null;
        return Parse(Named[(int)(AppProfile.StableHash(profileName) % AutoPaletteCount)].Hex);
    }

    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            color = Parse(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}
