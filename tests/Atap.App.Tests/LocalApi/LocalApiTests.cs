using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Atap.Core.Alerts;
using Atap.Core.LocalApi;
using Atap.Features.LocalApi;
using Atap.Platform.EnvironmentPath;
using Atap.Platform.Ipc;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Push;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Tests.LocalApi;

/// <summary>The pipe end to end, the push hub, the feature's answers and the PATH toggle.</summary>
public sealed class LocalApiTests : IDisposable
{
    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task AFullRequest_IsAnsweredOverThePipe()
    {
        var name = UniqueName();
        var seen = new List<(ApiRequest Request, string? Source)>();
        await using var server = Server(name, (request, source) =>
        {
            seen.Add((request, source));
            return Task.FromResult(ApiResponse.Pong("9.9.9"));
        });
        Assert.True(server.Start());

        var reply = await SendAsync(name, """{"op":"ping"}""");

        Assert.Contains("\"ok\":true", reply);
        Assert.Contains("9.9.9", reply);
        Assert.IsType<PingRequest>(Assert.Single(seen).Request);
        Assert.False(string.IsNullOrEmpty(seen[0].Source));
    }

    [Fact]
    public async Task ANameAlreadyTaken_KeepsTheApiOff()
    {
        var name = UniqueName();
        await using var first = Server(name, (_, _) => Task.FromResult(ApiResponse.Success));
        await using var second = Server(name, (_, _) => Task.FromResult(ApiResponse.Success));

        Assert.True(first.Start());
        Assert.False(second.Start());
    }

    [Fact]
    public async Task WithoutTheServer_ThereIsNoPipe()
    {
        await using var pipe = new NamedPipeClientStream(".", UniqueName(), PipeDirection.InOut, PipeOptions.CurrentUserOnly);

        await Assert.ThrowsAsync<TimeoutException>(() => pipe.ConnectAsync(200));
    }

    [Fact]
    public async Task MalformedRequests_GetAnError_AndTheServerCarriesOn()
    {
        var name = UniqueName();
        var limiter = new RateLimiter(TimeProvider.System, perSecond: 100_000);
        // A busy test runner can take longer than the 2 second deadline real clients get, so this server waits longer.
        await using var server = new LocalApiServer(name, new SynchronizationContext(), (_, _) => Task.FromResult(ApiResponse.Success), limiter, (_, _) => { }, TimeSpan.FromSeconds(15));
        Assert.True(server.Start());
        var random = new Random(7);

        for (var i = 0; i < 200; i++)
        {
            var junk = new byte[random.Next(1, 300)];
            random.NextBytes(junk);
            var reply = await SendRawAsync(name, junk);
            Assert.Contains("\"error\"", reply);
        }

        Assert.Contains("too_large", await SendRawAsync(name, new byte[PushFrame.MaxPayloadBytes + 1]));
        Assert.Contains("\"ok\":true", await SendAsync(name, """{"op":"ping"}"""));
    }

    [Fact]
    public void TheRateLimit_AllowsTwentyASecond()
    {
        var time = new FakeTimeProvider();
        var limiter = new RateLimiter(time);

        var allowed = Enumerable.Range(0, 25).Count(_ => limiter.TryTake());
        Assert.Equal(20, allowed);

        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(limiter.TryTake());
        Assert.True(limiter.TryTake());
        Assert.False(limiter.TryTake());
    }

    [Fact]
    public async Task Pushes_NeedThePushWidgetOn()
    {
        await using var feature = Feature(out _);
        _services.Layout.Update(d => d.EnabledWidgets.Remove(LocalApiFeature.PushWidgetId));

        var reply = await feature.HandleAsync(new PushRequest("build", "Building"), null);

        Assert.Equal(ApiErrorCode.WidgetDisabled, reply.Error);
    }

    [Fact]
    public async Task APush_ReachesTheHub_AndEndsAtItsTimeToLive()
    {
        await using var feature = Feature(out _);
        _services.Layout.Update(d => d.EnabledWidgets.Add(LocalApiFeature.PushWidgetId));
        await feature.StartAsync();
        using var hub = _services.Shared.Acquire<PushHub>();

        Assert.True((await feature.HandleAsync(new PushRequest("build", "Building") { Progress = 40, Ttl = TimeSpan.FromMinutes(1) }, "powershell")).Ok);
        var activity = Assert.Single(hub.Value.Activities.Items);
        Assert.Equal("powershell", activity.Source);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);

        _services.Time.Advance(TimeSpan.FromSeconds(61));

        Assert.True(hub.Value.Activities.IsEmpty);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task End_RemovesAPushEarly()
    {
        await using var feature = Feature(out _);
        _services.Layout.Update(d => d.EnabledWidgets.Add(LocalApiFeature.PushWidgetId));
        await feature.StartAsync();
        using var hub = _services.Shared.Acquire<PushHub>();
        await feature.HandleAsync(new PushRequest("build", "Building"), null);

        Assert.True((await feature.HandleAsync(new EndRequest("build"), null)).Ok);

        Assert.True(hub.Value.Activities.IsEmpty);
    }

    [Fact]
    public async Task AnAlert_ShowsOnTheNotch()
    {
        await using var feature = Feature(out var alerts);

        await feature.HandleAsync(new AlertRequest("Deploy done") { Subtitle = "atap", Icon = PushIcon.Check }, null);

        var alert = Assert.Single(alerts.Shown);
        Assert.Equal("Deploy done", alert.Title);
        Assert.Equal("done", alert.IconKey);
        Assert.Equal(AlertPriority.Normal, alert.Priority);
    }

