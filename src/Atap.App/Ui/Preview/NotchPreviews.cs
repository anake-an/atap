using System.Windows;
using System.Windows.Media;
using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Layout.Editing;
using Atap.Core.Localization;
using Atap.Core.Widgets;

namespace Atap.Ui.Preview;

/// <summary>
/// One widget drawn the way it looks on the notch, with sample content. Drawn by the implicit template in
/// PreviewViews.xaml. Static: nothing in it ticks or animates.
/// </summary>
public sealed record PreviewTile
{
    public required string InstanceId { get; init; }

    public required string TypeId { get; init; }

    /// <summary>The widget's name, such as "Clock".</summary>
    public required string Name { get; init; }

    public WidgetGroup Group { get; init; }

    public WidgetSize Size { get; init; }

    public Geometry? Icon { get; init; }

    /// <summary>The main sample line. Empty on a plain card.</summary>
    public string Value { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    /// <summary>A bar from 0 to 1, or NaN for none.</summary>
    public double Progress { get; init; } = double.NaN;

    /// <summary>False for a widget with no sample: it shows a plain card with its icon and name.</summary>
    public bool IsSample { get; init; }

    /// <summary>The tile's place in its state or page, or -1 in the gallery.</summary>
    public int Index { get; init; } = -1;

    public bool IsSelected { get; init; }

    /// <summary>Where a dragged widget will land. Drawn with a dashed outline.</summary>
    public bool IsMarker { get; init; }

    /// <summary>What a screen reader says, such as "Clock, Small".</summary>
    public string AutomationName { get; init; } = string.Empty;

    public double Width => PreviewMetrics.Notch.WidthOf(Size);

    public double Height => PreviewMetrics.Notch.RowHeight;

    public bool HasDetail => IsSample && Detail.Length > 0;

    public bool HasProgress => IsSample && Size != WidgetSize.Small && double.IsFinite(Progress);

    /// <summary>The filled part of the bar, as a star width beside <see cref="ProgressLeft"/>.</summary>
    public GridLength ProgressDone => new(HasProgress ? Math.Clamp(Progress, 0, 1) : 0, GridUnitType.Star);

    public GridLength ProgressLeft => new(HasProgress ? 1 - Math.Clamp(Progress, 0, 1) : 1, GridUnitType.Star);

    public bool IsPlain => !IsSample;
}

/// <summary>One notch state as a picture: its tiles laid out like the notch lays them out.</summary>
public sealed record NotchPicture(NotchState State, string Label, string Hint, IReadOnlyList<PreviewTile> Tiles, string EmptyText)
{
    public PreviewShape Shape => PreviewLayout.ShapeOf(State);

    public bool IsGrid => Shape == PreviewShape.Grid;

    public bool IsEmpty => Tiles.Count == 0;

    /// <summary>"Page 1 of 3" under a panel with more pages, or empty.</summary>
    public string Note { get; init; } = string.Empty;

    public bool HasNote => Note.Length > 0;

    public string AutomationName => IsEmpty ? $"{Label}. {EmptyText}" : $"{Label}. {string.Join(", ", Tiles.Select(t => t.AutomationName))}";
}

/// <summary>Builds preview tiles and pictures from the catalog and the sample content.</summary>
public static class NotchPreviews
{
    /// <summary>The plain name of a state: "Small notch", "When you point at it", "When you click it".</summary>
    public static string LabelKey(NotchState state) => $"preview.state.{state.ToString().ToLowerInvariant()}";

    /// <summary>One line on what the state is.</summary>
    public static string HintKey(NotchState state) => $"preview.hint.{state.ToString().ToLowerInvariant()}";

    /// <summary>
    /// A tile for <paramref name="instanceId"/> at <paramref name="size"/>. A type with no sample content, or not in
    /// the catalog, gets a plain card with an icon and its name.
    /// </summary>
    public static PreviewTile Tile(WidgetCatalog catalog, string instanceId, WidgetSize size, ILocalizer text, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(text);
        catalog.TryResolve(instanceId, out var descriptor);
        var typeId = descriptor?.TypeId ?? (WidgetInstanceId.TryParse(instanceId, out var parsed) ? parsed.TypeId : instanceId);
        var name = descriptor is null ? instanceId : text[descriptor.NameKey];
        var group = descriptor?.Group ?? WidgetGroup.Custom;
        var sizeName = text[$"layouteditor.sizeName.{size.ToString().ToLowerInvariant()}"];
        var tile = new PreviewTile
        {
            InstanceId = instanceId,
            TypeId = typeId,
            Name = name,
            Group = group,
            Size = size,
            Index = index,
            AutomationName = text.Format("preview.tile", ("name", name), ("size", sizeName)),
        };

        if (descriptor is null || !WidgetSamples.TryGet(typeId, out var sample))
            return tile with { Icon = FindIcon(PlainIconOf(group)) };
        return tile with
        {
            IsSample = true,
            Icon = FindIcon(sample.IconKey),
            Value = text[WidgetSample.ValueKey(typeId)],
            Detail = text[WidgetSample.DetailKey(typeId)],
            Progress = sample.Progress ?? double.NaN,
        };
    }

    /// <summary>The plain line about a widget for the gallery, or its description when it has no sample.</summary>
    public static string LineOf(IWidgetDescriptor descriptor, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(text);
        return WidgetSamples.TryGet(descriptor.TypeId, out _) ? text[WidgetSample.LineKey(descriptor.TypeId)] : text[descriptor.DescriptionKey];
    }

    /// <summary>A picture of one state from its placements, in order.</summary>
    public static NotchPicture Picture(WidgetCatalog catalog, NotchState state, IEnumerable<WidgetPlacement> placements, ILocalizer text) =>
        new(
            state,
            text[LabelKey(state)],
            text[HintKey(state)],
            placements.OrderBy(p => p.Order).Select((p, i) => Tile(catalog, p.WidgetId, p.Size, text, i)).ToList(),
            text["preview.empty"]);

    /// <summary>The icon of a group, for a widget with no sample content.</summary>
    public static string PlainIconOf(WidgetGroup group) => group switch
    {
        WidgetGroup.Media => "icon.music",
        WidgetGroup.Productivity => "icon.list",
        WidgetGroup.System => "icon.settings",
        WidgetGroup.Malaysia => "icon.moon",
        _ => "icon.grid",
    };

    // Icons live in the app's theme. Off the UI thread, or in tests without an app, there is no icon, which the
    // template handles.
    private static Geometry? FindIcon(string key) =>
        Application.Current is { } app && app.Dispatcher.CheckAccess() ? app.TryFindResource(key) as Geometry : null;
}
