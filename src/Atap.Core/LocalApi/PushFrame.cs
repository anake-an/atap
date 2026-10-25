using System.Buffers.Binary;
using System.Text;

namespace Atap.Core.LocalApi;

/// <summary>
/// Framing on the pipe: a 4-byte little-endian payload length, then that many bytes of UTF-8 JSON.
/// One request and one response per connection.
/// </summary>
public static class PushFrame
{
    public const int HeaderSize = 4;

    /// <summary>Same as <see cref="PushProtocol.MaxMessageBytes"/>.</summary>
    public const int MaxPayloadBytes = PushProtocol.MaxMessageBytes;

    /// <summary>
    /// Reads the payload length from the first <see cref="HeaderSize"/> bytes. Returns null when it is over
    /// <see cref="MaxPayloadBytes"/>, so the reader can answer <see cref="ApiErrorCode.TooLarge"/> without
    /// reading the payload.
    /// </summary>
    public static int? ReadLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize)
            throw new ArgumentException($"The header needs {HeaderSize} bytes.", nameof(header));

        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        return length > MaxPayloadBytes ? null : (int)length;
    }

    /// <summary>Builds a whole frame for <paramref name="json"/>. Throws when the payload is over the cap.</summary>
    public static byte[] Encode(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var length = Encoding.UTF8.GetByteCount(json);
        if (length > MaxPayloadBytes)
            throw new ArgumentException($"The payload is over {MaxPayloadBytes} bytes.", nameof(json));

        var frame = new byte[HeaderSize + length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)length);
        Encoding.UTF8.GetBytes(json, frame.AsSpan(HeaderSize));
        return frame;
    }
}
