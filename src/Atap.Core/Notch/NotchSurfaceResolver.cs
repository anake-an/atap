namespace Atap.Core.Notch;

/// <summary>Picks the surface a notch window shows.</summary>
public static class NotchSurfaceResolver
{
    /// <summary>
    /// Returns the first that applies: Hidden, Edit, Panel, Expanded (hover or peek, with content),
    /// Alert, Slim, Compact (a live owner with content), then Idle.
    /// </summary>
    public static NotchSurface Resolve(NotchInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.Hidden)
            return NotchSurface.Hidden;
        if (inputs.Editing)
            return NotchSurface.Edit;
        if (inputs.PanelOpen)
            return NotchSurface.Panel;
        if ((inputs.Hovered || inputs.Peeking) && inputs.HasExpandedContent)
            return NotchSurface.Expanded;
        if (inputs.HasAlert)
            return NotchSurface.Alert;
        if (inputs.Slim)
            return NotchSurface.Slim;
        if (inputs.HasCompactOwner && inputs.HasCompactContent)
            return NotchSurface.Compact;
        return NotchSurface.Idle;
    }

    /// <summary>The placement state whose widgets a surface shows. Null for surfaces that show no placements.</summary>
    public static NotchState? StateOf(NotchSurface surface) => surface switch
    {
        NotchSurface.Idle => NotchState.Idle,
        NotchSurface.Compact => NotchState.Compact,
        NotchSurface.Expanded => NotchState.Expanded,
        NotchSurface.Panel => NotchState.Panel,
        _ => null,
    };
}
