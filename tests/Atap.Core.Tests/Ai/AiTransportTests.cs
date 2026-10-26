using System.Net;
using System.Text;
using System.Text.Json;
using Atap.Core.Ai;
using Atap.Core.Tests.Net;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Core.Tests.Ai;

/// <summary>Ask AI's requests, streams, limits and refusals against a fake server and a fake clock. Nothing here goes online.</summary>
public sealed class AiTransportTests : IDisposable
{
    private const string Key = "sk-test-SECRET-1234567890";
    private static readonly IReadOnlyList<AiTurn> Question = [new(AiRole.User, "What is the capital of Malaysia?")];

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero));
    private readonly HttpDataServiceTests.FakeNetwork _network = new();
    private readonly FakeAiServer _server = new();
    private readonly List<AiTransport> _transports = [];
    private readonly List<AiAttempt> _attempts = [];

    public void Dispose()
    {
        foreach (var transport in _transports)
            transport.Dispose();
    }

    [Fact]
    public async Task Anthropic_PostsMessages_WithItsHeaders_AndStreams()
    {
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Events(
            "event: message_start\ndata: {\"type\":\"message_start\"}\n\n"
            + "event: ping\ndata: {\"type\": \"ping\"}\n\n"
            + "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Kuala \"}}\n\n"
            + "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Lumpur\"}}\n\n"
            + "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"));
        var (result, text) = await AskAsync(new AiAccount(AiProvider.Anthropic, "claude-haiku-5-5", Key));

        Assert.Equal(AiEventKind.Done, result.Kind);
        Assert.Equal("Kuala Lumpur", text);
        var request = Assert.Single(_server.Requests);
        Assert.Equal(HttpMethod.Post, request.Message.Method);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.Message.RequestUri!.AbsoluteUri);
        Assert.Equal(Key, request.Header("x-api-key"));
        Assert.Equal("2023-06-01", request.Header("anthropic-version"));
        Assert.Null(request.Message.Headers.Authorization);
        Assert.StartsWith("Atap/test", request.Message.Headers.UserAgent.ToString());
        using var body = JsonDocument.Parse(request.Body);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("claude-haiku-5-5", body.RootElement.GetProperty("model").GetString());
        var message = Assert.Single(body.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.False(body.RootElement.TryGetProperty("tools", out _));
    }

    [Theory]
    [InlineData(AiProvider.OpenAi, "https://api.openai.com/v1/chat/completions")]
    [InlineData(AiProvider.OpenRouter, "https://openrouter.ai/api/v1/chat/completions")]
    public async Task OpenAiStyle_PostsChatCompletions_WithBearer_UntilDone(AiProvider provider, string url)
    {
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Events(
            "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\"Kuala \"}}]}\n\n"
            + ": a comment the server sends to keep the line open\n\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\"Lumpur\"},\"finish_reason\":\"stop\"}]}\n\n"
            + "data: [DONE]\n\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\" never read\"}}]}\n\n"));
        var (result, text) = await AskAsync(new AiAccount(provider, "some-model", Key));

        Assert.Equal(AiEventKind.Done, result.Kind);
        Assert.Equal("Kuala Lumpur", text);
        var request = Assert.Single(_server.Requests);
        Assert.Equal(url, request.Message.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", request.Message.Headers.Authorization!.Scheme);
        Assert.Equal(Key, request.Message.Headers.Authorization.Parameter);
        using var body = JsonDocument.Parse(request.Body);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        var roles = body.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()).ToList();
        Assert.Equal(["system", "user"], roles);
    }

    [Fact]
    public async Task OwnServer_WorksWithoutAKey_OnThisPc()
    {
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Events("data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n\ndata: [DONE]\n\n"));
        var (result, text) = await AskAsync(new AiAccount(AiProvider.Custom, "llama3.2", null, new Uri("http://localhost:11434/v1")));

        Assert.Equal(AiEventKind.Done, result.Kind);
        Assert.Equal("Hi", text);
        var request = Assert.Single(_server.Requests);
        Assert.Equal("http://localhost:11434/v1/chat/completions", request.Message.RequestUri!.AbsoluteUri);
        Assert.Null(request.Message.Headers.Authorization);
    }

    [Fact]
    public async Task Gemini_PutsTheKeyInAHeader_NeverInTheAddress_AndEndsWithTheBody()
    {
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Events(
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Kuala \"}],\"role\":\"model\"}}]}\r\n\r\n"
            + "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"thinking\",\"thought\":true},{\"text\":\"Lumpur\"}],\"role\":\"model\"},\"finishReason\":\"STOP\"}]}\r\n\r\n"));
        var turns = new List<AiTurn> { new(AiRole.User, "Hi"), new(AiRole.Assistant, "Hello"), new(AiRole.User, "Capital?") };
        var (result, text) = await AskAsync(new AiAccount(AiProvider.Gemini, "gemini-2.5-flash", Key), turns);

        Assert.Equal(AiEventKind.Done, result.Kind);
        Assert.Equal("Kuala Lumpur", text);
        var request = Assert.Single(_server.Requests);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:streamGenerateContent?alt=sse",
            request.Message.RequestUri!.AbsoluteUri);
        Assert.Equal(Key, request.Header("x-goog-api-key"));
        using var body = JsonDocument.Parse(request.Body);
        var roles = body.RootElement.GetProperty("contents").EnumerateArray().Select(m => m.GetProperty("role").GetString()).ToList();
        Assert.Equal(["user", "model", "user"], roles);
    }

    [Fact]
    public async Task AStreamSplitMidLine_AndMidLetter_IsPutBackTogether()
    {
        var stream = new ScriptedStream();
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Streaming(stream));
        var bytes = Encoding.UTF8.GetBytes(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Selamat pagi éè 😀\"}}]}\r\n\r\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\" ok\"}}]}\n\ndata: [DONE]\n\n");
        // Three bytes at a time, so lines and the letters that take several bytes are split.
        for (var i = 0; i < bytes.Length; i += 3)
            stream.Send(bytes[i..Math.Min(i + 3, bytes.Length)]);
        stream.End();

        var (result, text) = await AskAsync(new AiAccount(AiProvider.OpenAi, "m", Key));

        Assert.Equal(AiEventKind.Done, result.Kind);
        Assert.Equal("Selamat pagi éè 😀 ok", text);
    }

    [Fact]
    public void TheRealHandler_Has10SecondsToConnect_NoRedirects_NoCookies_AndTheAddressCheck()
    {
        using var handler = AiTransport.CreateHandler();

        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.NotNull(handler.ConnectCallback);
    }

    [Fact]
    public async Task NoFirstPiece_In30Seconds_IsNoAnswerInTime()
    {
        var stream = new ScriptedStream();
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Streaming(stream));
        var ask = AskAsync(new AiAccount(AiProvider.Anthropic, "m", Key));
        await stream.WaitForWaitsAsync(1);
        // Pings keep the line busy but are not an answer.
        await stream.SendAndWaitAsync("event: ping\ndata: {\"type\":\"ping\"}\n\n");

        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(ask.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(1));

        var (result, _) = await ask;
        Assert.Equal(AiError.NoAnswerInTime, result.Error);
        Assert.Equal(AiError.NoAnswerInTime, Assert.Single(_attempts).Error);
    }

    [Fact]
    public async Task OwnServer_Gets90Seconds_ForTheFirstPiece()
    {
        var stream = new ScriptedStream();
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Streaming(stream));
        var ask = AskAsync(new AiAccount(AiProvider.Custom, "m", null, new Uri("http://127.0.0.1:11434/v1")));
        await stream.WaitForWaitsAsync(1);

        _time.Advance(TimeSpan.FromSeconds(89));
        Assert.False(ask.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(AiError.NoAnswerInTime, (await ask).Result.Error);
    }

    [Fact]
    public async Task AStreamThatStalls_For30Seconds_Stops_AndKeepsWhatArrived()
    {
        var stream = new ScriptedStream();
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Streaming(stream));
        var ask = AskAsync(new AiAccount(AiProvider.OpenAi, "m", Key));
        await stream.WaitForWaitsAsync(1);
        await stream.SendAndWaitAsync("data: {\"choices\":[{\"delta\":{\"content\":\"Half\"}}]}\n\n");

        _time.Advance(TimeSpan.FromSeconds(20));
        await stream.SendAndWaitAsync(": still here\n\n");
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(ask.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(1));

        var (result, text) = await ask;
        Assert.Equal(AiError.NoAnswerInTime, result.Error);
        Assert.Equal("Half", text);
    }

    [Fact]
    public async Task AnAnswerThatGoesOn_For3Minutes_Stops()
    {
        var stream = new ScriptedStream();
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Streaming(stream));
        var ask = AskAsync(new AiAccount(AiProvider.OpenAi, "m", Key));
        await stream.WaitForWaitsAsync(1);

        for (var i = 0; i < 9; i++)
        {
            await stream.SendAndWaitAsync("data: {\"choices\":[{\"delta\":{\"content\":\"a\"}}]}\n\n");
            _time.Advance(TimeSpan.FromSeconds(20));
        }

        var (result, text) = await ask;
        Assert.Equal(AiError.NoAnswerInTime, result.Error);
        Assert.Equal(new string('a', 9), text);
    }

    [Fact]
    public async Task Cancelling_Stops_AndIsNotLogged()
    {
        var stream = new ScriptedStream();
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Streaming(stream));
        using var stop = new CancellationTokenSource();
        var ask = AskAsync(new AiAccount(AiProvider.OpenAi, "m", Key), cancellationToken: stop.Token);
        await stream.WaitForWaitsAsync(1);
        await stream.SendAndWaitAsync("data: {\"choices\":[{\"delta\":{\"content\":\"Half\"}}]}\n\n");

        stop.Cancel();

        var (result, text) = await ask;
        Assert.Equal(AiError.Stopped, result.Error);
        Assert.Equal("Half", text);
        Assert.Empty(_attempts);
    }

    [Fact]
    public async Task TheOfflineSwitch_StopsAQuestionInFlight()
    {
        var stream = new ScriptedStream();
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Streaming(stream));
        var transport = Create();
        var ask = AskAsync(new AiAccount(AiProvider.OpenAi, "m", Key), transport: transport);
        await stream.WaitForWaitsAsync(1);

        transport.OfflineMode = true;

        Assert.Equal(AiError.Offline, (await ask).Result.Error);
        Assert.Equal(AiError.Offline, (await AskAsync(new AiAccount(AiProvider.OpenAi, "m", Key), transport: transport)).Result.Error);
        Assert.Equal(1, _server.Count);
    }

    [Fact]
    public async Task GoingOffline_StopsAQuestionToAService_ButNotToThisPc()
    {
        var stream = new ScriptedStream();
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Streaming(stream));
        var ask = AskAsync(new AiAccount(AiProvider.OpenAi, "m", Key));
        await stream.WaitForWaitsAsync(1);

        _network.Online = false;
        _network.RaiseChanged();

        Assert.Equal(AiError.Offline, (await ask).Result.Error);
        Assert.Equal(AiError.Offline, (await AskAsync(new AiAccount(AiProvider.Gemini, "m", Key))).Result.Error);
        Assert.Equal(1, _server.Count);

        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Events("data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n\ndata: [DONE]\n\n"));
        var local = await AskAsync(new AiAccount(AiProvider.Custom, "m", null, new Uri("http://localhost:11434/v1")));
        Assert.Equal(AiEventKind.Done, local.Result.Kind);
    }

    [Fact]
    public async Task ARedirect_IsRefused_AndNotFollowed()
    {
        _server.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("https://elsewhere.example/v1/messages");
            return Task.FromResult(response);
        };

        var (result, _) = await AskAsync(new AiAccount(AiProvider.Anthropic, "m", Key));

        Assert.Equal(AiError.NotAllowed, result.Error);
        Assert.Equal(1, _server.Count);
    }

    [Theory]
    [InlineData("http://8.8.8.8/v1")]
    [InlineData("http://[2001:4860:4860::8888]/v1")]
    [InlineData("http://user:pass@192.168.1.5/v1")]
    public async Task PlainHttp_ToAPublicAddress_IsRefused_BeforeAnythingIsSent(string server)
    {
        var (result, _) = await AskAsync(new AiAccount(AiProvider.Custom, "m", Key, new Uri(server)));

        Assert.Equal(AiError.NotAllowed, result.Error);
        Assert.Equal(0, _server.Count);
    }

    [Theory]
    [InlineData("http://192.168.1.5:11434/v1", true)]
    [InlineData("http://ollama.local:11434/v1", true)]
    [InlineData("https://my-server.example.com/v1", true)]
    [InlineData("http://8.8.8.8/v1", false)]
    public void ThePolicy_AllowsPrivatePlainHttp_AndAnyHttps_ForYourOwnServer(string server, bool allowed)
    {
        var account = new AiAccount(AiProvider.Custom, "m", null, new Uri(server));

        Assert.Equal(allowed, AiTransport.Check(AiDialects.Join(account.BaseUrl, "chat/completions"), account) is null);
    }

    [Fact]
    public void ThePolicy_HoldsEachServiceToItsOneHost()
    {
        var account = new AiAccount(AiProvider.OpenAi, "m", Key);

        Assert.Null(AiTransport.Check(new Uri("https://api.openai.com/v1/chat/completions"), account));
        Assert.Equal(AiError.NotAllowed, AiTransport.Check(new Uri("https://openrouter.ai/api/v1/chat/completions"), account));
        Assert.Equal(AiError.NotAllowed, AiTransport.Check(new Uri("http://api.openai.com/v1/chat/completions"), account));
    }

    [Fact]
    public async Task TheAnswer_IsCappedAt64Kilobytes()
    {
        var piece = new string('x', 1000);
        var body = new StringBuilder();
        for (var i = 0; i < 70; i++)
            body.Append("data: {\"choices\":[{\"delta\":{\"content\":\"").Append(piece).Append("\"}}]}\n\n");
        body.Append("data: [DONE]\n\n");
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Events(body.ToString()));

        var (result, text) = await AskAsync(new AiAccount(AiProvider.OpenAi, "m", Key));

        Assert.Equal(AiEventKind.Done, result.Kind);
        Assert.True(result.Capped);
        Assert.Equal(AiTransport.MaxAnswerBytes, text.Length);
    }

    [Fact]
    public async Task ALineThatNeverEnds_IsRefused()
    {
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Events("data: " + new string('x', AiTransport.MaxLineBytes + 10)));

        Assert.Equal(AiError.Unexpected, (await AskAsync(new AiAccount(AiProvider.OpenAi, "m", Key))).Result.Error);
    }

    [Theory]
    [InlineData(AiProvider.Anthropic, 401, "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\"}}", AiError.KeyNotAccepted)]
    [InlineData(AiProvider.Anthropic, 400, "{\"error\":{\"message\":\"Your credit balance is too low\"}}", AiError.OutOfCredit)]
    [InlineData(AiProvider.Anthropic, 529, "{}", AiError.ServiceBusy)]
    [InlineData(AiProvider.OpenAi, 429, "{\"error\":{\"code\":\"insufficient_quota\"}}", AiError.OutOfCredit)]
    [InlineData(AiProvider.OpenAi, 429, "{\"error\":{\"code\":\"rate_limit_exceeded\"}}", AiError.LimitReached)]
    [InlineData(AiProvider.OpenAi, 404, "{\"error\":{\"code\":\"model_not_found\"}}", AiError.ModelNotAvailable)]
    [InlineData(AiProvider.OpenRouter, 402, "{}", AiError.OutOfCredit)]
    [InlineData(AiProvider.Gemini, 400, "{\"error\":{\"status\":\"INVALID_ARGUMENT\",\"message\":\"API key not valid\"}}", AiError.KeyNotAccepted)]
    [InlineData(AiProvider.Gemini, 503, "{}", AiError.ServiceBusy)]
    public async Task HttpErrors_BecomePlainKinds(AiProvider provider, int status, string body, AiError expected)
    {
        _server.Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });

        var (result, _) = await AskAsync(new AiAccount(provider, "m", Key));

        Assert.Equal(expected, result.Error);
        var attempt = Assert.Single(_attempts);
        Assert.Equal(new AiAttempt(provider, status, expected), attempt);
    }

    [Theory]
    [InlineData("event: error\ndata: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}\n\n", AiError.ServiceBusy)]
    [InlineData("event: error\ndata: {\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\"}}\n\n", AiError.LimitReached)]
    public async Task AnErrorInTheStream_Fails(string events, AiError expected)
    {
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Events(events));

        Assert.Equal(expected, (await AskAsync(new AiAccount(AiProvider.Anthropic, "m", Key))).Result.Error);
    }

    [Fact]
    public async Task AnEmptyStream_IsUnexpected()
    {
        _server.Respond = (_, _) => Task.FromResult(FakeAiServer.Events(": nothing\n\n"));

        Assert.Equal(AiError.Unexpected, (await AskAsync(new AiAccount(AiProvider.Gemini, "m", Key))).Result.Error);
    }

    [Theory]
    [InlineData(AiProvider.Anthropic, "https://api.anthropic.com/v1/models?limit=1")]
    [InlineData(AiProvider.OpenAi, "https://api.openai.com/v1/models")]
    [InlineData(AiProvider.OpenRouter, "https://openrouter.ai/api/v1/key")]
    [InlineData(AiProvider.Gemini, "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1")]
    public async Task CheckingAKey_ListsModels_WhichCostsNothing(AiProvider provider, string url)
    {
        _server.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[]}") });

        var error = await Create().CheckAsync(new AiAccount(provider, "m", Key));

        Assert.Equal(AiError.None, error);
        var request = Assert.Single(_server.Requests);
        Assert.Equal(HttpMethod.Get, request.Message.Method);
        Assert.Equal(url, request.Message.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CheckingAKeyThatIsWrong_SaysSo()
    {
        _server.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") });

        Assert.Equal(AiError.KeyNotAccepted, await Create().CheckAsync(new AiAccount(AiProvider.OpenAi, "m", Key)));
    }

    [Theory]
    [InlineData(AiProvider.Anthropic)]
    [InlineData(AiProvider.OpenAi)]
    [InlineData(AiProvider.Gemini)]
    [InlineData(AiProvider.OpenRouter)]
    [InlineData(AiProvider.Custom)]
    public async Task TheKey_IsNeverInTheAddress_AnEvent_OrToString(AiProvider provider)
    {
        // The server repeats the key back in an error, as some do.
        _server.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent($"{{\"error\":{{\"message\":\"Incorrect API key provided: {Key}\"}}}}"),
        });
        var events = new List<AiEvent>();
        var account = new AiAccount(provider, "m", Key, provider == AiProvider.Custom ? new Uri("https://my-server.example.com/v1") : null);

        var result = await Create().AskAsync(account, Question, events.Add);
        await Create().CheckAsync(account);

        events.Add(result);
        Assert.All(_server.Requests, r => Assert.DoesNotContain(Key, r.Message.RequestUri!.AbsoluteUri, StringComparison.Ordinal));
        Assert.All(_server.Requests, r => Assert.DoesNotContain(Key, r.Body, StringComparison.Ordinal));
        Assert.All(events, e => Assert.DoesNotContain(Key, e.ToString(), StringComparison.Ordinal));
        Assert.All(_attempts, a => Assert.DoesNotContain(Key, a.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(Key, account.ToString(), StringComparison.Ordinal);
        Assert.True(account.HasKey);
    }

    private AiTransport Create()
    {
        var transport = new AiTransport("test", _network, _server.Handler, _time);
        transport.Attempted += (_, attempt) =>
        {
            lock (_attempts)
                _attempts.Add(attempt);
        };
        _transports.Add(transport);
        return transport;
    }

    private async Task<(AiEvent Result, string Text)> AskAsync(
        AiAccount account, IReadOnlyList<AiTurn>? turns = null, AiTransport? transport = null, CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        var result = await (transport ?? Create()).AskAsync(account, turns ?? Question, e =>
        {
            Assert.Equal(AiEventKind.Delta, e.Kind);
            lock (text)
                text.Append(e.Text);
        }, cancellationToken);
        lock (text)
            return (result, text.ToString());
    }
}
