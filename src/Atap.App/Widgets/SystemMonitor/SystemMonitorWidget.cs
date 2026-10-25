using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Metrics;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Platform.Metrics;
using Atap.Widgets.SystemMonitor;

[assembly: AtapWidget(typeof(SystemMonitorDescriptor))]

namespace Atap.Widgets.SystemMonitor;

/// <summary>CPU, memory and system drive. Constants only.</summary>
public sealed class SystemMonitorDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "system-monitor";

    public string TypeId => TypeIdValue;

    public string NameKey => "sysmon.name";

    public string DescriptionKey => "sysmon.description";

    public WidgetGroup Group => WidgetGroup.System;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/SystemMonitor/SystemMonitorViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new SystemMonitorWidget(context, new WindowsSystemMetrics());
}

/// <summary>Samples every 2 seconds while open, never while hidden. CPU needs two samples, so the first shows "...".</summary>
public sealed class SystemMonitorWidget : WidgetBase
{
    public static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(2);

    private readonly SystemMonitorViewModel _viewModel;
    private readonly ISystemMetrics _metrics;
    private IDisposable? _tick;
    private CpuTimes? _lastCpu;

    internal SystemMonitorWidget(WidgetContext context, ISystemMetrics metrics)
        : base(context)
    {
        _metrics = metrics;
        _viewModel = new SystemMonitorViewModel(context.Text);
    }

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        Context.Visibility.Changed += OnVisibilityChanged;
        OnVisibilityChanged(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        _tick?.Dispose();
        _tick = null;
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
            _lastCpu = null;
            Sample();
            _tick = Context.Scheduler.Every(SampleEvery, Sample);
        }
        else if (!shown && _tick is not null)
        {
            _tick.Dispose();
            _tick = null;
        }
    }

    private void Sample()
    {
        var reading = _metrics.Read();
        var cpu = SystemUsage.CpuPercent(_lastCpu, reading.Cpu);
        _lastCpu = reading.Cpu;
        _viewModel.Show(cpu, reading.Memory, reading.Disk);
    }
}

/// <summary>One row: "CPU", "23%", its bar and level, and a detail such as "9.8 of 16 GB".</summary>
public sealed record UsageRow(string Name, string Value, double Percent, ValueLevel Level, string Detail);

public sealed class SystemMonitorViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private double? _cpu;
    private MemoryReading? _memory;
    private DiskReading? _disk;

    public SystemMonitorViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<UsageRow> Rows { get; private set; } = [];

    public string AccessibleSummary => _text.Format("sysmon.accessible", ("rows", string.Join(". ", Rows.Select(r => $"{r.Name} {r.Value}"))));

    public void Show(double? cpu, MemoryReading? memory, DiskReading? disk)
    {
        _cpu = cpu;
        _memory = memory;
        _disk = disk;
        Build();
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void Build()
    {
        var culture = CultureInfo.CurrentCulture;
        var rows = new List<UsageRow>
        {
            new(_text["sysmon.cpu"], _cpu is { } c ? Percent(c) : "...", _cpu ?? 0, SystemUsage.LevelOf(_cpu), string.Empty),
        };
        if (_memory is { } m)
        {
            rows.Add(new(_text["sysmon.memory"], Percent(m.Percent), m.Percent, SystemUsage.LevelOf(m.Percent),
                _text.Format("sysmon.of", ("used", SystemUsage.Size(m.Used, culture)), ("total", SystemUsage.Size(m.Total, culture)))));
        }
        if (_disk is { } d)
        {
            rows.Add(new(_text.Format("sysmon.disk", ("drive", d.Name)), Percent(d.Percent), d.Percent, SystemUsage.LevelOf(d.Percent),
                _text.Format("sysmon.free", ("free", SystemUsage.Size(d.Free, culture)))));
        }
        Rows = rows;
        OnPropertyChanged(string.Empty);
    }

    private static string Percent(double value) => Math.Round(value).ToString(CultureInfo.CurrentCulture) + "%";

    private void OnLanguageChanged(object? sender, EventArgs e) => Build();
}
