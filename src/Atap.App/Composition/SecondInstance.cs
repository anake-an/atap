using System.IO;

namespace Atap.Composition;

/// <summary>
/// A second launch of Atap signals the first one through the event <c>Local\Atap.ShowSettings</c>
/// and exits. The first instance then runs the command "settings.open". The event can do nothing else.
/// </summary>
internal sealed class SecondInstance : IDisposable
{
    public const string EventName = @"Local\Atap.ShowSettings";
    public const string CommandId = "settings.open";

    private readonly EventWaitHandle _event;
    private RegisteredWaitHandle? _wait;

    private SecondInstance(EventWaitHandle handle) => _event = handle;

    /// <summary>
    /// Called by a second launch. Returns false when the first instance has not created the event yet, or
    /// when another account in this session made it first and shut this user out.
    /// </summary>
    public static bool Signal()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(EventName, out var handle))
                return false;
            using (handle)
                return handle.Set();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the event and waits on a thread-pool wait (no thread is blocked). <paramref name="onSignal"/>
    /// runs on <paramref name="ui"/>.
    /// </summary>
    public static SecondInstance Listen(SynchronizationContext ui, Action onSignal)
    {
        var handle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var listener = new SecondInstance(handle);
        listener._wait = ThreadPool.RegisterWaitForSingleObject(
            handle,
            (_, _) => ui.Post(_ => onSignal(), null),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        return listener;
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _wait = null;
        _event.Dispose();
    }
}
