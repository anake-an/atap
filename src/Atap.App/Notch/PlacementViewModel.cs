using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Widgets;

namespace Atap.Notch;

/// <summary>A widget view model placed in the current notch state, with enough context to pick its view.</summary>
/// <param name="InstanceId">The instance id, for example "json-url#nas".</param>
/// <param name="TypeId">The widget type id, which names its templates, for example "json-url".</param>
/// <param name="ViewModel">The widget's view model.</param>
/// <param name="State">The state it is shown in.</param>
/// <param name="Size">Its size in that state.</param>
/// <param name="Slot">Where it sits.</param>
public sealed record PlacementViewModel(
    string InstanceId,
    string TypeId,
    object ViewModel,
    NotchState State,
    WidgetSize Size,
    PlacementSlot Slot)
{
    /// <summary>The widget type's name key, which a screen reader says when the widget has no summary.</summary>
    public string? NameKey { get; init; }

    /// <summary>
    /// Resource keys to try, most specific first, for example
    /// "media.Compact.Left", "media.Compact.Small", "media.Compact", "media".
    /// </summary>
    public IEnumerable<string> TemplateKeys()
    {
        yield return $"{TypeId}.{State}.{Slot}";
        yield return $"{TypeId}.{State}.{Size}";
        yield return $"{TypeId}.{State}";
        yield return TypeId;
    }
}
