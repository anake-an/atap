using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Atap.Tests.Infrastructure;
using Atap.Ui.Controls;

namespace Atap.Tests.Ui.Controls;

/// <summary>Rules every UI kit control keeps: a style in the app, a screen reader name, the text scale, and no animation.</summary>
public class KitGuardTests
{
    public static TheoryData<string> Controls() => new(nameof(IconButton), nameof(ProgressPill), nameof(StatusDot), nameof(WidgetCard));

    [Theory]
    [MemberData(nameof(Controls))]
    public void EveryControl_HasItsStyleInTheAppResources(string name) => Sta.Run(() =>
    {
        var control = Create(name);
        var style = Assert.IsType<Style>(Application.Current.TryFindResource(control.GetType()));

        Assert.Contains(style.Setters.OfType<Setter>(), s => s.Property == Control.TemplateProperty);
    });

    [Theory]
    [MemberData(nameof(Controls))]
    public void EveryControl_HasAScreenReaderName(string name) => Sta.Run(() =>
    {
        var control = Create(name);
        using var host = new KitHost(control);

        Assert.False(string.IsNullOrWhiteSpace(KitHost.Peer(control).GetName()), $"{name} has no screen reader name.");
    });

    [Theory]
    [MemberData(nameof(Controls))]
    public void EveryControl_HasAFocusRingThatLoads(string name) => Sta.Run(() =>
    {
        var control = Create(name);
        using var host = new KitHost(control);
        var ring = Assert.IsType<Style>(control.FocusVisualStyle);

        // The adorner WPF draws for keyboard focus is a plain Control with this style.
        var adorner = new Control { Style = ring };
        using var ringHost = new KitHost(adorner);
        Assert.NotNull(adorner.Template);
        Assert.Equal(1, VisualTreeHelper.GetChildrenCount(adorner));
    });

    [Theory]
    [MemberData(nameof(Controls))]
    public void EveryControl_GrowsWithTheTextScale(string name) => Sta.Run(() =>
    {
        // The app applies the text scale as a LayoutTransform on the content root, as here at 150 %.
        var plain = Create(name);
        var scaled = Create(name);
        var root = new Border { Child = scaled, LayoutTransform = new ScaleTransform(1.5, 1.5) };
        using var plainHost = new KitHost(new StackPanel { Children = { plain } }, 600, 400);
        using var scaledHost = new KitHost(new StackPanel { Children = { root } }, 600, 400);

        Assert.True(plain.DesiredSize.Width > 0 && plain.DesiredSize.Height > 0, $"{name} has no size.");
        Assert.Equal(plain.DesiredSize.Width * 1.5, root.DesiredSize.Width, 1);
        Assert.Equal(plain.DesiredSize.Height * 1.5, root.DesiredSize.Height, 1);
    });

    [Fact]
    public void KitXaml_HasNoAnimation()
    {
        // Stricter than the idle guard: the kit has no storyboards at all, so nothing can wake the notch.
        var folder = Path.Combine(RepoPaths.App, "Ui", "Controls");
        var files = RepoPaths.Files(folder, "*.xaml").ToList();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var word in new[] { "Storyboard", "RepeatBehavior", "DoubleAnimation", "ColorAnimation", "VisualStateManager" })
                Assert.False(text.Contains(word, StringComparison.Ordinal), $"{RepoPaths.Relative(file)} has {word}.");
        }
    }

    [Fact]
    public void Contrast_FollowsWindowsUnlessForced() => Sta.Run(() =>
    {
        var changes = new List<string?>();
        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => changes.Add(e.PropertyName);
        Contrast.Current.PropertyChanged += OnChanged;
        try
        {
            Assert.Equal(SystemParameters.HighContrast, Contrast.Current.IsHigh);
            using (KitHost.HighContrast())
                Assert.True(Contrast.Current.IsHigh);
            Assert.Equal(SystemParameters.HighContrast, Contrast.Current.IsHigh);
            Assert.Equal(new string?[] { nameof(Contrast.IsHigh), nameof(Contrast.IsHigh) }, changes);
        }
        finally
        {
            Contrast.Current.PropertyChanged -= OnChanged;
        }
    });

    private static Control Create(string name) => name switch
    {
        nameof(IconButton) => new IconButton { Icon = KitHost.Icon("icon.play"), Label = "Play" },
        nameof(ProgressPill) => new ProgressPill { Width = 120, Value = 30 },
        nameof(StatusDot) => new StatusDot { Level = Atap.Core.Custom.ValueLevel.Warning },
        nameof(WidgetCard) => new WidgetCard { Header = "Weather", Icon = KitHost.Icon("icon.cloud"), Content = new TextBlock { Text = "31°" } },
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
