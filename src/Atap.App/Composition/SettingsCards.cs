using Atap.Core.Widgets;

namespace Atap.Composition;

/// <summary>
/// A hand-made card on a settings page, below the generated form. Created only while the settings
/// window is open, and its view model is dropped when the window closes.
/// </summary>
internal interface ISettingsCard
{
    string Id { get; }

    /// <summary>A <see cref="SettingsSections"/> id.</summary>
    string SectionId { get; }

    /// <summary>Cards sort by this within a page, lowest first.</summary>
    int Order { get; }

    string TitleKey { get; }

    /// <summary>The card's XAML dictionary. Merged into the settings window only while it is open.</summary>
    Uri ViewsSource { get; }

    object CreateViewModel(SettingsCardContext context);
}

/// <param name="Services">Everything the app has.</param>
/// <param name="Lifetime">Ends when the settings window closes.</param>
internal sealed record SettingsCardContext(AppServices Services, IWidgetLifetime Lifetime);
