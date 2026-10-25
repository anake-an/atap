using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Atap.Core.Widgets;

namespace Atap.Core.Storage;

/// <summary>
/// JSON files for one widget instance, in data\&lt;instanceId&gt;\ (User) or state\&lt;instanceId&gt;\ (Machine).
/// Folders are created on the first save only, and only plain file names are accepted.
/// </summary>
/// <remarks>
/// A file that does not parse is moved aside as &lt;name&gt;.corrupt-&lt;stamp&gt;.json before the fallback is
/// returned, so the next save cannot overwrite what the user had. A file that is locked or cannot be read is
/// left alone.
/// </remarks>
public sealed class WidgetStorage : IWidgetStorage
{
    private readonly string _userFolder;
    private readonly string _machineFolder;
    private readonly TimeProvider _time;
    // Files whose last load failed for a reason other than "not there", so Save must not replace them.
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="instanceId">The widget instance, for example "todo" or "json-url#nas".</param>
    /// <param name="paths">Where Atap keeps its files.</param>
    /// <param name="time">Clock for the names of corrupt-file backups.</param>
    public WidgetStorage(string instanceId, AtapPaths paths, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!WidgetInstanceId.TryParse(instanceId, out _))
            throw new ArgumentException($"'{instanceId}' is not a valid instance id.", nameof(instanceId));
        _userFolder = Path.Combine(paths.UserData, instanceId);
        _machineFolder = Path.Combine(paths.MachineState, instanceId);
        _time = time ?? TimeProvider.System;
    }

    public T Load<T>(string name, JsonTypeInfo<T> type, T fallback, DataScope scope = DataScope.User)
    {
        ArgumentNullException.ThrowIfNull(type);
        var path = PathFor(name, scope);
        byte[] bytes;
        try
        {
            using var stream = AtomicFile.OpenRead(path);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            SetUnreadable(path, false);
            return fallback;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked by another app. The file is left as it is, and Save refuses to replace it with the
            // fallback until a load succeeds.
            SetUnreadable(path, true);
            return fallback;
        }

        try
        {
            var value = JsonSerializer.Deserialize(bytes, type);
            SetUnreadable(path, false);
            return value ?? fallback;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            SetUnreadable(path, !KeepAside(path, bytes));
            return fallback;
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/> atomically. Throws <see cref="IOException"/> while the file could not be
    /// read, so data a widget never saw is never replaced.
    /// </summary>
    public void Save<T>(string name, T value, JsonTypeInfo<T> type, DataScope scope = DataScope.User)
    {
        ArgumentNullException.ThrowIfNull(type);
        var path = PathFor(name, scope);
        lock (_unreadable)
        {
            if (_unreadable.Contains(path))
                throw new IOException("This file could not be read, so it is not replaced until it can be.");
        }
        AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, value, type));
    }

    /// <summary>Deletes one file. Nothing happens when it or its folder is not there.</summary>
    public void Delete(string name, DataScope scope = DataScope.User)
    {
        var path = PathFor(name, scope);
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was ever saved.
        }
        SetUnreadable(path, false);
    }

    /// <summary>Deletes both folders of the instance. Both are tried before the first error is thrown.</summary>
    public void DeleteAll()
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var folder in new[] { _userFolder, _machineFolder })
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure ??= ExceptionDispatchInfo.Capture(ex);
            }
        }
        failure?.Throw();
    }

    /// <summary>
    /// Keeps a file that does not parse as name.corrupt-&lt;time&gt;.json: moved there, or failing that, the bytes
    /// already read are written there. False when neither worked, so the original must not be replaced.
    /// </summary>
    private bool KeepAside(string path, byte[] bytes)
    {
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var aside = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}.corrupt-{stamp}.json");
        try
        {
            File.Move(path, aside, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        try
        {
            AtomicFile.Write(aside, stream => stream.Write(bytes));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void SetUnreadable(string path, bool unreadable)
    {
        lock (_unreadable)
        {
            if (unreadable)
                _unreadable.Add(path);
            else
                _unreadable.Remove(path);
        }
    }

    private string PathFor(string name, DataScope scope)
    {
        // A plain file name only: no folders, no "..".
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"'{name}' is not a valid file name.", nameof(name));
        return Path.Combine(scope == DataScope.Machine ? _machineFolder : _userFolder, name + ".json");
    }
}
