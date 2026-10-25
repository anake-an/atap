using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Atap.Settings.Form;

/// <summary>The Windows file and folder dialogs, owned by the window that hosts the form.</summary>
internal sealed class DialogPathPicker(Func<Window?> owner) : IPathPicker
{
    public string? PickFolder(string? start)
    {
        var dialog = new OpenFolderDialog { Multiselect = false };
        if (start is not null && Directory.Exists(start))
            dialog.InitialDirectory = start;
        return Show(dialog) ? dialog.FolderName : null;
    }

    public string? PickFile(string? start)
    {
        var dialog = new OpenFileDialog { Multiselect = false, CheckFileExists = true };
        var folder = start is null ? null : Path.GetDirectoryName(start);
        if (folder is not null && Directory.Exists(folder))
            dialog.InitialDirectory = folder;
        return Show(dialog) ? dialog.FileName : null;
    }

    private bool Show(CommonDialog dialog) => (owner() is { } window ? dialog.ShowDialog(window) : dialog.ShowDialog()) == true;
}
