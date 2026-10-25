using System.Text;
using System.Text.Json;
using Atap.Composition;
using Atap.Core.Net;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;
using Atap.Widgets.NextMeeting;

namespace Atap.Tests.Productivity;

public sealed class NextMeetingTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new("next-meeting");
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task GoesLiveFiveMinutesBefore_FetchesFromTheSecretInMemory()
    {
        // TestServices starts at 2026-10-25 09:00 UTC.
        var http = new FakeCalendar(
            "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:a\r\nSUMMARY:Design review\r\nDTSTART:20261025T090300Z\r\nDTEND:20261025T093000Z\r\n" +
            "LOCATION:https://meet.google.com/abc-defg-hij\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
        var descriptor = new NextMeetingDescriptor();
        var context = _services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility, http);
        var reference = context.Secrets.Put("webcal://calendar.example/private.ics");
        var settings = new SettingValues(descriptor.Settings, new Dictionary<string, JsonElement> { [NextMeetingDescriptor.UrlKey] = JsonSerializer.SerializeToElement(reference) });
        Uri? opened = null;
        var widget = new NextMeetingWidget(context, link => opened = link);

        await widget.StartAsync(settings);
        var view = Assert.IsType<NextMeetingViewModel>(widget.ViewModel);

        var request = Assert.Single(http.Requests);
        Assert.Equal("https://calendar.example/private.ics", request.Url.AbsoluteUri);
        Assert.True(request.FromSecret);
        Assert.False(request.DiskCache);
        Assert.True(widget.IsLive);
        Assert.Equal("Design review", view.Title);
        Assert.Equal("In 3 min", view.When);

        view.JoinCommand.Execute(null);
        Assert.Equal("https://meet.google.com/abc-defg-hij", opened?.AbsoluteUri);

        await widget.StopAsync();
        Assert.False(widget.IsLive);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task WithoutALink_SaysWhereToAddIt_AndFetchesNothing()
    {
        var http = new FakeCalendar("");
        var descriptor = new NextMeetingDescriptor();
        var widget = new NextMeetingWidget(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility, http), _ => { });

        await widget.StartAsync(new SettingValues(descriptor.Settings, null));

        Assert.Empty(http.Requests);
        Assert.Equal("Add your calendar link in this widget's settings.", Assert.IsType<NextMeetingViewModel>(widget.ViewModel).Message);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        await widget.StopAsync();
    }

    private sealed class FakeCalendar(string body) : IHttpDataService
    {
        public List<FetchRequest> Requests { get; } = [];

        public Task<FetchResult> GetAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new FetchResult(FetchStatus.Fresh, Encoding.UTF8.GetBytes(body), DateTimeOffset.UtcNow, FetchError.None));
        }
    }
}
