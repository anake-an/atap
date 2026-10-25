using System.Text.Json;
using System.Text.Json.Serialization;
using Atap.Core.Widgets;

namespace Atap.Core.Layout;

/// <summary>Where a placement sits inside the notch.</summary>
public enum PlacementSlot
{
    Center,

    /// <summary>Left of the camera gap; used by the compact state.</summary>
    Left,

    /// <summary>Right of the camera gap; used by the compact state.</summary>
    Right,
}

/// <summary>One widget instance placed in one notch state.</summary>
public sealed record WidgetPlacement
{
    /// <summary>The instance id, for example "media" or "json-url#nas". The JSON name stays "widgetId".</summary>
    public string WidgetId { get; init; } = string.Empty;

    public WidgetSize Size { get; init; } = WidgetSize.Small;

    /// <summary>Sort order within the slot, lowest first.</summary>
    public int Order { get; init; }

    public PlacementSlot Slot { get; init; } = PlacementSlot.Center;

    /// <summary>Fields this version does not know. Null unless the file had some.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
