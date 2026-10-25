using Atap.Core.Alerts;

namespace Atap.Notch.Surfaces.Alerts;

/// <summary>
/// Glyphs from the icon font for the common <see cref="Alert.IconKey"/> values. An unknown or missing key
/// shows the glyph for the tone. Icon geometry from the UI kit can take over later.
/// </summary>
public static class AlertIcons
{
    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["info"] = "",
        ["done"] = "",
        ["warning"] = "",
        ["error"] = "",
        ["alarm"] = "",
        ["bell"] = "",
        ["timer"] = "",
        ["clock"] = "",
        ["calendar"] = "",
        ["volume"] = "",
        ["mute"] = "",
        ["brightness"] = "",
        ["keyboard"] = "",
        ["battery"] = "",
        ["wifi"] = "",
        ["mail"] = "",
        ["update"] = "",
        ["download"] = "",
        ["moon"] = "",
    };

    public static string GlyphFor(string? iconKey, AlertTone tone)
    {
        if (iconKey is not null && Glyphs.TryGetValue(iconKey, out var glyph))
            return glyph;
        return tone switch
        {
            AlertTone.Positive => Glyphs["done"],
            AlertTone.Warning => Glyphs["warning"],
            AlertTone.Negative => Glyphs["error"],
            _ => Glyphs["info"],
        };
    }
}
