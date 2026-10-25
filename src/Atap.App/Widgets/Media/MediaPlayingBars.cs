using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Atap.Widgets.Media;

/// <summary>
/// The small "playing" bars beside the album art. They bounce only while music plays, the bars are on screen
/// and motion is allowed: never while paused, with less motion or battery saver on (both set <see cref="IsCalm"/>),
/// with Windows' animation effects off, or once the notch shows something else. Then they stand still.
/// The animation runs at a low frame rate and is made in code, so nothing in XAML repeats forever.
/// </summary>
public sealed class MediaPlayingBars : StackPanel
{
    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(MediaPlayingBars), new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty IsCalmProperty = DependencyProperty.Register(
        nameof(IsCalm), typeof(bool), typeof(MediaPlayingBars), new PropertyMetadata(false, OnChanged));

    // Each bar's resting height share and its own pace, so they do not move in step.
    private static readonly (double Resting, double Seconds)[] Bars = [(0.55, 0.45), (1, 0.6), (0.7, 0.5)];

    private const int FrameRate = 24;

    private readonly List<ScaleTransform> _scales = [];
    private bool _running;

    public MediaPlayingBars()
    {
        Orientation = Orientation.Horizontal;
        foreach (var (rest, _) in Bars)
        {
            var scale = new ScaleTransform(1, rest);
            _scales.Add(scale);
            var bar = new Rectangle
            {
                Width = 3,
                Margin = new Thickness(1, 0, 1, 0),
                RadiusX = 1.5,
                RadiusY = 1.5,
                VerticalAlignment = VerticalAlignment.Stretch,
                RenderTransformOrigin = new Point(0.5, 1),
                RenderTransform = scale,
            };
            bar.SetResourceReference(Shape.FillProperty, "MediaAccentBrush");
            Children.Add(bar);
        }
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => Update();
        IsVisibleChanged += (_, _) => Update();
    }

    /// <summary>True while the song plays.</summary>
    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    /// <summary>Less motion is on, from the setting or battery saver. The notch's own flag.</summary>
    public bool IsCalm
    {
        get => (bool)GetValue(IsCalmProperty);
        set => SetValue(IsCalmProperty, value);
    }

    /// <summary>True while the bars move. Tests read it.</summary>
    internal bool IsAnimating => _running;

    /// <summary>The rule, pure: they move only while playing, shown and allowed to.</summary>
    internal static bool ShouldMove(bool playing, bool shown, bool calm, bool windowsAnimates) =>
        playing && shown && !calm && windowsAnimates;

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MediaPlayingBars)d).Update();

    private void Update()
    {
        var move = ShouldMove(IsPlaying, IsLoaded && IsVisible, IsCalm, SystemParameters.ClientAreaAnimation);
        if (move == _running)
            return;
        _running = move;
        for (var i = 0; i < _scales.Count; i++)
        {
            if (!move)
            {
                _scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                continue;
            }
            var bounce = new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(Bars[i].Seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(bounce, FrameRate);
            _scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
        }
    }
}
