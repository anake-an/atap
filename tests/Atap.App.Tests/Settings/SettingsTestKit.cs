using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Atap.Composition;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Settings.Form;

namespace Atap.Tests.Settings;

/// <summary>Secrets in memory. Put returns a new reference each time, as the real store does.</summary>
internal sealed class FakeSecretStore(bool available = true) : ISecretStore
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public List<string> Deleted { get; } = [];

    public bool IsAvailable { get; set; } = available;

    public string Put(string value)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("Not available.");
        var reference = $"{SettingValues.SecretPrefix}{Guid.NewGuid():N}";
        Values[reference] = value;
        return reference;
    }

    public string? Get(string reference) => Values.GetValueOrDefault(reference);

    public void Delete(string reference)
    {
        Deleted.Add(reference);
        Values.Remove(reference);
    }
}

/// <summary>Widget instances with set statuses, for the widgets card.</summary>
internal sealed class FakeHostView(WidgetCatalog catalog) : IWidgetHostView
{
    public Dictionary<string, WidgetStatus> Statuses { get; } = new(StringComparer.Ordinal);

    public List<string> Ids { get; } = [];

    public List<string> Restarted { get; } = [];

    public WidgetCatalog Catalog { get; } = catalog;

    public IReadOnlyList<IWidget> Widgets => [];

    public IReadOnlyList<string> InstanceIds => Ids.ToList();

    public event EventHandler? WidgetsChanged;

    public event EventHandler<string>? StatusChanged;

    public WidgetStatus StatusOf(string instanceId) => Statuses.GetValueOrDefault(instanceId, WidgetStatus.Stopped);

    public string? StatusMessageKeyOf(string instanceId) => null;

    public WidgetVisibility VisibilityOf(string instanceId) => WidgetVisibility.Hidden;

    public Task RestartAsync(string instanceId)
    {
        Restarted.Add(instanceId);
        return Task.CompletedTask;
    }

    public void SetStatus(string id, WidgetStatus status)
    {
        Statuses[id] = status;
        StatusChanged?.Invoke(this, id);
    }

    public void RaiseWidgetsChanged() => WidgetsChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>A widget type with chosen settings and capabilities, for the cards.</summary>
internal sealed class SettingsTestDescriptor(string typeId, IReadOnlyList<SettingDefinition>? settings = null, WidgetCapabilities? capabilities = null) : IWidgetDescriptor
{
    public string TypeId { get; } = typeId;

    public string NameKey => "clock.name";

    public string DescriptionKey => "clock.description";

    public WidgetGroup Group => WidgetGroup.Custom;

    public IReadOnlyList<WidgetSize> SupportedSizes => [WidgetSize.Small];

    public IReadOnlyList<Atap.Core.NotchState> SupportedStates => [Atap.Core.NotchState.Idle];

    public IReadOnlyList<SettingDefinition> Settings { get; } = settings ?? [];

    public WidgetCapabilities Capabilities { get; } = capabilities ?? WidgetCapabilities.None;

    public bool AllowsMultipleInstances => true;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource => new("/Tests;component/none.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => throw new NotSupportedException();
}

internal static class SettingsUi
{
    /// <summary>Shows <paramref name="content"/> in a measured host with the form's templates, like the settings window does.</summary>
    public static ContentControl Render(object content, params Uri[] views)
    {
        var host = new ContentControl();
        host.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(SettingsForm.ViewsSource));
        foreach (var source in views)
            host.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(source));
        host.Content = content;
        Layout(host);
        return host;
    }

    public static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(900, 4000));
        element.Arrange(new Rect(0, 0, 900, 4000));
        element.UpdateLayout();
    }

    public static IEnumerable<T> Find<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var nested in Find<T>(child))
                yield return nested;
        }
    }

    /// <summary>Visible controls only: collapsed parts of a template do not count.</summary>
    public static IEnumerable<T> Visible<T>(DependencyObject root)
        where T : UIElement => Find<T>(root).Where(e => e.IsVisible || IsShown(e));

    private static bool IsShown(UIElement element)
    {
        // IsVisible needs a window. Without one, walk up and look for a collapsed parent.
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement { Visibility: not Visibility.Visible })
                return false;
        }
        return true;
    }
}
