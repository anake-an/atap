using Atap.Widgets.Shelf;

namespace Atap.Notch;

/// <summary>Where files dragged over the notch open it.</summary>
public enum NotchDragTarget
{
    /// <summary>The notch stays as it is and takes no drop.</summary>
    None,

    /// <summary>The hover view, where the file shelf shows.</summary>
    Expanded,

    /// <summary>The panel, at the page that holds the file shelf.</summary>
    Panel,
}

/// <summary>
/// Hover does not fire while files are dragged, so the notch opens for the drag itself, to the place where
/// the file shelf can take the drop. Without a running shelf, or without files, nothing opens.
/// </summary>
internal static class NotchFileDrag
{
    /// <summary>The widget that takes the drop.</summary>
    public const string ShelfTypeId = FileShelfDescriptor.TypeIdValue;

    /// <param name="hasFiles">The drag carries files from Explorer.</param>
    /// <param name="shelfInExpanded">A file shelf shows in the hover view.</param>
    /// <param name="shelfPage">The panel page that shows a file shelf, from 0, or -1 when none does.</param>
    public static NotchDragTarget Target(bool hasFiles, bool shelfInExpanded, int shelfPage)
    {
        if (!hasFiles)
            return NotchDragTarget.None;
        if (shelfInExpanded)
            return NotchDragTarget.Expanded;
        return shelfPage >= 0 ? NotchDragTarget.Panel : NotchDragTarget.None;
    }
}
