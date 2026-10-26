using System.Net;
using System.Text;
using System.Threading.Channels;

namespace Atap.Core.Tests.Ai;

/// <summary>A fake service: keeps every request with its body, and answers with what the test sets.</summary>
internal sealed class FakeAiServer
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly List<SeenRequest> _requests = [];

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Respond { get; set; }

    public IReadOnlyList<SeenRequest> Requests
    {
        get
        {
            lock (_requests)
                return _requests.ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_requests)
                return _requests.Count;
        }
    }

    public HttpMessageHandler Handler() => new ServerHandler(this);

    public async Task WaitForAsync(int count)
    {
        var until = DateTime.UtcNow + Wait;
        while (Count < count)
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"The server got {Count} requests, not {count}.");
            await Task.Delay(2);
        }
    }

    /// <summary>A 200 with this event stream as the body, all at once.</summary>
    public static HttpResponseMessage Events(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
    };

    /// <summary>A 200 whose body the test writes bit by bit.</summary>
    public static HttpResponseMessage Streaming(ScriptedStream stream) => new(HttpStatusCode.OK) { Content = new StreamContent(stream) };

    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_requests)
            _requests.Add(new SeenRequest(request, body));
        return await (Respond?.Invoke(request, cancellationToken) ?? Task.FromResult(Events("data: [DONE]\n\n")));
    }

    private sealed class ServerHandler(FakeAiServer server) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            server.HandleAsync(request, cancellationToken);
    }
}

internal sealed record SeenRequest(HttpRequestMessage Message, string Body)
{
    public string? Header(string name) =>
        Message.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}

/// <summary>A response body the test writes to while the transport reads it. Reads wait, with the token, until there is more.</summary>
internal sealed class ScriptedStream : Stream
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
    private byte[] _current = [];
    private int _offset;
    private int _waits;

    /// <summary>How many times a read found nothing and started to wait.</summary>
    public int Waits => Volatile.Read(ref _waits);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void Send(string text) => _chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

    public void Send(byte[] bytes) => _chunks.Writer.TryWrite(bytes);

    public void End() => _chunks.Writer.TryComplete();

    /// <summary>Sends <paramref name="text"/> and waits until the reader has taken it and waits again.</summary>
    public async Task SendAndWaitAsync(string text)
    {
        var before = Waits;
        Send(text);
        await WaitForWaitsAsync(before + 1);
    }

    public async Task WaitForWaitsAsync(int count)
    {
        var until = DateTime.UtcNow + Wait;
        while (Waits < count)
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"The reader waited {Waits} times, not {count}.");
            await Task.Delay(2);
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_offset >= _current.Length)
        {
            if (!_chunks.Reader.TryRead(out var next))
            {
                Interlocked.Increment(ref _waits);
                if (!await _chunks.Reader.WaitToReadAsync(cancellationToken))
                    return 0;
                if (!_chunks.Reader.TryRead(out next))
                    return 0;
            }
            _current = next;
            _offset = 0;
        }
        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
