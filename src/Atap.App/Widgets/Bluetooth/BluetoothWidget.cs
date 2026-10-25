using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Platform.Bluetooth;
using Atap.Widgets.Bluetooth;

[assembly: AtapWidget(typeof(BluetoothDescriptor))]

namespace Atap.Widgets.Bluetooth;

/// <summary>Connected Bluetooth devices and their battery. Constants only.</summary>
public sealed class BluetoothDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "bluetooth";

    public string TypeId => TypeIdValue;

    public string NameKey => "bluetooth.name";

    public string DescriptionKey => "bluetooth.description";

    public WidgetGroup Group => WidgetGroup.System;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Bluetooth/BluetoothViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new BluetoothWidget(context, new WindowsBluetoothDevices());
}

/// <summary>Reads the devices when shown and every 30 seconds while open. Nothing runs while hidden or stopped.</summary>
public sealed class BluetoothWidget : WidgetBase
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30);

    private readonly IBluetoothDevices _devices;
    private readonly BluetoothViewModel _viewModel;
    private IDisposable? _tick;
    private CancellationTokenSource? _stopping;

    internal BluetoothWidget(WidgetContext context, IBluetoothDevices devices)
        : base(context)
    {
        _devices = devices;
        _viewModel = new BluetoothViewModel(context.Text);
    }

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _stopping = new CancellationTokenSource();
        Context.Visibility.Changed += OnVisibilityChanged;
        OnVisibilityChanged(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        _tick?.Dispose();
        _tick = null;
        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        var shown = Context.Visibility.Value is WidgetVisibility.Expanded or WidgetVisibility.Panel;
        if (shown && _tick is null)
        {
            _ = RefreshAsync();
            _tick = Context.Scheduler.Every(RefreshEvery, () => _ = RefreshAsync(), new ScheduleOptions(TimeSpan.FromSeconds(3), true));
        }
        else if (!shown && _tick is not null)
        {
            _tick.Dispose();
            _tick = null;
        }
    }

    private async Task RefreshAsync()
    {
        if (_stopping is not { } stopping)
            return;
        try
        {
            var devices = await _devices.ConnectedAsync(stopping.Token);
            if (!stopping.IsCancellationRequested)
                _viewModel.Show(devices);
        }
        catch (OperationCanceledException)
        {
            // Stopped while reading.
        }
    }
}

/// <summary>"WH-1000XM4", "80%" or "Battery unknown".</summary>
public sealed record DeviceRow(string Name, string Battery, double Level, bool HasLevel, ValueLevel Tone);

public sealed class BluetoothViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private IReadOnlyList<BluetoothGadget> _devices = [];

    public BluetoothViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<DeviceRow> Rows { get; private set; } = [];

    public bool HasNone => Rows.Count == 0;

    public string AccessibleSummary => _text.Format("bluetooth.accessible", ("devices", string.Join(". ", Rows.Select(r => $"{r.Name} {r.Battery}"))));

    internal void Show(IReadOnlyList<BluetoothGadget> devices)
    {
        _devices = devices;
        Rows = devices.Select(d => new DeviceRow(
            d.Name,
            d.Battery is { } b ? b.ToString(CultureInfo.CurrentCulture) + "%" : _text["bluetooth.unknown"],
            d.Battery ?? 0,
            d.Battery is not null,
            d.Battery is { } level && level <= 15 ? ValueLevel.Critical : d.Battery is { } low && low <= 30 ? ValueLevel.Warning : ValueLevel.Normal)).ToList();
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => Show(_devices);
}
