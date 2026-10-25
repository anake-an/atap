namespace Atap.Core.Notch;

/// <summary>What a notch window shows. Each has a template "surface.&lt;Name&gt;" in Notch/Surfaces.</summary>
public enum NotchSurface
{
    Hidden,

    /// <summary>The layout editor.</summary>
    Edit,
    Panel,

    /// <summary>The hover or peek view.</summary>
    Expanded,

    /// <summary>A transient alert.</summary>
    Alert,

    /// <summary>A thin line in place of Compact and Idle.</summary>
    Slim,
    Compact,
    Idle,
}

/// <param name="Hidden">A rule says hide, or a full-screen app is in front.</param>
/// <param name="Editing">The layout editor is open.</param>
/// <param name="PanelOpen">The panel is open.</param>
/// <param name="Hovered">The mouse is over this notch.</param>
/// <param name="Peeking">The peek hotkey is held.</param>
/// <param name="HasExpandedContent">The expanded view has something to show.</param>
/// <param name="HasAlert">An alert is on screen.</param>
/// <param name="Slim">Slim mode is on.</param>
/// <param name="HasCompactOwner">A live widget claims compact.</param>
/// <param name="HasCompactContent">The compact owner has a placement in the compact state.</param>
public sealed record NotchInputs(
    bool Hidden,
    bool Editing,
    bool PanelOpen,
    bool Hovered,
    bool Peeking,
    bool HasExpandedContent,
    bool HasAlert,
    bool Slim,
    bool HasCompactOwner,
    bool HasCompactContent);
