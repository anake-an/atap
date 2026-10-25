using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace Atap.Cli;

/// <param name="ExitCode">One of <see cref="ExitCodes"/>.</param>
/// <param name="Message">What to print.</param>
internal readonly record struct CliResult(int ExitCode, string Message);

internal interface IPushClient
{
    Task<CliResult> PingAsync();

    /// <summary>Sends one request. <paramref name="done"/> is printed when Atap says it is fine.</summary>
    Task<CliResult> SendAsync(byte[] json, string done);
}

/// <summary>
/// Talks to Atap's local API: one request per connection, framed as a 4-byte little-endian length
/// and at most 8 KB of UTF-8 JSON. It connects with CurrentUserOnly, so it refuses a pipe owned by
/// another user. Every step has a 2 second deadline.
/// </summary>
internal sealed class PushClient : IPushClient
{
    public const int MaxMessageBytes = 8 * 1024;
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    /// <summary>"Atap.v1.&lt;user SID&gt;", so each user has their own pipe.</summary>
    public static string PipeName => $"Atap.v1.{WindowsIdentity.GetCurrent().User?.Value}";

    public async Task<CliResult> SendAsync(byte[] json, string done)
    {
        var response = await SendAsync(writer => writer.WriteRawValue(json, skipInputValidation: false));
        if (response.Error is { } failure)
            return failure;
        return response.Root is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
            ? new CliResult(ExitCodes.Ok, done)
            : Rejected(response.Root);
    }

    public async Task<CliResult> PingAsync()
    {
        var response = await SendAsync(WritePing);
        if (response.Error is { } failure)
            return failure;
        return response.Root is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
            ? new CliResult(ExitCodes.Ok, "Atap is running.")
            : Rejected(response.Root);
    }

    /// <summary>
    /// The error code in a reply, safe to print. Anything but a short code such as "rate_limited" becomes
    /// "invalid", so a process posing as Atap cannot send control sequences to the terminal. Public for tests.
    /// </summary>
    public static string ErrorCode(JsonElement? reply)
    {
        if (reply is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.String)
        {
            return "invalid";
        }
        var code = error.GetString();
        return code is { Length: >= 1 and <= 32 } && code.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_')
            ? code
            : "invalid";
    }

    /// <summary>Builds a request frame. Public for tests.</summary>
    public static byte[] Frame(ReadOnlySpan<byte> json)
    {
        var frame = new byte[4 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, json.Length);
        json.CopyTo(frame.AsSpan(4));
        return frame;
    }

    private static void WritePing(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("op", "ping");
        writer.WriteEndObject();
    }

    private static async Task<(JsonElement? Root, CliResult? Error)> SendAsync(Action<Utf8JsonWriter> writeRequest)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(deadline.Token);
        }
        catch (UnauthorizedAccessException)
        {
            // CurrentUserOnly refused it: another account owns a pipe with Atap's name.
            return (null, new CliResult(ExitCodes.NotRunning, "A program run by another user has taken Atap's local API name. Atap was not contacted."));
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException)
        {
            return (null, new CliResult(ExitCodes.NotRunning, "Atap is not running, or its local API is off."));
        }

        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
                writeRequest(writer);
            await pipe.WriteAsync(Frame(buffer.WrittenSpan), deadline.Token);
            await pipe.FlushAsync(deadline.Token);

            var header = new byte[4];
            await pipe.ReadExactlyAsync(header, deadline.Token);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is <= 0 or > MaxMessageBytes)
                return (null, new CliResult(ExitCodes.Rejected, "Atap sent a reply that is not valid."));

            var body = new byte[length];
            await pipe.ReadExactlyAsync(body, deadline.Token);
            using var document = JsonDocument.Parse(body);
            return (document.RootElement.Clone(), null);
        }
        catch (OperationCanceledException)
        {
            return (null, new CliResult(ExitCodes.Timeout, "Atap did not answer in time."));
        }
        catch (Exception ex) when (ex is IOException or JsonException or EndOfStreamException)
        {
            return (null, new CliResult(ExitCodes.Rejected, "Atap closed the connection or sent a reply that is not valid."));
        }
    }

    private static CliResult Rejected(JsonElement? root) =>
        new(ExitCodes.Rejected, $"Atap rejected the request: {ErrorCode(root)}");
}
