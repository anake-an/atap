using System.Globalization;
using Atap.Composition;
using Atap.Core.Localization;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.Language;

[assembly: AtapFeature(typeof(LanguageFeatureDescriptor))]

namespace Atap.Features.Language;

/// <summary>The language setting: Same as Windows, English or Bahasa Melayu. Startup already applies it, this feature follows changes.</summary>
internal sealed class LanguageFeatureDescriptor : IFeatureDescriptor
{
    public const string ChoiceKey = AppServices.LanguageChoiceKey;
    public const string SameAsWindows = "system";

    public string Id => "language";

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(ChoiceKey, ChoiceKey, SettingType.Choice, SameAsWindows, [SameAsWindows, Localizer.English, Localizer.Malay])
        {
            DescriptionKey = "language.choiceHint",
            SectionId = SettingsSections.General,
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    // Always on, safe mode too: it only changes text.
    public bool IsEnabled(SettingValues settings, AppMode mode) => true;

    public IAppFeature Create(FeatureContext context) =>
        new LanguageFeature(context.Settings, context.Text as Localizer ?? Localizer.Instance);
}

/// <summary>Switches the text and the UI culture when the language setting changes. Bound text updates at once.</summary>
internal sealed class LanguageFeature(IAppSettingsView settings, Localizer text) : IAppFeature
{
    public Task StartAsync()
    {
        settings.Changed += OnChanged;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        settings.Changed -= OnChanged;
        return ValueTask.CompletedTask;
    }

    /// <summary>Picks the language for a choice, then switches the text and the UI culture to it. Returns "en" or "ms".</summary>
    internal static string Apply(Localizer text, string? choice, CultureInfo windowsCulture)
    {
        var language = Localizer.ResolveChoice(choice, windowsCulture);
        text.SetLanguage(language);
        CultureInfo.CurrentUICulture = text.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = text.Culture;
        return language;
    }

    // Changed is raised on the UI thread.
    private void OnChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(LanguageFeatureDescriptor.ChoiceKey))
            Apply(text, settings.Values.GetString(LanguageFeatureDescriptor.ChoiceKey), LanguageNative.WindowsDisplayCulture());
    }
}
