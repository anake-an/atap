using Atap.Core.Alerts;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Core.Tests.Alerts;

/// <summary>The alert queue on a fake clock. Its one-shot timer fires inline when the clock moves.</summary>
public class AlertQueueTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly AlertQueue _queue;

    public AlertQueueTests()
    {
        _queue = new AlertQueue(_time, ui: null);
    }

    private static Alert Normal(string key, string title = "Notice") => new(key, AlertPriority.Normal, title);

    private static Alert High(string key, string title = "Reminder") => new(key, AlertPriority.High, title);

    private static Alert Critical(string key, string title = "Alarm") => new(key, AlertPriority.Critical, title);

    private static Alert Overlay(string key, double level) => new(key, AlertPriority.Overlay, "Volume") { Level = level };

    private void Advance(double seconds) => _time.Advance(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void Show_ThenDismiss_ClearsCurrent()
    {
        var sink = _queue.For("media");

        sink.Show(Normal("a"));
        Assert.Equal("media", _queue.Current!.Source);

        sink.Dismiss("a");
        Assert.Null(_queue.Current);
    }

    [Fact]
    public void HigherPriority_TakesOverAndTheOtherWaits()
    {
        _queue.For("a").Show(Normal("x", "Normal"));
        _queue.For("b").Show(Critical("y", "Critical"));

        Assert.Equal("Critical", _queue.Current!.Alert.Title);
        Assert.Equal("Normal", Assert.Single(_queue.Waiting).Alert.Title);
        _queue.For("b").Dismiss("y");
        Assert.Equal("Normal", _queue.Current!.Alert.Title);
    }

    [Fact]
    public void SamePriority_WaitsItsTurnInOrder()
    {
        _queue.For("a").Show(Normal("1", "First"));
        _queue.For("a").Show(Normal("2", "Second"));
        _queue.For("a").Show(Normal("3", "Third"));

        Assert.Equal("First", _queue.Current!.Alert.Title);
        Advance(4);
        Assert.Equal("Second", _queue.Current!.Alert.Title);
        Advance(4);
        Assert.Equal("Third", _queue.Current!.Alert.Title);
        Advance(4);
        Assert.Null(_queue.Current);
    }

    [Theory]
    [InlineData(AlertPriority.Overlay, 1.5)]
    [InlineData(AlertPriority.Normal, 4)]
    [InlineData(AlertPriority.High, 8)]
    [InlineData(AlertPriority.Critical, 60)]
    public void EachPriority_ShowsForItsTime(AlertPriority priority, double seconds)
    {
        _queue.For("a").Show(new Alert("k", priority, "Title"));

        Advance(seconds - 0.01);
        Assert.NotNull(_queue.Current);
        Advance(0.01);
        Assert.Null(_queue.Current);
    }

    [Fact]
    public void Critical_IsStickyWithASixtySecondCap()
    {
        _queue.For("alarm").Show(Critical("ring") with { Duration = TimeSpan.FromMinutes(10) });

        Advance(59);
        Assert.NotNull(_queue.Current);
        Advance(1);
        Assert.Null(_queue.Current);
    }

    [Theory]
    [InlineData(0.2, 1)]
    [InlineData(2.5, 2.5)]
    [InlineData(600, 60)]
    public void Duration_IsKeptBetweenOneAndSixtySeconds(double asked, double expected)
    {
        var alert = Normal("k") with { Duration = TimeSpan.FromSeconds(asked) };

        Assert.Equal(TimeSpan.FromSeconds(expected), AlertQueue.DurationOf(alert));
    }

    [Fact]
    public void Overlay_IsReplacedByKeyAndStaysAfterTheLastChange()
    {
        var sink = _queue.For("volume");

        sink.Show(Overlay("level", 0.2));
        Advance(1);
        sink.Show(Overlay("level", 0.4));
        Advance(1);
        sink.Show(Overlay("level", 0.6));

        Assert.Equal(0.6, _queue.Current!.Alert.Level);
        Assert.Empty(_queue.Waiting);
        Advance(1.49);
        Assert.NotNull(_queue.Current);
        Advance(0.01);
        Assert.Null(_queue.Current);
    }

    [Fact]
    public void Overlay_ReplacesAnotherOverlay()
    {
        _queue.For("volume").Show(Overlay("level", 0.2));
        _queue.For("brightness").Show(new Alert("level", AlertPriority.Overlay, "Brightness") { Level = 0.8 });

        Assert.Equal("Brightness", _queue.Current!.Alert.Title);
        Assert.Empty(_queue.Waiting);
    }

    [Fact]
    public void Overlay_IsDroppedWhileSomethingMoreUrgentShows()
    {
        _queue.For("a").Show(Normal("n"));
        _queue.For("volume").Show(Overlay("level", 0.5));

        Assert.Equal(AlertPriority.Normal, _queue.Current!.Alert.Priority);
        Assert.Empty(_queue.Waiting);
        Advance(4);
        Assert.Null(_queue.Current);
    }

    [Fact]
    public void Overlay_TakenOverByANotice_DoesNotComeBack()
    {
        _queue.For("volume").Show(Overlay("level", 0.5));
        _queue.For("a").Show(Normal("n"));

        Assert.Equal(AlertPriority.Normal, _queue.Current!.Alert.Priority);
        Advance(4);
        Assert.Null(_queue.Current);
    }

    [Fact]
    public void SameKey_ReplacesAWaitingAlert()
    {
        _queue.For("a").Show(High("busy"));
        _queue.For("b").Show(Normal("n", "Old"));
        _queue.For("b").Show(Normal("n", "New"));

        Assert.Equal("New", Assert.Single(_queue.Waiting).Alert.Title);
    }

    [Fact]
    public void Quiet_SendsHighAndNormalToMissed_DropsOverlays_AndShowsCritical()
    {
        _queue.SetQuiet("rule", true);

        _queue.For("a").Show(Normal("n", "Normal"));
        _queue.For("a").Show(High("h", "High"));
        _queue.For("a").Show(Overlay("o", 0.3));
        _queue.For("a").Show(Critical("c"));

        Assert.Equal(["High", "Normal"], _queue.Missed.Select(m => m.Alert.Title));
        Assert.Equal("Alarm", _queue.Current!.Alert.Title);
        Assert.Empty(_queue.Waiting);
    }

    [Fact]
    public void Quiet_AppliesWhenAWaitingAlertIsAboutToShow()
    {
        _queue.For("a").Show(Critical("c"));
        _queue.For("b").Show(Normal("n"));
        _queue.SetQuiet("rule", true);

        _queue.For("a").Dismiss("c");

        Assert.Null(_queue.Current);
        Assert.Single(_queue.Missed);
    }

    [Fact]
    public void Quiet_AnUpdateToTheAlertOnScreen_GoesToMissed()
    {
        _queue.For("a").Show(Normal("n"));
        _queue.SetQuiet("rule", true);

        _queue.For("a").Show(Normal("n"));

        Assert.Null(_queue.Current);
        Assert.Single(_queue.Missed);
    }

    [Fact]
    public void Quiet_AnUpdateToACriticalAlertOnScreen_StillShows()
    {
        _queue.For("a").Show(Critical("c"));
        _queue.SetQuiet("rule", true);

        _queue.For("a").Show(Critical("c"));

        Assert.NotNull(_queue.Current);
        Assert.Empty(_queue.Missed);
    }

    [Theory]
    [InlineData("push", "push")]
    [InlineData("push#2", "push")]
    [InlineData("clock", "clock")]
    public void TypeOf_IsThePartBeforeTheHash(string source, string type) => Assert.Equal(type, AlertQueue.TypeOf(source));

    [Fact]
    public void Quiet_EndingLetsNewAlertsShowAgain()
    {
        _queue.SetQuiet("rule", true);
        _queue.SetQuiet("rule", false);

        _queue.For("a").Show(Normal("n"));

        Assert.NotNull(_queue.Current);
        Assert.Empty(_queue.Missed);
    }

    [Fact]
    public void Hidden_CriticalWaitsAndShowsWhenTheNotchIsBack()
    {
        _queue.SetHidden("fullscreen", true);

        _queue.For("alarm").Show(Critical("ring"));
        Assert.Null(_queue.Current);
        Assert.Single(_queue.Waiting);

        // However long the game lasts, it waits.
        Advance(3600);
        _queue.SetHidden("fullscreen", false);
        Assert.Equal("Alarm", _queue.Current!.Alert.Title);
        Advance(59);
        Assert.NotNull(_queue.Current);
    }

    [Fact]
    public void Hidden_SendsHighAndNormalToMissedAndDropsOverlays()
    {
        _queue.SetHidden("fullscreen", true);

        _queue.For("a").Show(Normal("n"));
        _queue.For("a").Show(High("h"));
        _queue.For("a").Show(Overlay("o", 0.5));

        Assert.Equal(2, _queue.Missed.Count);
        Assert.Empty(_queue.Waiting);
        Assert.Null(_queue.Current);
    }

    [Fact]
    public void Hiding_MovesTheAlertOnScreenAwayAndBringsBackCritical()
    {
        _queue.For("a").Show(Normal("n", "Notice"));
        _queue.SetHidden("fullscreen", true);

        Assert.Null(_queue.Current);
        Assert.Equal("Notice", Assert.Single(_queue.Missed).Alert.Title);

        _queue.SetHidden("fullscreen", false);
        _queue.For("b").Show(Critical("c"));
        _queue.SetHidden("fullscreen", true);
        Assert.Null(_queue.Current);
        Assert.False(_queue.HasTimer);

        _queue.SetHidden("fullscreen", false);
        Assert.Equal("Alarm", _queue.Current!.Alert.Title);
    }

    [Fact]
    public void Hidden_StaysUntilEveryOwnerLetsGo()
    {
        _queue.SetHidden("fullscreen", true);
        _queue.SetHidden("rule", true);
        _queue.For("a").Show(Critical("c"));

        _queue.SetHidden("fullscreen", false);
        Assert.Null(_queue.Current);
        _queue.SetHidden("rule", false);
        Assert.NotNull(_queue.Current);
    }

    [Fact]
    public void Critical_SoundIsDueWhenItArrives_EvenWhileHidden()
    {
        var sounds = new List<ShownAlert>();
        _queue.SoundDue += (_, alert) => sounds.Add(alert);
        _queue.SetHidden("fullscreen", true);

        _queue.For("alarm").Show(Critical("ring") with { SoundKey = AlertSounds.Default });
        Assert.Single(sounds);

        _queue.SetHidden("fullscreen", false);
        Assert.NotNull(_queue.Current);
        Assert.Single(sounds);
    }

    [Fact]
    public void Sound_IsDueOnceWhenTheAlertFirstShows()
    {
        var sounds = new List<string>();
        _queue.SoundDue += (_, alert) => sounds.Add(alert.Alert.Key);

        _queue.For("a").Show(High("h") with { SoundKey = AlertSounds.Windows });
        _queue.For("b").Show(Normal("n") with { SoundKey = AlertSounds.Windows });
        Assert.Equal(["h"], sounds);

        Advance(8);
        Assert.Equal(["h", "n"], sounds);

        // Taken over and back again: no second sound.
        _queue.For("c").Show(Critical("c"));
        _queue.For("c").Dismiss("c");
        Assert.Equal(["h", "n"], sounds);
    }

    [Fact]
    public void Sound_IsNotDueForAnAlertThatGoesToMissed()
    {
        var sounds = 0;
        _queue.SoundDue += (_, _) => sounds++;
        _queue.SetQuiet("rule", true);

        _queue.For("a").Show(High("h") with { SoundKey = AlertSounds.Windows });

        Assert.Equal(0, sounds);
    }

    [Fact]
    public void PushSource_GetsOnePerFiveSecondsWithABurstOfThree()
    {
        var push = _queue.For(AlertQueue.PushSource);

        for (var i = 0; i < 4; i++)
            push.Show(Normal($"p{i}"));
        Assert.Equal(3, Count(_queue));

        Advance(4.9);
        push.Show(Normal("late"));
        Assert.False(_queue.Contains(AlertQueue.PushSource, "late"));

        Advance(0.1);
        push.Show(Normal("on-time"));
        Assert.True(_queue.Contains(AlertQueue.PushSource, "on-time"));

        // The bucket refills to three, never more.
        _queue.ClearMissed();
        Advance(600);
        for (var i = 0; i < 5; i++)
            push.Show(Normal($"q{i}"));
        Assert.Equal(3, Enumerable.Range(0, 5).Count(i => _queue.Contains(AlertQueue.PushSource, $"q{i}")));
    }

    [Fact]
    public void RateLimit_AppliesToNamedPushInstancesAndNotToOthers()
    {
        for (var i = 0; i < 5; i++)
        {
            _queue.For("push#scripts").Show(Normal($"s{i}"));
            _queue.For("battery").Show(Normal($"b{i}"));
        }

        Assert.Equal(3, Enumerable.Range(0, 5).Count(i => _queue.Contains("push#scripts", $"s{i}")));
        Assert.Equal(5, Enumerable.Range(0, 5).Count(i => _queue.Contains("battery", $"b{i}")));
    }

    [Fact]
    public void RateLimit_CanBeSetAndRemoved()
    {
        _queue.SetRateLimit("local-api", new AlertRateLimit(1, TimeSpan.FromSeconds(10)));
        _queue.For("local-api").Show(Normal("1"));
        _queue.For("local-api").Show(Normal("2"));
        Assert.False(_queue.Contains("local-api", "2"));

        _queue.SetRateLimit(AlertQueue.PushSource, null);
        for (var i = 0; i < 5; i++)
            _queue.For(AlertQueue.PushSource).Show(Normal($"p{i}"));
        Assert.Equal(5, Enumerable.Range(0, 5).Count(i => _queue.Contains(AlertQueue.PushSource, $"p{i}")));
    }

    [Fact]
    public void SystemQuiet_IsAskedOnlyWhenAnAlertIsAboutToShow()
    {
        var asked = 0;
        _queue.SystemQuiet = () =>
        {
            asked++;
            return false;
        };

        Advance(600);
        Assert.Equal(0, asked);

        _queue.For("a").Show(High("h"));
        Assert.Equal(1, asked);

        // Waiting behind it: not asked until its turn.
        _queue.For("a").Show(Normal("n"));
        Assert.Equal(1, asked);
        Advance(8);
        Assert.Equal(2, asked);

        // A Critical alert shows whatever Windows says.
        _queue.For("a").Show(Critical("c"));
        Assert.Equal(2, asked);

        // An update to the alert on screen is not a new alert.
        _queue.For("a").Show(Critical("c", "Alarm again"));
        Assert.Equal(2, asked);
    }

    [Fact]
    public void SystemQuiet_SendsAlertsToMissed()
    {
        _queue.SystemQuiet = () => true;

        _queue.For("a").Show(Normal("n"));
        _queue.For("a").Show(Overlay("o", 0.4));
        _queue.For("a").Show(Critical("c"));

        Assert.Single(_queue.Missed);
        Assert.Equal(AlertPriority.Critical, _queue.Current!.Alert.Priority);
    }

    [Fact]
    public void SystemQuiet_ThatThrows_CountsAsNotQuiet()
    {
        _queue.SystemQuiet = () => throw new InvalidOperationException("Broken.");

        _queue.For("a").Show(Normal("n"));

        Assert.NotNull(_queue.Current);
    }

    [Fact]
    public void Timer_ExistsOnlyWhileAnAlertShows()
    {
        Assert.False(_queue.HasTimer);

        _queue.For("a").Show(Normal("n"));
        Assert.True(_queue.HasTimer);

        Advance(4);
        Assert.False(_queue.HasTimer);
        Assert.Null(_queue.CurrentEndsAt);
    }

    [Fact]
    public void Missed_IsNewestFirstAndKeepsTwenty()
    {
        _queue.SetQuiet("rule", true);

        for (var i = 0; i < 25; i++)
        {
            _queue.For("a").Show(Normal($"n{i}", $"Notice {i}"));
            Advance(1);
        }

        Assert.Equal(AlertQueue.MissedLimit, _queue.Missed.Count);
        Assert.Equal("Notice 24", _queue.Missed[0].Alert.Title);
        Assert.Equal("Notice 5", _queue.Missed[^1].Alert.Title);
    }

    [Fact]
    public void Missed_KeepsOneEntryPerKey()
    {
        _queue.SetQuiet("rule", true);

        _queue.For("rain").Show(High("warning", "Rain"));
        _queue.For("rain").Show(High("warning", "Heavy rain"));

        Assert.Equal("Heavy rain", Assert.Single(_queue.Missed).Alert.Title);
    }

    [Fact]
    public void ClearMissed_EmptiesTheListAndTellsListeners()
    {
        var missedChanged = 0;
        var changed = 0;
        _queue.MissedChanged += (_, _) => missedChanged++;
        _queue.Changed += (_, _) => changed++;
        _queue.SetQuiet("rule", true);
        _queue.For("a").Show(Normal("n"));

        _queue.ClearMissed();
        _queue.ClearMissed();

        Assert.Empty(_queue.Missed);
        Assert.Equal(2, missedChanged);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Sink_OffersTheMissedList()
    {
        var sink = _queue.For("missed-alerts");
        var missed = Assert.IsAssignableFrom<IMissedAlerts>(sink);
        var raised = 0;
        missed.MissedChanged += (_, _) => raised++;
        _queue.SetQuiet("rule", true);

        _queue.For("a").Show(Normal("n"));
        Assert.Single(missed.Missed);
        Assert.Equal(1, raised);

        missed.ClearMissed();
        Assert.Empty(_queue.Missed);
    }

    [Fact]
    public void Keep_HoldsTheAlertPastItsTime_AndReleaseEndsItAtOnce()
    {
        _queue.For("prayer").Show(High("zohor") with { SoundKey = AlertSounds.Windows });
        var hold = _queue.Keep("prayer", "zohor");
        Assert.False(_queue.HasTimer);

        Advance(180);
        Assert.NotNull(_queue.Current);

        hold.Dispose();
        Assert.Null(_queue.Current);
        hold.Dispose();
    }

    [Fact]
    public void Keep_ReleasedEarly_EndsAtTheUsualTime()
    {
        _queue.For("a").Show(High("h"));
        var hold = _queue.Keep("a", "h");
        Advance(2);

        hold.Dispose();
        Assert.True(_queue.HasTimer);
        Advance(5.99);
        Assert.NotNull(_queue.Current);
        Advance(0.01);
        Assert.Null(_queue.Current);
    }

    [Fact]
    public void Dismissed_IsRaisedForSourceAndUserDismissals_NotForTimeouts()
    {
        var dismissed = new List<string>();
        _queue.Dismissed += (_, alert) => dismissed.Add(alert.Alert.Key);

        _queue.For("a").Show(Normal("timeout"));
        Advance(4);
        _queue.For("a").Show(Normal("source"));
        _queue.For("a").Dismiss("source");
        _queue.For("a").Show(Normal("user"));
        _queue.DismissCurrent();

        Assert.Equal(["source", "user"], dismissed);
    }

    [Fact]
    public void DismissCurrent_ShowsTheNextOne()
    {
        _queue.For("a").Show(High("h"));
        _queue.For("a").Show(Normal("n"));

        _queue.DismissCurrent();

        Assert.Equal("n", _queue.Current!.Alert.Key);
    }

    [Fact]
    public void Events_AreRaisedAfterTheChangeIsComplete()
    {
        ShownAlert? seen = null;
        _queue.Changed += (_, _) => seen = _queue.Current;

        _queue.For("a").Show(Normal("n"));

        Assert.Same(_queue.Current, seen);
    }

    [Fact]
    public void Timer_RunsOnTheUiContext()
    {
        var ui = new QueueingContext();
        var queue = new AlertQueue(_time, ui);
        queue.For("a").Show(Normal("n"));

        Advance(4);
        Assert.NotNull(queue.Current);
        Assert.Equal(1, ui.Pending);

        ui.RunAll();
        Assert.Null(queue.Current);
    }

    [Fact]
    public void Timer_PostedForACancelledAlert_DoesNothing()
    {
        var ui = new QueueingContext();
        var queue = new AlertQueue(_time, ui);
        queue.For("a").Show(Normal("first"));
        Advance(4);

        // The posted end arrives after another alert took the screen.
        queue.For("a").Dismiss("first");
        queue.For("a").Show(Normal("second"));
        ui.RunAll();

        Assert.Equal("second", queue.Current!.Alert.Key);
    }

    private static int Count(AlertQueue queue) => (queue.Current is null ? 0 : 1) + queue.Waiting.Count;

    /// <summary>Holds posted work until the test runs it, like a busy UI thread.</summary>
    private sealed class QueueingContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _work = new();

        public int Pending => _work.Count;

        public override void Post(SendOrPostCallback d, object? state) => _work.Enqueue((d, state));

        public void RunAll()
        {
            while (_work.TryDequeue(out var item))
                item.Callback(item.State);
        }
    }
}
