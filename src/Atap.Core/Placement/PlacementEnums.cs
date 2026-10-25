namespace Atap.Core.Placement;

/// <summary>Screen edge position the notch sticks to. There are no bottom anchors.</summary>
public enum NotchAnchor
{
    /// <summary>Top edge, a small margin in from the left.</summary>
    TopLeft,

    /// <summary>Top edge, centered. The default.</summary>
    TopCenter,

    /// <summary>Top edge, a small margin in from the right.</summary>
    TopRight,

    /// <summary>Left edge, vertically centered.</summary>
    LeftCenter,

    /// <summary>Right edge, vertically centered.</summary>
    RightCenter,
}

/// <summary>How the notch lays out its slots.</summary>
public enum NotchOrientation
{
    /// <summary>Along the top edge; compact slots sit left and right.</summary>
    Horizontal,

    /// <summary>Along a side edge; compact slots stack top to bottom.</summary>
    Vertical,
}

/// <summary>Corners of the notch rectangle.</summary>
[Flags]
public enum NotchCorners
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomRight = 4,
    BottomLeft = 8,
}

/// <summary>Which monitors show the notch.</summary>
public enum MonitorMode
{
    /// <summary>The primary monitor, following it when the primary changes. The default.</summary>
    Primary,

    /// <summary>One monitor chosen by its device id; falls back to the primary when it is not connected.</summary>
    Specific,

    /// <summary>One notch on every connected monitor.</summary>
    All,
}
