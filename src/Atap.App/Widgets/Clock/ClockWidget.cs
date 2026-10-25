using System.Globalization;
using Atap.Core.Widgets;

namespace Atap.Widgets.Clock;

/// <summary>
/// Shows the time. Refreshes on the shared minute tick, which the scheduler also runs at once when the
/// Windows clock or time zone changes and when the PC wakes, so the clock owns no timer or system events.
/// </summary>
public sealed class ClockWidget(WidgetContext context) : WidgetBase(context)
{
    private readonly ClockViewModel _viewModel = new(context.Text);
    private IDisposable? _tick;
    private bool _use24Hour = true;

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _use24Hour = settings.GetBool(ClockDescriptor.Use24HourKey);
        Refresh();
        _tick = Context.Scheduler.EveryMinute(Refresh);
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        _tick?.Dispose();
        _tick = null;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void Refresh()
    {
        var now = Context.Time.GetLocalNow();
        _viewModel.Time = now.ToString(_use24Hour ? "HH:mm" : "h:mm", CultureInfo.CurrentCulture);
    }
}
