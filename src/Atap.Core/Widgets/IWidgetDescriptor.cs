namespace Atap.Core.Widgets;

/// <summary>
/// Describes a widget type. Created once at startup for every registered type, enabled or not,
/// so it must stay cheap: a parameterless constructor and references to Core types and constants only.
/// Widget code, view models, views and heavy packages are reached only inside <see cref="Create"/>.
/// </summary>
public interface IWidgetDescriptor
{
    /// <summary>Matches <c>^[a-z][a-z0-9-]{1,31}$</c>, for example "media". Never changes once released.</summary>
    string TypeId { get; }

    /// <summary>String key of the name shown in the gallery and in settings.</summary>
    string NameKey { get; }

    string DescriptionKey { get; }

    WidgetGroup Group { get; }

    IReadOnlyList<WidgetSize> SupportedSizes { get; }

    IReadOnlyList<NotchState> SupportedStates { get; }

    /// <summary>Settings schema for each instance. Values live in layout.json under widgetSettings.</summary>
    IReadOnlyList<SettingDefinition> Settings { get; }

    WidgetCapabilities Capabilities { get; }

    /// <summary>True when the user can add several, each with an id such as "json-url#nas".</summary>
    bool AllowsMultipleInstances { get; }

    /// <summary>True when the widget stays enabled after its last placement is removed (alarms, reminders).</summary>
    bool WorksWithoutPlacement { get; }

    /// <summary>
    /// The widget's XAML dictionary, for example "/Atap;component/Widgets/Clock/ClockViews.xaml".
    /// It is merged when the first instance starts and removed after the last one stops.
    /// </summary>
    Uri ViewsSource { get; }

    /// <summary>Creates one instance. Called on the UI thread, only for enabled instances.</summary>
    IWidget Create(WidgetContext context);
}

/// <summary>What a widget may do. Shown to the user before it is enabled.</summary>
[Flags]
public enum WidgetPermissions
{
    None = 0,
    Network = 1,

    /// <summary>Fetches from hosts the user types in, not only <see cref="WidgetCapabilities.NetworkHosts"/>.</summary>
    UserUrls = 2,
    Files = 4,
    Clipboard = 8,
    Keyboard = 16,
    RunsPrograms = 32,
    Location = 64,
}

/// <param name="Permissions">What the widget may do.</param>
/// <param name="NetworkHosts">The only hosts its HTTP client may reach, over https.</param>
/// <param name="PrivacyNoteKey">String key of a note shown before it is enabled, if any.</param>
public sealed record WidgetCapabilities(
    WidgetPermissions Permissions,
    IReadOnlyList<string> NetworkHosts,
    string? PrivacyNoteKey = null)
{
    /// <summary>No permissions and no network.</summary>
    public static WidgetCapabilities None { get; } = new(WidgetPermissions.None, []);
}
