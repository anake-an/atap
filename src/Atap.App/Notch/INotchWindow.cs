using Atap.Core.Placement;

namespace Atap.Notch;

/// <summary>What <see cref="NotchHost"/> needs of a notch window. Tests place fakes, so no window opens.</summary>
internal interface INotchWindow
{
    /// <summary>The monitor this notch is on. Null until <see cref="Place"/> is called.</summary>
    DisplayMonitor? TargetMonitor { get; }

    void Place(DisplayMonitor monitor, NotchAnchor anchor, int offset);

    /// <summary>Shows or hides the window to match its surface.</summary>
    void UpdateVisibility();

    void Close();
}
