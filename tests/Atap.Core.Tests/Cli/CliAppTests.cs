using System.Buffers.Binary;
using System.Runtime.Versioning;
using Atap.Cli;

namespace Atap.Core.Tests.Cli;

// The tool is Windows only, like the named pipe it talks to.
[SupportedOSPlatform("windows")]
public class CliAppTests
{
    [Fact]
    public async Task NoArguments_PrintsUsageAndExitsWith1()
    {
        var (code, output, error) = await RunAsync();

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Empty(output);
        Assert.Contains("Usage: atap", error);
    }

    [Fact]
    public async Task UnknownCommand_ExitsWith1()
    {
        var (code, _, error) = await RunAsync("fly");

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("fly", error);
    }

    [Fact]
    public async Task Version_PrintsTheToolVersion()
    {
        var (code, output, _) = await RunAsync("--version");

        Assert.Equal(ExitCodes.Ok, code);
        Assert.StartsWith("atap ", output);
    }

    [Theory]
    [InlineData(ExitCodes.Ok)]
    [InlineData(ExitCodes.NotRunning)]
    [InlineData(ExitCodes.Timeout)]
    public async Task Ping_ReturnsTheClientResult(int exitCode)
    {
        var (code, output, error) = await RunAsync(new FakeClient(new CliResult(exitCode, "message")), "ping");

        Assert.Equal(exitCode, code);
        Assert.Contains("message", exitCode == ExitCodes.Ok ? output : error);
    }

    [Fact]
    public void Frame_PrefixesTheLengthLittleEndian()
    {
        var frame = PushClient.Frame("{}"u8);

        Assert.Equal(6, frame.Length);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(frame));
        Assert.Equal((byte)'{', frame[4]);
    }

    [Fact]
    public void PipeName_IsPerUser()
    {
        Assert.StartsWith("Atap.v1.S-1-", PushClient.PipeName);
    }

    [Theory]
    [InlineData("""{ "ok": false, "error": "rate_limited" }""", "rate_limited")]
    [InlineData("""{ "ok": false, "error": "too_large" }""", "too_large")]
    [InlineData("""{ "ok": false, "error": "\u001b[31mbad\u001b[0m" }""", "invalid")]
    [InlineData("""{ "ok": false, "error": "line\r\nAtap is running." }""", "invalid")]
    [InlineData("""{ "ok": false, "error": "a-very-long-error-code-that-goes-on-and-on" }""", "invalid")]
    [InlineData("""{ "ok": false, "error": 7 }""", "invalid")]
    [InlineData("""{ "ok": false }""", "invalid")]
    [InlineData("""[ "error" ]""", "invalid")]
    public void ErrorCode_PrintsOnlyShortPlainCodes(string reply, string expected)
    {
        using var document = System.Text.Json.JsonDocument.Parse(reply);

        Assert.Equal(expected, PushClient.ErrorCode(document.RootElement));
    }

    private static Task<(int Code, string Output, string Error)> RunAsync(params string[] args) => RunAsync(null, args);

    private static async Task<(int Code, string Output, string Error)> RunAsync(IPushClient? client, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await CliApp.RunAsync(args, output, error, client);
        return (code, output.ToString(), error.ToString());
    }

    private sealed class FakeClient(CliResult result) : IPushClient
    {
        public List<byte[]> Sent { get; } = [];

        public Task<CliResult> PingAsync() => Task.FromResult(result);

        public Task<CliResult> SendAsync(byte[] json, string done)
        {
            Sent.Add(json);
            return Task.FromResult(result.ExitCode == ExitCodes.Ok ? result with { Message = done } : result);
        }
    }
}
