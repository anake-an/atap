using System.Text;

namespace Atap.Core.Clipboard;

/// <summary>
/// Copied text, newest first. Up to <see cref="MaxItems"/> items of up to <see cref="MaxItemBytes"/> each. Copying
/// the same text again moves it to the top. Held in memory, saved only when the user asks the widget to keep it,
/// and never logged.
/// </summary>
public sealed class ClipboardHistory
{
    public const int MaxItems = 25;
    public const int MaxItemBytes = 10 * 1024;

    private readonly List<string> _items = [];

    public IReadOnlyList<string> Items => _items;

    /// <summary>False when the text is blank or too long, so nothing changed.</summary>
    public bool Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaxItemBytes)
            return false;
        var at = _items.IndexOf(text);
        if (at == 0)
            return false;
        if (at > 0)
            _items.RemoveAt(at);
        _items.Insert(0, text);
        if (_items.Count > MaxItems)
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        return true;
    }

    public void Remove(string text) => _items.Remove(text);

    /// <summary>Replaces the list with saved items, newest first. The same limits as <see cref="Add"/> apply.</summary>
    public void Restore(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items.Clear();
        foreach (var item in items.Reverse())
            Add(item);
    }

    public void Clear() => _items.Clear();

    /// <summary>
    /// Password managers and other apps mark copies that must not be kept. These formats say so: the copy is
    /// skipped when one is present, or when CanIncludeInClipboardHistory or CanUploadToCloudClipboard holds 0.
    /// <paramref name="readDword"/> reads a format's 4-byte value, or null when it cannot.
    /// </summary>
    public static bool ShouldSkip(IEnumerable<string> formats, Func<string, int?> readDword)
    {
        foreach (var format in formats)
        {
            if (format.Equals("ExcludeClipboardContentFromMonitorProcessing", StringComparison.OrdinalIgnoreCase)
                || format.Equals("Clipboard Viewer Ignore", StringComparison.OrdinalIgnoreCase))
                return true;
            if ((format.Equals("CanIncludeInClipboardHistory", StringComparison.OrdinalIgnoreCase)
                 || format.Equals("CanUploadToCloudClipboard", StringComparison.OrdinalIgnoreCase))
                && readDword(format) is 0)
                return true;
        }
        return false;
    }

    /// <summary>The first line, trimmed, for the list. "Line one…" when there is more.</summary>
    public static string Preview(string text, int maxLength = 80)
    {
        var trimmed = text.Trim();
        var newline = trimmed.IndexOfAny(['\r', '\n']);
        var line = newline >= 0 ? trimmed[..newline].TrimEnd() : trimmed;
        var more = newline >= 0;
        if (line.Length > maxLength)
        {
            line = line[..maxLength].TrimEnd();
            more = true;
        }
        return more ? line + "…" : line;
    }
}
