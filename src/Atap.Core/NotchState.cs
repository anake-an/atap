namespace Atap.Core;

/// <summary>The states a widget can be placed in. The notch shows them through surfaces (see Notch/NotchSurface).</summary>
public enum NotchState
{
    /// <summary>Nothing live; shows the idle widgets (the clock by default).</summary>
    Idle,

    /// <summary>A live widget owns the notch, split into left and right slots.</summary>
    Compact,

    /// <summary>The hover view.</summary>
    Expanded,

    /// <summary>A larger panel with pages.</summary>
    Panel,
}
