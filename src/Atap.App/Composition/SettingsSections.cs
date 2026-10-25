namespace Atap.Composition;

/// <summary>The fixed pages of the settings window, in order. Titles are strings "app.section.&lt;id&gt;".</summary>
internal static class SettingsSections
{
    public const string General = "general";
    public const string Appearance = "appearance";
    public const string Position = "position";
    public const string Widgets = "widgets";
    public const string Layout = "layout";
    public const string Profiles = "profiles";
    public const string Malaysia = "malaysia";
    public const string Behavior = "behavior";
    public const string Advanced = "advanced";
    public const string Accessibility = "accessibility";
    public const string Privacy = "privacy";
    public const string Updates = "updates";
    public const string About = "about";

    public static IReadOnlyList<string> All { get; } =
    [
        General, Appearance, Position, Widgets, Layout, Profiles, Malaysia,
        Behavior, Advanced, Accessibility, Privacy, Updates, About,
    ];

    public static string TitleKey(string sectionId) => $"app.section.{sectionId}";
}
