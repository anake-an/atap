using Atap.Core.Alerts;
using Atap.Core.Widgets;

namespace Atap.Widgets.MissedAlerts;

/// <summary>
/// Lists the Missed alerts and clears them. It reads the list through its alert sink, follows changes
/// to it, and has no timer. Its indicator is on while something was missed.
/// </summary>
public sealed class MissedAlertsWidget(WidgetContext context) : WidgetBase(context), IIndicator
{
    private readonly MissedAlertsViewModel _viewModel = new(context.Text, context.Alerts as IMissedAlerts);
    private readonly IMissedAlerts? _missed = context.Alerts as IMissedAlerts;
    private bool _started;
    private bool _indicatorOn;

    public override object ViewModel => _viewModel;

    public bool IndicatorOn => _indicatorOn;

    public string IndicatorIconKey => "bell";

    public string IndicatorNameKey => "missed-alerts.indicator";

    public event EventHandler? IndicatorChanged;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        if (_started)
            return Task.CompletedTask;
        _started = true;
        if (_missed is not null)
            _missed.MissedChanged += OnMissedChanged;
        Refresh();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        if (!_started)
            return Task.CompletedTask;
        _started = false;
        if (_missed is not null)
            _missed.MissedChanged -= OnMissedChanged;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    // Raised on the UI thread.
    private void OnMissedChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _viewModel.Refresh();
        var on = _missed is { Missed.Count: > 0 };
        if (on == _indicatorOn)
            return;
        _indicatorOn = on;
        IndicatorChanged?.Invoke(this, EventArgs.Empty);
    }
}
