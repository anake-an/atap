using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.NotchSize;
using Atap.Notch;

[assembly: AtapFeature(typeof(NotchSizeDescriptor))]

namespace Atap.Features.NotchSize;

/// <summary>
/// How much room the notch takes: Normal, Slim (a thin bar that opens on hover, and music and alerts still
/// show) or Tucked away (a hairline at the screen edge, and only Critical alerts drop down). Also Hide for
/// 1 hour, which ends by itself or when Atap is started again. All of it is in the notch's right-click menu.
/// </summary>
internal sealed class NotchSizeDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "notchSize";
    public const string SizeKey = "notch.size";
    public const string Normal = "normal";
    public const string Slim = "slim";
    public const string Tucked = "tucked";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(SizeKey, "notchsize.size", SettingType.Choice, Normal, [Normal, Slim, Tucked])
        {
            SectionId = SettingsSections.General,
            DescriptionKey = "notchsize.sizeHint",
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    /// <summary>Not in safe mode, so the notch is always full size there.</summary>
    public bool IsEnabled(SettingValues settings, AppMode mode) => mode != AppMode.Safe;

    public IAppFeature Create(FeatureContext context) =>
        new NotchSizeFeature(context.Settings, context.Notch, context.Commands, context.Scheduler, context.Time, FeatureId);
}

/// <summary>Turns the size setting into notch flags, and holds the Hide flag for an hour when asked. Holds no timer otherwise.</summary>
internal sealed class NotchSizeFeature(
    IAppSettings settings, INotchControl notch, ICommandRegistry commands, IScheduler scheduler, TimeProvider time, string ownerId) : IAppFeature
{
    public const string HideForAnHourCommand = "notch.hideHour";
    public const string ShowNowCommand = "notch.showNow";
    public const string MenuGroup = "notch.size";

    public static readonly TimeSpan HideFor = TimeSpan.FromHours(1);

    private readonly List<IDisposable> _registrations = [];
    private IDisposable? _comeBack;

    /// <summary>True while Hide for 1 hour holds the notch hidden.</summary>
    public bool IsHiddenForAnHour => _comeBack is not null;

    public Task StartAsync()
    {
        settings.Changed += OnSettingsChanged;
        Apply();
        RegisterSize("notch.size.normal", "notchsize.menu.normal", NotchSizeDescriptor.Normal, 10);
        RegisterSize("notch.size.slim", "notchsize.menu.slim", NotchSizeDescriptor.Slim, 11);
        RegisterSize("notch.size.tucked", "notchsize.menu.tucked", NotchSizeDescriptor.Tucked, 12);
        _registrations.Add(commands.Register(new AtapCommand(HideForAnHourCommand, "notchsize.menu.hideHour", _ =>
        {
            HideForAnHour();
            return Task.CompletedTask;
        })
        {
            MenuGroup = MenuGroup,
            MenuOrder = 13,
            KeywordsKey = "notchsize.hideKeywords",
            IsChecked = () => IsHiddenForAnHour,
        }));
        _registrations.Add(commands.Register(new AtapCommand(ShowNowCommand, "notchsize.command.showNow", _ =>
        {
            ShowNow();
            return Task.CompletedTask;
        })
        {
            KeywordsKey = "notchsize.showKeywords",
        }));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        settings.Changed -= OnSettingsChanged;
        _comeBack?.Dispose();
        _comeBack = null;
        foreach (var registration in _registrations)
            registration.Dispose();
        _registrations.Clear();
        // The feature host clears every flag this owner set once the feature has stopped.
        return ValueTask.CompletedTask;
    }

    /// <summary>Hides the notch for an hour. Asked again, the hour starts over. It comes back after sleep too.</summary>
    public void HideForAnHour()
    {
        _comeBack?.Dispose();
        notch.SetFlag(NotchFlag.Hide, ownerId, on: true);
        _comeBack = scheduler.At(time.GetUtcNow() + HideFor, ShowNow, new ScheduleOptions(Missed: MissedRun.RunOnce));
    }

    /// <summary>Ends Hide for 1 hour early. Starting Atap again runs this.</summary>
    public void ShowNow()
    {
        _comeBack?.Dispose();
        _comeBack = null;
        notch.SetFlag(NotchFlag.Hide, ownerId, on: false);
    }

    private void RegisterSize(string id, string titleKey, string size, int order) =>
        _registrations.Add(commands.Register(new AtapCommand(id, titleKey, _ =>
        {
            settings.Set(NotchSizeDescriptor.SizeKey, size);
            return Task.CompletedTask;
        })
        {
            MenuGroup = MenuGroup,
            MenuOrder = order,
            KeywordsKey = "notchsize.sizeKeywords",
            IsChecked = () => CurrentSize() == size,
        }));

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(NotchSizeDescriptor.SizeKey))
            Apply();
    }

    private string CurrentSize() => settings.Values.GetString(NotchSizeDescriptor.SizeKey) switch
    {
        NotchSizeDescriptor.Slim => NotchSizeDescriptor.Slim,
        NotchSizeDescriptor.Tucked => NotchSizeDescriptor.Tucked,
        _ => NotchSizeDescriptor.Normal,
    };

    /// <summary>Slim and tucked away both use the Slim surface. Tucked away also keeps alerts quiet, so only Critical ones show.</summary>
    private void Apply()
    {
        var size = CurrentSize();
        notch.SetFlag(NotchFlag.Slim, ownerId, size != NotchSizeDescriptor.Normal);
        notch.SetFlag(NotchFlag.Tucked, ownerId, size == NotchSizeDescriptor.Tucked);
        notch.SetFlag(NotchFlag.QuietAlerts, ownerId, size == NotchSizeDescriptor.Tucked);
    }
}
