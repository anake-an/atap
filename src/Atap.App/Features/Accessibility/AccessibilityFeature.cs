using Atap.Composition;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.Accessibility;
using Atap.Notch;

[assembly: AtapFeature(typeof(AccessibilityDescriptor))]

namespace Atap.Features.Accessibility;

/// <summary>
/// Text size and less motion for the notch, on the Accessibility page. Text size follows Windows' "Make text
/// bigger" unless a size is picked. Runs in safe mode too, since people need it there as well.
/// </summary>
internal sealed class AccessibilityDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "accessibility";
    public const string TextSizeKey = "notch.textSize";
    public const string LessMotionKey = "notch.lessMotion";
    public const string MatchWindows = "windows";
    public const string Normal = "normal";
    public const string Large = "large";
    public const string Larger = "larger";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(TextSizeKey, "accessibility.textSize", SettingType.Choice, MatchWindows, [MatchWindows, Normal, Large, Larger])
        {
            SectionId = SettingsSections.Accessibility,
            DescriptionKey = "accessibility.textSizeHint",
        },
        new(LessMotionKey, "accessibility.lessMotion", SettingType.Bool, false)
        {
            SectionId = SettingsSections.Accessibility,
            DescriptionKey = "accessibility.lessMotionHint",
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => true;

    public IAppFeature Create(FeatureContext context) =>
        new AccessibilityFeature(context.Settings, context.Notch, new WindowsTextScale(context.Ui), FeatureId);
}

/// <summary>Windows' own text size, from "Make text bigger" in Settings, 1 to 2.25.</summary>
internal interface ITextScaleSource : IDisposable
{
    double Factor { get; }

    /// <summary>Raised on the UI thread when the Windows text size changes.</summary>
    event EventHandler? Changed;
}

/// <summary>Turns the two settings into the notch's look. Holds no timer.</summary>
internal sealed class AccessibilityFeature(IAppSettings settings, INotchControl notch, ITextScaleSource windows, string ownerId) : IAppFeature
{
    public static readonly IReadOnlyDictionary<string, double> Scales = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        [AccessibilityDescriptor.Normal] = 1,
        [AccessibilityDescriptor.Large] = 1.25,
        [AccessibilityDescriptor.Larger] = 1.5,
    };

    public Task StartAsync()
    {
        settings.Changed += OnSettingsChanged;
        windows.Changed += OnWindowsChanged;
        Apply();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        settings.Changed -= OnSettingsChanged;
        windows.Changed -= OnWindowsChanged;
        windows.Dispose();
        // The feature host puts the usual look back once the feature has stopped.
        return ValueTask.CompletedTask;
    }

    /// <summary>The text size in use: the one picked, or Windows' own.</summary>
    public double TextScale =>
        Scales.TryGetValue(settings.Values.GetString(AccessibilityDescriptor.TextSizeKey), out var scale)
            ? scale
            : Math.Clamp(windows.Factor, 1, 2.25);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(AccessibilityDescriptor.TextSizeKey) || e.Keys.Contains(AccessibilityDescriptor.LessMotionKey))
            Apply();
    }

    private void OnWindowsChanged(object? sender, EventArgs e) => Apply();

    private void Apply() =>
        notch.SetLook(new NotchLook(TextScale, settings.Values.GetBool(AccessibilityDescriptor.LessMotionKey)), ownerId);
}

/// <summary>Reads Windows' text size and listens for changes. Falls back to 1 where Windows cannot say.</summary>
internal sealed class WindowsTextScale : ITextScaleSource
{
    private readonly SynchronizationContext _ui;
    private readonly Windows.UI.ViewManagement.UISettings? _settings;

    public WindowsTextScale(SynchronizationContext ui)
    {
        _ui = ui;
        try
        {
            _settings = new Windows.UI.ViewManagement.UISettings();
            _settings.TextScaleFactorChanged += OnChanged;
        }
        catch (Exception)
        {
            // Older or trimmed Windows without the WinRT settings: keep the usual size.
            _settings = null;
        }
    }

    public event EventHandler? Changed;

    public double Factor
    {
        get
        {
            try
            {
                return _settings?.TextScaleFactor ?? 1;
            }
            catch (Exception)
            {
                return 1;
            }
        }
    }

    public void Dispose()
    {
        if (_settings is not null)
            _settings.TextScaleFactorChanged -= OnChanged;
    }

    // Windows raises this on a worker thread.
    private void OnChanged(Windows.UI.ViewManagement.UISettings sender, object args) =>
        _ui.Post(_ => Changed?.Invoke(this, EventArgs.Empty), null);
}
