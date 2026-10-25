using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Layout;
using Atap.Core.LocalApi;
using Atap.Core.Settings;
using Atap.Core.Shared;
using Atap.Core.Widgets;
using Atap.Diagnostics;
using Atap.Features.LocalApi;
using Atap.Platform.Ipc;

[assembly: AtapFeature(typeof(LocalApiDescriptor))]

namespace Atap.Features.LocalApi;

/// <summary>
/// The local API: programs on this PC, such as a build script through <c>atap push</c>, show progress and alerts
/// on the notch. Off by default, and a Machine setting, so sync can never turn it on. Not in safe mode.
/// </summary>
internal sealed class LocalApiDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "localApi";
    public const string EnabledKey = "localApi.enabled";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(EnabledKey, "localapi.enabled", SettingType.Bool, false)
        {
            SectionId = SettingsSections.Advanced,
            DescriptionKey = "localapi.enabledHint",
            Scope = SettingScope.Machine,
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [EnabledKey];

    public bool IsEnabled(SettingValues settings, AppMode mode) => mode != AppMode.Safe && settings.GetBool(EnabledKey);

    public IAppFeature Create(FeatureContext context) => new LocalApiFeature(
        context.Shared, context.Layout, context.Alerts.For(FeatureId), context.Ui, context.Time,
        LocalApiServer.NameForCurrentUser, (message, ex) => Log.Warn(message, ex));
}

/// <summary>Whether the local API listens, for the Settings card. The feature writes it.</summary>
internal enum LocalApiState
{
    Off,
    Running,

    /// <summary>Another program holds the pipe name.</summary>
    CouldNotStart,
}

internal static class LocalApiStatus
{
    public static LocalApiState State { get; private set; }

    public static event EventHandler? Changed;

    public static void Set(LocalApiState state)
    {
        if (state == State)
            return;
        State = state;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}

/// <summary>Runs the pipe server while on and answers each request on the UI thread.</summary>
internal sealed class LocalApiFeature(
    ISharedServices shared,
    ILayoutService layout,
    IAlertSink alerts,
    SynchronizationContext ui,
    TimeProvider time,
    string pipeName,
    Action<string, Exception?> log) : IAppFeature
{
    /// <summary>The push widget's type and instance id. Pushes need it on, or they are refused.</summary>
    public const string PushWidgetId = "push";

    private Lease<PushHub>? _hub;
    private LocalApiServer? _server;
    private int _alerts;

    public Task StartAsync()
    {
        _hub = shared.Acquire<PushHub>();
        _server = new LocalApiServer(pipeName, ui, HandleAsync, new RateLimiter(time), log);
        LocalApiStatus.Set(_server.Start() ? LocalApiState.Running : LocalApiState.CouldNotStart);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
            await _server.DisposeAsync();
        _server = null;
        _hub?.Dispose();
        _hub = null;
        LocalApiStatus.Set(LocalApiState.Off);
    }

    /// <summary>One request. Pushes only display: they carry no link, image or action.</summary>
    public Task<ApiResponse> HandleAsync(ApiRequest request, string? source) => Task.FromResult(request switch
    {
        PingRequest => ApiResponse.Pong(AppInfo.Version),
        PushRequest or EndRequest when !layout.Active.EnabledWidgets.Contains(PushWidgetId) =>
            ApiResponse.Failure(ApiErrorCode.WidgetDisabled, "Turn on the Push widget in Atap's Settings."),
        PushRequest push => Push(push, source),
        EndRequest end => End(end),
        AlertRequest alert => Alert(alert),
        _ => ApiResponse.Failure(ApiErrorCode.UnknownOp),
    });

    private ApiResponse Push(PushRequest push, string? source)
    {
        _hub?.Value.Push(push, source);
        return ApiResponse.Success;
    }

    private ApiResponse End(EndRequest end)
    {
        _hub?.Value.End(end.Id);
        return ApiResponse.Success;
    }

    private ApiResponse Alert(AlertRequest alert)
    {
        alerts.Show(new Alert($"api.{++_alerts}", AlertPriority.Normal, alert.Title)
        {
            Subtitle = alert.Subtitle,
            IconKey = alert.Icon is { } icon ? IconKey(icon) : "bell",
        });
        return ApiResponse.Success;
    }

    /// <summary>The notch's alert icon for an API icon. Names the alert icons know pass through.</summary>
    public static string IconKey(PushIcon icon) => icon switch
    {
        PushIcon.Check => "done",
        _ => ApiNames.Of(icon),
    };
}
