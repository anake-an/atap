using Atap.Core.Widgets;

namespace Atap.Composition;

/// <summary>
/// What features and settings cards may see of the running widgets: the catalog, the instances, their
/// status and a restart. Only the app applies layouts, starts the host and disposes it. Use it on the UI thread.
/// </summary>
internal interface IWidgetHostView
{
    /// <summary>Every known widget type, enabled or not, for the widget list, the layout gallery and presets.</summary>
    WidgetCatalog Catalog { get; }

    /// <summary>Instances that exist right now (created, started or starting), in layout order.</summary>
    IReadOnlyList<IWidget> Widgets { get; }

    /// <summary>Every enabled instance id the host knows, including Failed ones.</summary>
    IReadOnlyList<string> InstanceIds { get; }

    /// <summary>Stopped for an id the host does not know.</summary>
    WidgetStatus StatusOf(string instanceId);

    /// <summary>The string key the instance gave with its status, if any.</summary>
    string? StatusMessageKeyOf(string instanceId);

    WidgetVisibility VisibilityOf(string instanceId);

    /// <summary>Stops an instance and starts a fresh one with the same id, for example after Failed.</summary>
    Task RestartAsync(string instanceId);

    /// <summary>Raised when <see cref="Widgets"/> changes.</summary>
    event EventHandler? WidgetsChanged;

    /// <summary>Raised with the instance id when its status changes.</summary>
    event EventHandler<string>? StatusChanged;
}
