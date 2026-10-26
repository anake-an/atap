using System.Text;

namespace Atap.Core.Ai;

/// <summary>One server-sent event: its name (empty when the server sent none) and its data lines joined with a line break.</summary>
public readonly record struct SseMessage(string Event, string Data);

/// <summary>
/// Reads server-sent events from a stream. Lines are split on the byte level, so a line or a letter split
/// across two reads is put back together, and a line longer than the cap stops the read with
/// <see cref="InvalidDataException"/> instead of growing without end.
/// </summary>
internal sealed class SseReader
{
    private readonly Stream _stream;
    private readonly int _maxLineBytes;
    private readonly Action _onLine;
    private readonly byte[] _buffer = new byte[8192];
    private readonly MemoryStream _line = new();
    private int _start;
    private int _end;
    private bool _ended;

    /// <param name="stream">The response body.</param>
    /// <param name="maxLineBytes">The longest line accepted.</param>
    /// <param name="onLine">Called for every line read, comments and pings too, so a stall timer can start again.</param>
    public SseReader(Stream stream, int maxLineBytes, Action onLine)
    {
        _stream = stream;
        _maxLineBytes = maxLineBytes;
        _onLine = onLine;
    }

    /// <summary>Every byte read from the stream so far.</summary>
    public long BytesRead { get; private set; }

    /// <summary>The next event, or null at the end of the stream.</summary>
    public async ValueTask<SseMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        var name = string.Empty;
        StringBuilder? data = null;
        while (true)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return data is null ? null : new SseMessage(name, data.ToString());
            if (line.Length == 0)
            {
                if (data is not null)
                    return new SseMessage(name, data.ToString());
                name = string.Empty;
                continue;
            }
            if (line[0] == ':')
                continue;

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..];
            if (value.StartsWith(' '))
                value = value[1..];
            if (field == "event")
            {
                name = value;
            }
            else if (field == "data")
            {
                if (data is null)
                    data = new StringBuilder(value);
                else
                    data.Append('\n').Append(value);
            }
        }
    }

    /// <summary>One line without its line break, or null at the end of the stream.</summary>
    internal async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var newline = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
            if (newline >= 0)
            {
                Append(_start, newline - _start);
                _start = newline + 1;
                return TakeLine();
            }
            Append(_start, _end - _start);
            _start = _end = 0;
            if (_ended)
                return _line.Length > 0 ? TakeLine() : null;

            var read = await _stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                _ended = true;
                continue;
            }
            BytesRead += read;
            _end = read;
        }
    }

    private void Append(int offset, int count)
    {
        if (count <= 0)
            return;
        if (_line.Length + count > _maxLineBytes)
            throw new InvalidDataException("A line from the server is too long.");
        _line.Write(_buffer, offset, count);
    }

    private string TakeLine()
    {
        var bytes = _line.GetBuffer().AsSpan(0, (int)_line.Length);
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
            bytes = bytes[..^1];
        var text = Encoding.UTF8.GetString(bytes);
        _line.SetLength(0);
        _onLine();
        return text;
    }
}
