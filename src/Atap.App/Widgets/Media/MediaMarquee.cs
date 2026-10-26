using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Atap.Widgets.Media;

/// <summary>
/// One line of text in the small notch. When it does not fit, it scrolls slowly sideways: a pause at the start, a
/// calm scroll to the end, a pause, then back to the start. It scrolls only while it is on screen and motion is
/// allowed. With less motion or battery saver on (both set <see cref="IsCalm"/>), or Windows' animation effects off,
/// it stands still and the text ends with an ellipsis, so give it a TextBlock with TextTrimming set.
/// The animation is made in code at a low frame rate, so nothing in XAML repeats forever.
/// </summary>
public sealed class MediaMarquee : Decorator
{
    public static readonly DependencyProperty IsCalmProperty = DependencyProperty.Register(
        nameof(IsCalm), typeof(bool), typeof(MediaMarquee), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Pixels a second. A calm reading pace, and the scroll never goes faster.</summary>
    internal const double Speed = 30;

    private const int FrameRate = 30;

    private static readonly TimeSpan StartPause = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EndPause = TimeSpan.FromSeconds(1.5);

    private readonly TranslateTransform _shift = new();
    private double _fullWidth;
    private bool _canScroll;
    // How far the running scroll goes, 0 while it stands still.
    private double _distance;

    public MediaMarquee()
    {
        ClipToBounds = true;
        Loaded += (_, _) => InvalidateMeasure();
        Unloaded += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => Refresh();
    }

    /// <summary>Less motion is on, from the setting or battery saver. The notch's own flag.</summary>
    public bool IsCalm
    {
        get => (bool)GetValue(IsCalmProperty);
        set => SetValue(IsCalmProperty, value);
    }

    /// <summary>True while the text scrolls. Tests read it.</summary>
    internal bool IsScrolling => _distance > 0;

    /// <summary>The rule, pure: it scrolls only when the text does not fit, it is shown and motion is allowed.</summary>
    internal static bool ShouldScroll(double overflow, bool shown, bool calm, bool windowsAnimates) =>
        overflow > 1 && shown && !calm && windowsAnimates;

    /// <summary>How long one round takes: the pause at the start, the scroll at <see cref="Speed"/>, the pause at the end.</summary>
    internal static TimeSpan RoundFor(double distance) => StartPause + TimeSpan.FromSeconds(distance / Speed) + EndPause;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not { } child)
            return default;
        if (!ReferenceEquals(child.RenderTransform, _shift))
            child.RenderTransform = _shift;

        child.Measure(new Size(double.PositiveInfinity, constraint.Height));
        var full = child.DesiredSize;
        _fullWidth = full.Width;
        var overflow = double.IsInfinity(constraint.Width) ? 0 : full.Width - constraint.Width;
        _canScroll = ShouldScroll(overflow, IsLoaded && IsVisible, IsCalm, SystemParameters.ClientAreaAnimation);
        // Standing still, the text is measured at the room there is, so it ends with an ellipsis.
        if (!_canScroll && overflow > 0)
            child.Measure(constraint);
        return new Size(Math.Min(full.Width, constraint.Width), full.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is not { } child)
            return arrangeSize;
        var scroll = _canScroll && _fullWidth > arrangeSize.Width + 1;
        child.Arrange(new Rect(0, 0, scroll ? _fullWidth : arrangeSize.Width, arrangeSize.Height));
        Scroll(scroll ? _fullWidth - arrangeSize.Width : 0);
        return arrangeSize;
    }

    private void Refresh()
    {
        if (!IsLoaded || !IsVisible)
            Scroll(0);
        InvalidateMeasure();
    }

    private void Scroll(double distance)
    {
        if (Math.Abs(distance - _distance) < 0.5)
            return;
        _distance = distance;
        if (distance <= 0)
        {
            _shift.BeginAnimation(TranslateTransform.XProperty, null);
            _shift.X = 0;
            _distance = 0;
            return;
        }
        var startsScroll = KeyTime.FromTimeSpan(StartPause);
        var endsScroll = KeyTime.FromTimeSpan(StartPause + TimeSpan.FromSeconds(distance / Speed));
        var round = new DoubleAnimationUsingKeyFrames
        {
            Duration = RoundFor(distance),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        round.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        round.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, startsScroll));
        // Linear, so it never runs faster than the calm pace.
        round.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, endsScroll));
        round.KeyFrames.Add(new DiscreteDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(RoundFor(distance))));
        Timeline.SetDesiredFrameRate(round, FrameRate);
        _shift.BeginAnimation(TranslateTransform.XProperty, round);
    }
}
