using System.Diagnostics;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Calendar;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Net;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.NextMeeting;

[assembly: AtapWidget(typeof(NextMeetingDescriptor))]

namespace Atap.Widgets.NextMeeting;

/// <summary>The next meeting from a calendar link. Constants only.</summary>
public sealed class NextMeetingDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "next-meeting";
    public const string UrlKey = "url";

    public string TypeId => TypeIdValue;

    public string NameKey => "next-meeting.name";

    public string DescriptionKey => "next-meeting.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(UrlKey, "next-meeting.url", SettingType.Secret, null) { DescriptionKey = "next-meeting.urlHint" },
    ];

    public WidgetCapabilities Capabilities { get; } = new(WidgetPermissions.Network | WidgetPermissions.UserUrls, [], "next-meeting.privacy");

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/NextMeeting/NextMeetingViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new NextMeetingWidget(context, OpenLink);

    private static void OpenLink(Uri link)
    {
        try
        {
            Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No app handles the link. Nothing to do.
        }
    }
}

/// <summary>
/// Fetches the calendar every 15 minutes. The link is a secret: it is read from the secret store, fetched with
/// FromSecret so the copy stays in memory, and never shown or logged. One timer waits for the next change (5
/// minutes before a start, the start, the end). A minute tick runs only while a meeting is live or the card is on
/// screen. The feed is read off the UI thread.
/// </summary>
public sealed class NextMeetingWidget : WidgetBase
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(15);

    /// <summary>Feeds with years of history are larger than the default limit.</summary>
    public const int MaxFeedBytes = 8 * 1024 * 1024;

    private readonly Action<Uri> _open;
    private readonly NextMeetingViewModel _viewModel;
    private Uri? _url;
    private IReadOnlyList<CalendarEvent> _events = [];
    private CancellationTokenSource? _stopping;
    private IDisposable? _refresh;
    private IDisposable? _change;
    private IDisposable? _minute;
    private bool _live;

    internal NextMeetingWidget(WidgetContext context, Action<Uri> open)
        : base(context)
    {
        _open = open;
        _viewModel = new NextMeetingViewModel(context.Text, JoinNext);
    }

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _stopping = new CancellationTokenSource();
        Context.Visibility.Changed += OnVisibilityChanged;
        _url = FeedUrl(settings.GetSecretRef(NextMeetingDescriptor.UrlKey) is { } reference ? Context.Secrets.Get(reference) : null);
        if (_url is null)
        {
            _viewModel.ShowState(MeetingState.NoLink);
            return Task.CompletedTask;
        }
        _refresh = Context.Scheduler.Every(RefreshEvery, () => Loading = LoadAsync(), new ScheduleOptions(TimeSpan.FromMinutes(3), Deferrable: true));
        Loading = LoadAsync();
        return Task.CompletedTask;
    }

    /// <summary>The load in progress, for tests.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        _live = false;
        _refresh?.Dispose();
        _refresh = null;
        _change?.Dispose();
        _change = null;
        _minute?.Dispose();
        _minute = null;
        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;
        _events = [];
        _url = null;
        SetIsLive(false);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    /// <summary>webcal:// links are https. Anything that is not http or https is ignored.</summary>
    internal static Uri? FeedUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        if (text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text["webcal://".Length..];
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;
    }

    private async Task LoadAsync()
    {
        if (_stopping is not { } stopping || _url is not { } url)
            return;
        try
        {
            var request = new FetchRequest(url, RefreshEvery) { FromSecret = true, DiskCache = false, MaxBytes = MaxFeedBytes };
            var result = await Context.Http.GetAsync(request, stopping.Token);
            if (stopping.IsCancellationRequested)
                return;
            if (!result.Body.IsEmpty)
            {
                // A feed with years of history takes a while to read, so it is read off the UI thread.
                var body = result.Body;
                var events = await Task.Run(() => IcsParser.Parse(Encoding.UTF8.GetString(body.Span), TimeZoneInfo.Local), stopping.Token);
                if (stopping.IsCancellationRequested)
                    return;
                _events = events;
            }
            else if (_events.Count == 0)
            {
                _viewModel.ShowState(MeetingState.Failed);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        Show();
    }

    private void Show()
    {
        _change?.Dispose();
        _change = null;
        if (_stopping is null)
            return;
        var now = Context.Time.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        var endOfTomorrow = new DateTimeOffset(local.Date.AddDays(2), local.Offset);
        var meetings = MeetingSchedule.Between(_events, now, endOfTomorrow);
        var next = MeetingSchedule.Next(meetings, now);
        _viewModel.Show(next, meetings.Where(m => m != next).Take(3).ToList(), now);
        _live = next is not null && MeetingSchedule.IsLive(next, now);
        SetIsLive(_live);
        FollowMinutes(next is not null && (_live || Shown));

        // The next moment the view changes: going live, starting, or ending.
        if (next is not null)
        {
            var due = new[] { next.Start - MeetingSchedule.LiveBefore, next.Start, next.End }.Where(t => t > now).DefaultIfEmpty(next.End).Min();
            _change = Context.Scheduler.At(due, Show);
        }
    }

    private bool Shown => Context.Visibility.Value is WidgetVisibility.Compact or WidgetVisibility.Expanded or WidgetVisibility.Panel;

    /// <summary>"In 45 min" counts down each minute while a meeting is live or the card is on screen.</summary>
    private void FollowMinutes(bool tick)
    {
        if (tick && _minute is null)
            _minute = Context.Scheduler.EveryMinute(() => _viewModel.Tick(Context.Time.GetUtcNow()));
        else if (!tick && _minute is not null)
        {
            _minute.Dispose();
            _minute = null;
        }
    }

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        if (_stopping is null || !_viewModel.HasNext)
            return;
        var now = Context.Time.GetUtcNow();
        _viewModel.Tick(now);
        FollowMinutes(Shown || _live);
    }

    private void JoinNext(Uri link) => _open(link);
}

