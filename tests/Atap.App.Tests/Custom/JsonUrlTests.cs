using System.Text;
using System.Text.Json;
using Atap.Composition;
using Atap.Core.Custom;
using Atap.Core.Net;
using Atap.Core.Widgets;
using Atap.Settings.Form;
using Atap.Tests.Infrastructure;
using Atap.Tests.Settings;
using Atap.Widgets.JsonUrl;

namespace Atap.Tests.Custom;

public sealed class JsonUrlTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new("json-url#nas");
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);
    private readonly JsonUrlDescriptor _descriptor = new();
    private WidgetContext? _context;

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task Interval_IsNeverBelowAMinute()
    {
        var http = new FakeLink();
        http.Replies.Enqueue(Ok("""{"cpu": 12}"""));
        http.Replies.Enqueue(Ok("""{"cpu": 13}"""));
        var (widget, _) = Create(http);

        await widget.StartAsync(Settings(("url", "http://192.168.1.10/api"), ("path", "cpu"), ("interval", 10)));

        Assert.Equal(TimeSpan.FromSeconds(60), widget.Interval);
        Assert.Single(http.Requests);
        _services.Time.Advance(TimeSpan.FromSeconds(30));
        Assert.Single(http.Requests);
        _services.Time.Advance(TimeSpan.FromSeconds(40));
        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(60, _descriptor.Settings.Single(s => s.Key == JsonUrlDescriptor.IntervalKey).Min);
        await widget.StopAsync();
    }

    [Fact]
    public async Task FailedRefresh_KeepsTheLastValue_MarkedOutOfDate()
    {
        var http = new FakeLink();
        http.Replies.Enqueue(Ok("""{"cpu": 42.5}"""));
        http.Replies.Enqueue(FetchResult.Fail(FetchStatus.Failed, FetchError.Timeout));
        http.Replies.Enqueue(Ok("not json {"));
        http.Replies.Enqueue(Ok("""{"cpu": 7}"""));
        var (widget, view) = Create(http);

        await widget.StartAsync(Settings(("url", "https://nas.example/api"), ("path", "cpu"), ("unit", "%")));
        Assert.Equal("42.5%", view.Value?.DisplayText);
        Assert.False(view.Value?.Stale);

        Refresh();
        Assert.Equal("42.5%", view.Value?.DisplayText);
        Assert.True(view.Value?.Stale);
        Assert.Contains("Out of date", view.AccessibleSummary, StringComparison.Ordinal);

        Refresh();
        Assert.Equal("42.5%", view.Value?.DisplayText);
        Assert.True(view.Value?.Stale);

        Refresh();
        Assert.Equal("7%", view.Value?.DisplayText);
        Assert.False(view.Value?.Stale);
        await widget.StopAsync();
    }

    [Fact]
    public async Task FirstFetchFails_SaysWhy()
    {
        var http = new FakeLink();
        http.Replies.Enqueue(FetchResult.Fail(FetchStatus.Blocked, FetchError.InsecureHttp));
        var (widget, view) = Create(http);

        await widget.StartAsync(Settings(("url", "http://8.8.8.8/api")));

        Assert.Null(view.Value);
        Assert.Equal("Atap cannot open this link. Plain http works only for addresses on your home network.", view.Message);
        Assert.False(widget.IsLive);
        await widget.StopAsync();
    }

    [Fact]
    public async Task Threshold_MakesItLive_UntilTheValueSettles()
    {
        var http = new FakeLink();
        http.Replies.Enqueue(Ok("""{"disk": {"used": 50}}"""));
        http.Replies.Enqueue(Ok("""{"disk": {"used": 95}}"""));
        http.Replies.Enqueue(Ok("""{"disk": {"used": 40}}"""));
        var (widget, view) = Create(http);

        await widget.StartAsync(Settings(
            ("url", "https://nas.example/api"), ("path", "disk.used"), ("unit", "%"), ("alerts", true), ("warnAt", 80), ("criticalAt", 90)));
        Assert.False(widget.IsLive);

        Refresh();
        Assert.True(widget.IsLive);
        Assert.Equal(ValueLevel.Critical, view.Level);
        Assert.Equal("95%", view.CompactText);

        Refresh();
        Assert.False(widget.IsLive);
        await widget.StopAsync();
    }

    [Fact]
    public async Task Header_ComesFromTheSecret_AndStaysInMemory()
    {
        var http = new FakeLink();
        http.Replies.Enqueue(Ok("""{"ok": true}"""));
        var (widget, _) = Create(http);
        var reference = _context!.Secrets.Put("Authorization: Bearer abc123");

        await widget.StartAsync(Settings(("url", "https://nas.example/api"), ("path", "ok"), ("show", "status"), ("header", reference)));

        var request = Assert.Single(http.Requests);
        Assert.Equal("Bearer abc123", request.Headers?["Authorization"]);
        Assert.False(request.DiskCache);
        Assert.Equal(PayloadReader.JsonUrlMaxBytes, request.MaxBytes);
        await widget.StopAsync();
    }

    [Fact]
    public async Task WithoutAHeader_TheReplyMayUseTheDiskCache()
    {
        var http = new FakeLink();
        http.Replies.Enqueue(Ok("""{"ok": true}"""));
        var (widget, _) = Create(http);

        await widget.StartAsync(Settings(("url", "https://nas.example/api")));

        var request = Assert.Single(http.Requests);
        Assert.Null(request.Headers);
        Assert.True(request.DiskCache);
        await widget.StopAsync();
    }

    [Fact]
    public async Task HeaderFromAnotherPc_AsksForItAgain_AndFetchesNothing()
    {
        var http = new FakeLink();
        var (widget, view) = Create(http);

        await widget.StartAsync(Settings(("url", "https://nas.example/api"), ("header", "secret:made-elsewhere")));

        Assert.Empty(http.Requests);
        Assert.Equal("The sign-in header was saved on another PC. Enter it again here.", view.Message);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        await widget.StopAsync();
    }

    [Theory]
    [InlineData("", "$")]
    [InlineData("data.temperature", "$.data.temperature")]
    [InlineData("[0].name", "$[0].name")]
    [InlineData("$.a", "$.a")]
    public void Path_AddsTheRoot(string typed, string path) => Assert.Equal(path, JsonUrlSetup.Path(typed));

    [Theory]
    [InlineData("Authorization: Bearer abc", true)]
    [InlineData("X-Api-Key:abc", true)]
    [InlineData("no colon", false)]
    [InlineData(": value", false)]
    [InlineData("Bad Name: value", false)]
    [InlineData("Name:", false)]
    public void Header_MustBeNameColonValue(string text, bool valid) => Assert.Equal(valid, JsonUrlSetup.TryParseHeader(text, out _, out _));

    [Fact]
    public async Task TestButton_FetchesNow_AndPreviewsTheValue()
    {
        _services.Layout.Load();
        var http = new FakeLink();
        http.Replies.Enqueue(Ok("""{"temp": 31.25}"""));
        var target = new WidgetSettingsTarget(_services.Layout, "json-url#nas", _descriptor.Settings);
        target.Write(JsonUrlDescriptor.UrlKey, "https://nas.example/api");
        target.Write(JsonUrlDescriptor.PathKey, "temp");
        target.Write(JsonUrlDescriptor.UnitKey, "°C");
        using var form = new SettingsFormViewModel(_descriptor.Settings, new SettingsFormContext(target, _services.Text, new FakeSecretStore(), null, http));
        var field = Assert.IsType<CustomField>(form.Fields.Single(f => f.Key == JsonUrlDescriptor.TestKey));
        var tester = Assert.IsType<JsonUrlTester>(new JsonUrlTesterFor().Convert(field, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture));

        await tester.TestAsync();

        var request = Assert.Single(http.Requests);
        Assert.Equal(TimeSpan.Zero, request.MaxAge);
        Assert.Equal("json-url.testOk", tester.StatusKey);
        Assert.Equal("31.25°C", tester.Preview?.DisplayText);
        Assert.Contains("\"temp\": 31.25", tester.Sample, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestButton_WithoutALink_SaysSo()
    {
        _services.Layout.Load();
        var http = new FakeLink();
        var target = new WidgetSettingsTarget(_services.Layout, "json-url#nas", _descriptor.Settings);
        using var form = new SettingsFormViewModel(_descriptor.Settings, new SettingsFormContext(target, _services.Text, new FakeSecretStore(), null, http));
        var field = Assert.IsType<CustomField>(form.Fields.Single(f => f.Key == JsonUrlDescriptor.TestKey));

        var tester = new JsonUrlTester(field.Context);
        await tester.TestAsync();

        Assert.Empty(http.Requests);
        Assert.Equal("json-url.noLink", tester.StatusKey);
        Assert.Null(tester.Preview);
    }

    private (JsonUrlWidget Widget, JsonUrlViewModel View) Create(FakeLink http)
    {
        _context = _services.CreateContext("json-url#nas", _descriptor, _lifetime, _visibility, http);
        var widget = new JsonUrlWidget(_context);
        return (widget, Assert.IsType<JsonUrlViewModel>(widget.ViewModel));
    }

    /// <summary>The given values, with a 60 second interval unless one is given.</summary>
    private SettingValues Settings(params (string Key, object Value)[] values)
    {
        var stored = values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value));
        stored.TryAdd(JsonUrlDescriptor.IntervalKey, JsonSerializer.SerializeToElement(60));
        return new SettingValues(_descriptor.Settings, stored);
    }

    /// <summary>Past one 60 second interval and its slack.</summary>
    private void Refresh() => _services.Time.Advance(TimeSpan.FromSeconds(70));

    private static FetchResult Ok(string body) => new(FetchStatus.Fresh, Encoding.UTF8.GetBytes(body), DateTimeOffset.UtcNow, FetchError.None);

    /// <summary>Gives the queued replies in order, then fails.</summary>
    private sealed class FakeLink : IHttpDataService
    {
        public Queue<FetchResult> Replies { get; } = new();

        public List<FetchRequest> Requests { get; } = [];

        public Task<FetchResult> GetAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Replies.TryDequeue(out var reply) ? reply : FetchResult.Fail(FetchStatus.Failed, FetchError.Network));
        }
    }
}
