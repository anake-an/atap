using Atap.Composition;
using Atap.Features.SettingsWindow;
using Atap.Features.Startup;

namespace Atap.Settings.Shell;

/// <summary>One page in the left list of the settings window.</summary>
/// <param name="Id">Used by "settings.open" to open this page.</param>
/// <param name="TitleKey">String key of the title in the list and on the page.</param>
/// <param name="HintKey">String key of the one line under the title.</param>
/// <param name="Glyph">Segoe Fluent Icons glyph next to the title.</param>
/// <param name="Sections">Section ids shown on the page: their generated settings, then their cards.</param>
/// <param name="MoreSections">Section ids shown under "More options" at the bottom of the page.</param>
/// <param name="ComingSoonKey">String key of the line shown while the page has nothing yet.</param>
/// <param name="KeyOrder">Setting keys shown first on the page, in this order.</param>
internal sealed record SettingsPage(
    string Id,
    string TitleKey,
    string HintKey,
    string Glyph,
    IReadOnlyList<string> Sections,
    IReadOnlyList<string> MoreSections,
    string ComingSoonKey,
    IReadOnlyList<string> KeyOrder);

/// <summary>
/// The six pages of the approved layout, and which <see cref="SettingsSections"/> each one shows. Every
/// section is on exactly one page, so a new setting or card always has a home.
/// </summary>
internal static class SettingsPages
{
    public const string Home = "home";
    public const string Shows = "shows";
    public const string Prayer = "prayer";
    public const string Privacy = "privacy";
    public const string About = "about";

    /// <summary>
    /// The language setting, shown on Home after Start with Windows. The language feature (feat/localization)
    /// registers it in the General section. Until that lands, Home shows only the settings that are registered.
    /// </summary>
    private const string LanguageKey = AppServices.LanguageChoiceKey;

    public static IReadOnlyList<SettingsPage> All { get; } =
    [
        new(Home, "settings.page.home", "settings.page.homeHint", "",
            [SettingsSections.General, SettingsSections.Appearance, SettingsSections.Position, SettingsSections.Behavior, SettingsSections.Accessibility],
            [SettingsSections.Advanced],
            "settings.comingSoon.generic",
            [ShowAtapDescriptor.ShowKey, StartupDescriptor.EnabledKey, LanguageKey]),
        new(Shows, "settings.page.shows", "settings.page.showsHint", "",
            [SettingsSections.Widgets, SettingsSections.Layout, SettingsSections.Profiles],
            [],
            "settings.comingSoon.generic",
            []),
        new(Prayer, "settings.page.prayer", "settings.page.prayerHint", "",
            [SettingsSections.Malaysia],
            [],
            "settings.comingSoon.prayer",
            []),
        new(Privacy, "settings.page.privacy", "settings.page.privacyHint", "",
            [SettingsSections.Privacy],
            [],
            "settings.comingSoon.generic",
            []),
        new(About, "settings.page.about", "settings.page.aboutHint", "",
            [SettingsSections.About, SettingsSections.Updates],
            [],
            "settings.comingSoon.generic",
            []),
    ];

    /// <summary>The page with this id, or the page that shows this section id. Null for anything else.</summary>
    public static SettingsPage? Find(string? pageOrSection)
    {
        if (string.IsNullOrWhiteSpace(pageOrSection))
            return null;
        var id = pageOrSection.Trim();
        return All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? All.FirstOrDefault(p => p.Sections.Concat(p.MoreSections).Contains(id, StringComparer.OrdinalIgnoreCase));
    }
}