public enum MeetingState
{
    Loading,
    NoLink,
    Failed,
    Ready,
}

/// <summary>A later meeting in the list: "14:00", "Design review".</summary>
public sealed record MeetingRow(string Time, string Title);

public sealed class NextMeetingViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private readonly Action<Uri> _join;
    private MeetingState _state = MeetingState.Loading;
    private Meeting? _next;
    private IReadOnlyList<Meeting> _later = [];
    private DateTimeOffset _now;

    public NextMeetingViewModel(ILocalizer text, Action<Uri> join)
    {
        _text = text;
        _join = join;
        JoinCommand = new RelayCommand(() =>
        {
            if (_next?.JoinUrl is { } link)
                _join(link);
        });
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IRelayCommand JoinCommand { get; }

    public bool HasNext => _next is not null;

    public bool CanJoin => _next?.JoinUrl is not null;

    public bool ShowMessage => _next is null;

    public string Message => _state switch
    {
        MeetingState.NoLink => _text["next-meeting.noLink"],
        MeetingState.Failed => _text["next-meeting.failed"],
        MeetingState.Loading => _text["next-meeting.loading"],
        _ => _text["next-meeting.none"],
    };

    public string Title => _next is { } m ? (m.Title.Length > 0 ? m.Title : _text["next-meeting.untitled"]) : string.Empty;

    /// <summary>"Now", "In 4 min", or "Today 14:00" and "Tomorrow 09:30".</summary>
    public string When
    {
        get
        {
            if (_next is not { } m)
                return string.Empty;
            if (m.Start <= _now)
                return _text["next-meeting.now"];
            var until = m.Start - _now;
            if (until <= TimeSpan.FromMinutes(60))
                return _text.Format("next-meeting.inMinutes", ("minutes", Math.Max(1, (int)Math.Ceiling(until.TotalMinutes)).ToString(CultureInfo.CurrentCulture)));
            return DayAndTime(m.Start);
        }
    }

    public string Location => _next?.Location ?? string.Empty;

    public bool HasLocation => Location.Length > 0;

    public IReadOnlyList<MeetingRow> Later => _later.Select(m => new MeetingRow(DayAndTime(m.Start), m.Title.Length > 0 ? m.Title : _text["next-meeting.untitled"])).ToList();

    public bool HasLater => _later.Count > 0;

    public string AccessibleSummary => _next is null ? Message : _text.Format("next-meeting.accessible", ("title", Title), ("when", When));

    internal void ShowState(MeetingState state)
    {
        _state = state;
        _next = null;
        _later = [];
        OnPropertyChanged(string.Empty);
    }

    internal void Show(Meeting? next, IReadOnlyList<Meeting> later, DateTimeOffset now)
    {
        _state = MeetingState.Ready;
        _next = next;
        _later = later;
        _now = now;
        OnPropertyChanged(string.Empty);
    }

    internal void Tick(DateTimeOffset now)
    {
        _now = now;
        OnPropertyChanged(nameof(When));
        OnPropertyChanged(nameof(AccessibleSummary));
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private string DayAndTime(DateTimeOffset start)
    {
        var local = TimeZoneInfo.ConvertTime(start, TimeZoneInfo.Local);
        var today = TimeZoneInfo.ConvertTime(_now, TimeZoneInfo.Local).Date;
        var day = local.Date == today ? _text["next-meeting.today"] : _text["next-meeting.tomorrow"];
        return $"{day} {local.ToString("HH:mm", CultureInfo.InvariantCulture)}";
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