    [Fact]
    public async Task Ping_SaysTheVersion()
    {
        await using var feature = Feature(out _);

        var reply = await feature.HandleAsync(new PingRequest(), null);

        Assert.True(reply.Ok);
        Assert.False(string.IsNullOrEmpty(reply.Version));
    }

    [Fact]
    public void ThePushWidget_ShowsNewestFirst_WithProgressAndSource()
    {
        using var model = new PushViewModel(_services.Text);
        var now = DateTimeOffset.UnixEpoch;

        model.Show(
        [
            new PushActivity("b", "Backup") { State = PushState.Done, UpdatedAt = now.AddMinutes(2) },
            new PushActivity("a", "Build") { Progress = 40, Source = "powershell", UpdatedAt = now },
        ]);

        Assert.Equal("Backup", model.Latest!.Title);
        Assert.Equal(_services.Text["push.state.done"], model.Latest.Short);
        Assert.Equal("40%", model.Items[1].Short);
        Assert.Equal(_services.Text.Format("push.from", ("source", "powershell")), model.Items[1].Detail);
    }

    [Theory]
    [InlineData(null, @"C:\Atap\cli", @"C:\Atap\cli")]
    [InlineData(@"C:\Tools", @"C:\Atap\cli", @"C:\Tools;C:\Atap\cli")]
    [InlineData(@"C:\Tools;", @"C:\Atap\cli", @"C:\Tools;C:\Atap\cli")]
    [InlineData(@"C:\Tools;c:\atap\CLI\", @"C:\Atap\cli", @"C:\Tools;c:\atap\CLI\")]
    public void PathList_AddsOneEntry_OnlyWhenMissing(string? path, string folder, string expected)
    {
        Assert.Equal(expected, PathList.Add(path, folder));
    }

    [Fact]
    public void PathList_AddThenRemove_GivesBackTheSamePath()
    {
        const string original = @"%USERPROFILE%\bin;C:\Tools";

        var added = PathList.Add(original, @"C:\Atap\cli");

        Assert.Equal(original, PathList.Remove(added, @"C:\Atap\cli"));
    }

    [Fact]
    public async Task ThePathToggle_AddsAndRemovesExactlyOneEntry()
    {
        _services.Settings.Load();
        _services.Settings.Register(new LocalApiDescriptor().Settings);
        _services.Settings.Register(new CliPathDescriptor().Settings);
        var path = new FakeUserPath { Value = @"C:\Tools" };
        await using var feature = new CliPathFeature(_services.Settings, path, @"C:\Atap\cli");
        await feature.StartAsync();
        Assert.Equal(0, path.Writes);

        _services.Settings.Set(CliPathDescriptor.OnPathKey, true);
        Assert.Equal(@"C:\Tools;C:\Atap\cli", path.Value);

        _services.Settings.Set(CliPathDescriptor.OnPathKey, false);
        Assert.Equal(@"C:\Tools", path.Value);
        Assert.Equal(2, path.Writes);
    }

    [Fact]
    public void TheApi_IsOffByDefault_AndInSafeMode()
    {
        var descriptor = new LocalApiDescriptor();
        var on = new Atap.Core.Widgets.SettingValues(descriptor.Settings, new Dictionary<string, System.Text.Json.JsonElement>
        {
            [LocalApiDescriptor.EnabledKey] = System.Text.Json.JsonDocument.Parse("true").RootElement,
        });

        Assert.False(descriptor.IsEnabled(new Atap.Core.Widgets.SettingValues(descriptor.Settings, null), Atap.Composition.AppMode.Normal));
        Assert.True(descriptor.IsEnabled(on, Atap.Composition.AppMode.Normal));
        Assert.False(descriptor.IsEnabled(on, Atap.Composition.AppMode.Safe));
        Assert.Equal(Atap.Core.Widgets.SettingScope.Machine, descriptor.Settings.Single().Scope);
    }

    private LocalApiFeature Feature(out FakeAlerts alerts)
    {
        _services.Layout.Load();
        alerts = new FakeAlerts();
        return new LocalApiFeature(_services.Shared, _services.Layout, alerts, new SynchronizationContext(), _services.Time, UniqueName(), (_, _) => { });
    }

    private static LocalApiServer Server(string name, ApiHandler handler) =>
        new(name, new SynchronizationContext(), handler, new RateLimiter(TimeProvider.System), (_, _) => { }, TimeSpan.FromSeconds(15));

    private static string UniqueName() => $"Atap.test.{Guid.NewGuid():N}";

    private static Task<string> SendAsync(string name, string json) => SendRawAsync(name, Encoding.UTF8.GetBytes(json));

    private static async Task<string> SendRawAsync(string name, byte[] body)
    {
        await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        // Generous, so a busy test machine does not fail it. The server answers in milliseconds when idle.
        await pipe.ConnectAsync(10_000);
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        // The server answers a length it refuses without reading the rest, so only the header goes then.
        await pipe.WriteAsync(body.Length > PushFrame.MaxPayloadBytes ? frame.AsMemory(0, 4) : frame);
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header);
        var reply = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
        await pipe.ReadExactlyAsync(reply);
        return Encoding.UTF8.GetString(reply);
    }

    private sealed class FakeUserPath : IUserPath
    {
        public string? Value { get; set; }

        public int Writes { get; private set; }

        public string? Read() => Value;

        public void Write(string value)
        {
            Value = value;
            Writes++;
        }
    }

    private sealed class FakeAlerts : IAlertSink
    {
        public List<Alert> Shown { get; } = [];

        public void Show(Alert alert) => Shown.Add(alert);

        public void Dismiss(string key)
        {
        }
    }
}
