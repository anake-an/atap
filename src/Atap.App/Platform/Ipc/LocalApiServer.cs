using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Atap.Core.LocalApi;

namespace Atap.Platform.Ipc;

/// <summary>Answers one request. Runs on the UI thread. <paramref name="source"/> is the calling program's name, shown only.</summary>
internal delegate Task<ApiResponse> ApiHandler(ApiRequest request, string? source);

/// <summary>
/// Atap's local API: a named pipe only the signed-in user can open, never the network or a sandboxed app.
/// One request per connection, a 4-byte length and at most 8 KB of JSON, a 2 second deadline and 20 requests
/// a second in all. Waiting uses overlapped I/O, so no thread blocks and nothing polls. Requests reach the UI
/// thread in the order they arrive.
/// </summary>
internal sealed class LocalApiServer : IAsyncDisposable
{
    public const int MaxInstances = 4;
    public static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(2);

    // How long a listener waits before it tries again to open a pipe instance.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly string _name;
    private readonly TimeSpan _readDeadline;
    private readonly SynchronizationContext _ui;
    private readonly ApiHandler _handler;
    private readonly RateLimiter _limiter;
    private readonly Action<string, Exception?> _log;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _loops = [];

    /// <param name="readDeadline">How long a client may take to send its request. <see cref="ReadDeadline"/> unless a test needs more on a busy machine.</param>
    public LocalApiServer(string name, SynchronizationContext ui, ApiHandler handler, RateLimiter limiter, Action<string, Exception?> log, TimeSpan? readDeadline = null)
    {
        _readDeadline = readDeadline ?? ReadDeadline;
        _name = name;
        _ui = ui;
        _handler = handler;
        _limiter = limiter;
        _log = log;
    }

    /// <summary>"Atap.v1.&lt;user SID&gt;", so each user has their own pipe. The CLI builds the same name.</summary>
    public static string NameForCurrentUser => $"Atap.v1.{WindowsIdentity.GetCurrent().User?.Value}";

    /// <summary>
    /// Starts listening. False when another program already holds the name: the API stays off, with no retry loop,
    /// so a squatter can never receive what Atap would answer.
    /// </summary>
    public bool Start()
    {
        NamedPipeServerStream first;
        try
        {
            first = Create(firstInstance: true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _log("Local API could not start: its pipe name is taken.", ex);
            return false;
        }

        _loops.Add(Task.Run(() => ListenAsync(first)));
        for (var i = 1; i < MaxInstances; i++)
            _loops.Add(Task.Run(() => ListenAsync(null)));
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            await Task.WhenAll(_loops);
        }
        catch (Exception ex)
        {
            _log("Local API stopped with an error.", ex);
        }
        _stop.Dispose();
    }

    private NamedPipeServerStream Create(bool firstInstance)
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("No user SID.");
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        var options = PipeOptions.Asynchronous | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return NamedPipeServerStreamAcl.Create(_name, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte, options, 0, 0, security);
    }

    private async Task ListenAsync(NamedPipeServerStream? pipe)
    {
        while (!_stop.IsCancellationRequested)
        {
            pipe ??= await TryCreateAsync();
            if (pipe is null)
                continue;
            try
            {
                await pipe.WaitForConnectionAsync(_stop.Token);
                await AnswerAsync(pipe);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad connection never stops the API. The next one gets a fresh instance.
                _log("Local API request failed.", ex);
            }
            finally
            {
                pipe?.Dispose();
                pipe = null;
            }
        }
        pipe?.Dispose();

        // A new instance can fail for a moment, such as while Windows still frees the last one. Before, that
        // stopped this listener for good, so the API answered more slowly until Atap restarted.
        async Task<NamedPipeServerStream?> TryCreateAsync()
        {
            try
            {
                return Create(firstInstance: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log("Local API could not open a pipe instance. Trying again.", ex);
                try
                {
                    await Task.Delay(RetryDelay, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                }
                return null;
            }
        }
    }

    private async Task AnswerAsync(NamedPipeServerStream pipe)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        deadline.CancelAfter(_readDeadline);

        ApiResponse response;
        var header = new byte[PushFrame.HeaderSize];
        await pipe.ReadExactlyAsync(header, deadline.Token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length > PushFrame.MaxPayloadBytes)
        {
            response = ApiResponse.Failure(ApiErrorCode.TooLarge);
        }
        else if (length <= 0)
        {
            response = ApiResponse.Failure(ApiErrorCode.Invalid);
        }
        else
        {
            var body = new byte[length];
            await pipe.ReadExactlyAsync(body, deadline.Token);
            var parsed = PushProtocol.Parse(body);
            if (!_limiter.TryTake())
                response = ApiResponse.Failure(ApiErrorCode.RateLimited);
            else if (parsed.Request is { } request)
                response = await OnUiAsync(request, SourceOf(pipe));
            else
                response = ApiResponse.Failure(parsed.Error!);
        }

        await pipe.WriteAsync(PushFrame.Encode(PushProtocol.FormatResponse(response)), deadline.Token);
        await pipe.FlushAsync(deadline.Token);
    }

    private Task<ApiResponse> OnUiAsync(ApiRequest request, string? source)
    {
        var answer = new TaskCompletionSource<ApiResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(async _ =>
        {
            try
            {
                answer.SetResult(await _handler(request, source));
            }
            catch (Exception ex)
            {
                _log("Local API handler failed.", ex);
                answer.SetResult(ApiResponse.Failure(ApiErrorCode.Invalid));
            }
        }, null);
        return answer.Task;
    }

    /// <summary>The calling program's name, such as "powershell", for the "from" label. Null when Windows cannot say.</summary>
    private static string? SourceOf(NamedPipeServerStream pipe)
    {
        try
        {
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var id))
                return null;
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);
}

/// <summary>A token bucket: up to <paramref name="perSecond"/> requests a second across every connection. Thread safe.</summary>
internal sealed class RateLimiter(TimeProvider time, int perSecond = 20)
{
    private readonly object _gate = new();
    private double _tokens = perSecond;
    private long _stamp = time.GetTimestamp();

    public bool TryTake()
    {
        lock (_gate)
        {
            var now = time.GetTimestamp();
            _tokens = Math.Min(perSecond, _tokens + time.GetElapsedTime(_stamp, now).TotalSeconds * perSecond);
            _stamp = now;
            if (_tokens < 1)
                return false;
            _tokens--;
            return true;
        }
    }
}
