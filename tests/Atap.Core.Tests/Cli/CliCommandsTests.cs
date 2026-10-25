using System.Runtime.Versioning;
using Atap.Cli;
using Atap.Core.LocalApi;

namespace Atap.Core.Tests.Cli;

/// <summary>push, end and alert build messages that Atap's own parser reads back exactly.</summary>
[SupportedOSPlatform("windows")]
public class CliCommandsTests
{
    [Fact]
    public async Task Push_SendsEveryOption_InFieldsAtapReads()
    {
        var (code, output, client) = await RunAsync(
            "push", "--id", "build", "--title", "Building", "--subtitle", "Release x64", "--progress", "40.5",
            "--state", "running", "--icon", "build", "--accent", "amber", "--ttl", "10m");

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal("Pushed.", output.Trim());
        var push = Assert.IsType<PushRequest>(Parse(client));
        Assert.Equal("build", push.Id);
        Assert.Equal("Building", push.Title);
        Assert.Equal("Release x64", push.Subtitle);
        Assert.Equal(40.5, push.Progress);
        Assert.Equal(PushState.Running, push.State);
        Assert.Equal(PushIcon.Build, push.Icon);
        Assert.Equal(PushAccent.Amber, push.Accent);
        Assert.Equal(TimeSpan.FromMinutes(10), push.Ttl);
    }

    [Fact]
    public async Task Push_WithOnlyIdAndTitle_IsEnough()
    {
        var (code, _, client) = await RunAsync("push", "--id", "backup", "--title", "Backing up");

        Assert.Equal(ExitCodes.Ok, code);
        var push = Assert.IsType<PushRequest>(Parse(client));
        Assert.Null(push.Progress);
        Assert.Equal(PushProtocol.DefaultTtl, push.EffectiveTtl);
    }

    [Fact]
    public async Task End_SendsTheId()
    {
        var (code, output, client) = await RunAsync("end", "--id", "build");

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal("Ended.", output.Trim());
        Assert.Equal("build", Assert.IsType<EndRequest>(Parse(client)).Id);
    }

    [Fact]
    public async Task Alert_SendsTitleSubtitleAndIcon()
    {
        var (code, _, client) = await RunAsync("alert", "--title", "Deploy done", "--subtitle", "atap.novafortress.my", "--icon", "check");

        Assert.Equal(ExitCodes.Ok, code);
        var alert = Assert.IsType<AlertRequest>(Parse(client));
        Assert.Equal("Deploy done", alert.Title);
        Assert.Equal("atap.novafortress.my", alert.Subtitle);
        Assert.Equal(PushIcon.Check, alert.Icon);
    }

    [Fact]
    public async Task PushJson_ReadsStandardInput_AndAddsTheOp()
    {
        var (code, _, client) = await RunAsync(new StringReader("""{"id":"sync","title":"Syncing","progress":75}"""), "push", "--json", "-");

        Assert.Equal(ExitCodes.Ok, code);
        var push = Assert.IsType<PushRequest>(Parse(client));
        Assert.Equal("sync", push.Id);
        Assert.Equal(75, push.Progress);
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("not json")]
    public async Task PushJson_ThatIsNotAnObject_IsAUsageError(string json)
    {
        var (code, _, client) = await RunAsync(new StringReader(json), "push", "--json", "-");

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Empty(client.Sent);
    }

    [Fact]
    public async Task PushJson_OverTheLimit_IsAUsageError()
    {
        var big = $$"""{"id":"x","title":"{{new string('a', 9000)}}"}""";

        var (code, _, client) = await RunAsync(new StringReader(big), "push", "--json", "-");

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Empty(client.Sent);
    }

    [Theory]
    [InlineData("push", "--title", "No id")]
    [InlineData("push", "--id", "x")]
    [InlineData("end")]
    [InlineData("alert", "--subtitle", "No title")]
    [InlineData("push", "--id", "x", "--title", "T", "--colour", "red")]
    [InlineData("push", "--id", "x", "--title")]
    [InlineData("push", "--id", "x", "--title", "T", "--progress", "half")]
    public async Task WrongOptions_AreAUsageError_AndSendNothing(params string[] args)
    {
        var (code, _, client) = await RunAsync(args);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Empty(client.Sent);
    }

    [Fact]
    public async Task ARejectedRequest_ExitsWith3()
    {
        var client = new FakeClient(new CliResult(ExitCodes.Rejected, "Atap rejected the request: widget_disabled"));
        var error = new StringWriter();

        var code = await CliApp.RunAsync(["push", "--id", "x", "--title", "T"], new StringWriter(), error, client);

        Assert.Equal(ExitCodes.Rejected, code);
        Assert.Contains("widget_disabled", error.ToString());
    }

    private static ApiRequest Parse(FakeClient client) =>
        PushProtocol.Parse(Assert.Single(client.Sent)).Request ?? throw new Xunit.Sdk.XunitException("Atap's parser refused the message.");

    private static Task<(int Code, string Output, FakeClient Client)> RunAsync(params string[] args) => RunAsync(TextReader.Null, args);

    private static async Task<(int Code, string Output, FakeClient Client)> RunAsync(TextReader input, params string[] args)
    {
        var client = new FakeClient(new CliResult(ExitCodes.Ok, "ok"));
        var output = new StringWriter();
        var code = await CliApp.RunAsync(args, output, new StringWriter(), client, input);
        return (code, output.ToString(), client);
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
