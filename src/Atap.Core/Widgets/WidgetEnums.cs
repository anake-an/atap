namespace Atap.Core.Widgets;

/// <summary>Groups widgets in the gallery. Each group has its own accent colour.</summary>
public enum WidgetGroup
{
    Media,
    Productivity,
    System,
    Custom,
    Malaysia,
}

/// <summary>Size a widget is rendered at within a notch state.</summary>
public enum WidgetSize
{
    Small,
    Medium,
    Large,
}

/// <summary>Shown in Settings and through the widget.status template.</summary>
public enum WidgetStatus
{
    Running,

    /// <summary>A required setting or secret is missing.</summary>
    NeedsSetup,

    /// <summary>Waits for the user to approve something on this PC.</summary>
    NeedsApproval,
    Offline,

    /// <summary>Stopped after errors. Settings offers Restart.</summary>
    Failed,

    /// <summary>Created but not started yet, or stopped.</summary>
    Stopped,
}

/// <summary>
/// Where an instance is shown right now, merged over every notch window.
/// Higher values win, so the order matters.
/// </summary>
public enum WidgetVisibility
{
    Hidden,
    Idle,
    Compact,
    Expanded,
    Panel,
}
