using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Atap.Composition;
using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Notch;
using Atap.Core.Widgets;
using Atap.Features.Accessibility;
using Atap.Features.Panel;
using Atap.Notch;
using Atap.Notch.Automation;
using Atap.Tests.Infrastructure;
using Atap.Tests.Ui.Controls;
using Atap.Ui.Controls;
using Atap.Ui.Theme;

namespace Atap.Tests.Notch;

/// <summary>Keyboard use in the panel, screen reader names, high contrast, text size and less motion.</summary>
public sealed class NotchAccessTests : IAsyncLifetime
{
    private readonly TestServices _services = new();
    private readonly NotchControl _notch = new();
    private readonly FakeTextScale _windows = new() { Factor = 1.5 };
    private AccessibilityFeature _feature = null!;

    public async Task InitializeAsync()
    {
        _services.Settings.Load();
        _services.Settings.Register(new AccessibilityDescriptor().Settings);
        _feature = new AccessibilityFeature(_services.Settings, _notch, _windows, AccessibilityDescriptor.FeatureId);
        await _feature.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _feature.DisposeAsync();
        _services.Dispose();
    }

    [Fact]
    public void TextSize_MatchesWindows_ByDefault_AndFollowsItsChanges()
    {
        Assert.Equal(1.5, _notch.Look.TextScale);

        _windows.Factor = 2;
        _windows.Raise();

        Assert.Equal(2, _notch.Look.TextScale);
    }

    [Theory]
    [InlineData(AccessibilityDescriptor.Normal, 1)]
    [InlineData(AccessibilityDescriptor.Large, 1.25)]
    [InlineData(AccessibilityDescriptor.Larger, 1.5)]
    public void APickedTextSize_WinsOverWindows(string size, double scale)
    {
        _services.Settings.Set(AccessibilityDescriptor.TextSizeKey, size);
        _windows.Factor = 2;
        _windows.Raise();

        Assert.Equal(scale, _notch.Look.TextScale);
    }

    [Fact]
    public void LessMotion_ReachesTheNotch()
    {
        Assert.False(_notch.Look.LessMotion);

        _services.Settings.Set(AccessibilityDescriptor.LessMotionKey, true);

        Assert.True(_notch.Look.LessMotion);
    }

    [Fact]
    public async Task Stopping_PutsTheUsualLookBack_AndLetsGoOfWindows()
    {
        await _feature.DisposeAsync();
        _notch.ClearOwner(AccessibilityDescriptor.FeatureId);

        Assert.Equal(NotchLook.Default, _notch.Look);
        Assert.True(_windows.Disposed);
    }

    [Fact]
    public void Accessibility_RunsInSafeModeToo()
    {
        Assert.True(new AccessibilityDescriptor().IsEnabled(SettingValues.Empty, AppMode.Safe));
    }

    [Fact]
    public void SetLook_RaisesChangedOnlyForANewLook()
    {
        var control = new NotchControl();
        var changes = 0;
        control.Changed += (_, _) => changes++;

        control.SetLook(new NotchLook(1.25, false), "a");
        control.SetLook(new NotchLook(1.25, false), "a");
        control.ClearOwner("someone else");

        Assert.Equal(1, changes);
        Assert.Equal(1.25, control.Look.TextScale);
    }

    [Fact]
    public async Task ANewTextSize_ResizesTheNotch()
    {
        await using var widgets = _services.CreateWidgetHost(WidgetCatalog.FromDescriptors([]));
        _services.Layout.Load();
        using var model = new NotchViewModel(_services.Layout, widgets, _notch, new ForegroundStub());
        var changes = 0;
        model.ContentChanged += (_, _) => changes++;
        Assert.Equal(1.5, model.TextScale);

        _services.Settings.Set(AccessibilityDescriptor.TextSizeKey, AccessibilityDescriptor.Normal);
        _services.Settings.Set(AccessibilityDescriptor.LessMotionKey, true);

        Assert.Equal(1, model.TextScale);
        Assert.True(model.LessMotion);
        // Less motion alone changes nothing on screen.
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task AClickOnAnotherApp_ClosesThePanel()
    {
        var panel = new PanelFeature(_notch, _services.Commands, PanelDescriptor.FeatureId);
        await panel.StartAsync();
        panel.Toggle();

        Assert.False(_notch.Offer(new NotchInput(NotchInputKind.Dismiss, NotchSurface.Expanded)));
        Assert.True(_notch.Offer(new NotchInput(NotchInputKind.Dismiss, NotchSurface.Panel)));
        Assert.False(_notch.IsSet(NotchFlag.PanelOpen));
        await panel.DisposeAsync();
    }

    [Fact]
    public void TheKeyboard_GoesToTheNotchThePointerWasLastOn()
    {
        var focus = new NotchFocus();
        object left = new(), right = new();

        focus.PointerEntered(right);

        Assert.False(focus.TryClaim(left));
        Assert.True(focus.TryClaim(right));
    }

    [Fact]
    public void WithNoPointer_TheFirstNotchToAskTakesTheKeyboard()
    {
        var focus = new NotchFocus();
        object left = new(), right = new();

        Assert.True(focus.TryClaim(left));
        Assert.False(focus.TryClaim(right));

        focus.WindowClosed(left);

        Assert.True(focus.TryClaim(right));
    }

    [Fact]
    public void Slots_AndButtons_TakeFocusOnlyWhileThePanelHasTheKeyboard() => Sta.Run(() =>
    {
        var slots = new WidgetSlots { ItemsSource = new[] { Placement(new Summarised("Now playing: Lagu")) } };
        var button = new Button { Style = (Style)Application.Current.FindResource("IconButton") };
        var root = new StackPanel { Children = { slots, button } };
        using var host = new KitHost(root);
        var slot = Assert.IsType<WidgetSlot>(slots.ItemContainerGenerator.ContainerFromIndex(0));

        Assert.False(slot.Focusable);
        Assert.False(button.Focusable);

        KeyboardMode.SetIsOn(root, true);

        Assert.True(slot.Focusable);
        Assert.True(button.Focusable);
    });

    [Fact]
    public void ASlot_IsNamedByItsSummary_OrElseByTheWidgetName() => Sta.Run(() =>
    {
        var slots = new WidgetSlots
        {
            ItemsSource = new[]
            {
                Placement(new Summarised("Now playing: Lagu")),
                Placement(new object()) with { NameKey = "media.name" },
            },
        };
        using var host = new KitHost(slots);

        var first = KitHost.Peer((UIElement)slots.ItemContainerGenerator.ContainerFromIndex(0));
        var second = KitHost.Peer((UIElement)slots.ItemContainerGenerator.ContainerFromIndex(1));

        Assert.Equal("Now playing: Lagu", first.GetName());
        Assert.Equal(AutomationControlType.Group, first.GetAutomationControlType());
        Assert.Equal(Localizer.Instance["media.name"], second.GetName());
    });

    [Fact]
    public void HighContrast_RepaintsTheThemeBrushes_AndPutsThemBack() => Sta.Run(() =>
    {
        var resources = new ResourceDictionary
        {
            ["PrimaryTextBrush"] = new SolidColorBrush(Colors.White),
            ["CardBrush"] = new SolidColorBrush(Colors.DimGray),
            ["NotInTheMap"] = new SolidColorBrush(Colors.Red),
        };
        using var theme = new HighContrastTheme(resources, Contrast.Current);
        Assert.False(theme.IsApplied);

        using (KitHost.HighContrast())
        {
            Assert.True(theme.IsApplied);
            Assert.Equal(SystemColors.WindowTextColor, KitHost.ColorOf(resources["PrimaryTextBrush"]));
            Assert.Equal(SystemColors.WindowColor, KitHost.ColorOf(resources["CardBrush"]));
            Assert.Equal(Colors.Red, KitHost.ColorOf(resources["NotInTheMap"]));
        }

        Assert.False(theme.IsApplied);
        Assert.Equal(Colors.White, KitHost.ColorOf(resources["PrimaryTextBrush"]));
        Assert.Equal(Colors.DimGray, KitHost.ColorOf(resources["CardBrush"]));
    });

    [Fact]
    public void EveryThemeBrush_HasAHighContrastColour() => Sta.Run(() =>
    {
        var map = (ResourceDictionary)Application.LoadComponent(HighContrastTheme.MapSource);
        var brushKeys = new[] { "/Atap;component/Ui/Theme/Theme.xaml", "/Atap;component/Ui/Theme/Tokens.xaml" }
            .Select(source => (ResourceDictionary)Application.LoadComponent(new Uri(source, UriKind.Relative)))
            .SelectMany(dictionary => dictionary.Keys.OfType<string>().Where(key => dictionary[key] is SolidColorBrush))
            .ToList();

        Assert.NotEmpty(brushKeys);
        Assert.All(brushKeys, key => Assert.True(map.Contains(key), $"{key} has no high contrast colour."));
        Assert.All(map.Keys.OfType<string>(), key =>
        {
            Assert.IsType<Color>(map[key]);
            Assert.IsAssignableFrom<SolidColorBrush>(Application.Current.Resources[key]);
        });
    });

    [Fact]
    public void HighContrast_ReachesBrushesInMergedDictionaries() => Sta.Run(() =>
    {
        var tokens = new ResourceDictionary { ["TextBrush"] = new SolidColorBrush(Colors.Gray) };
        var resources = new ResourceDictionary { MergedDictionaries = { tokens } };
        var label = new TextBlock();
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        label.Resources.MergedDictionaries.Add(resources);
        using var theme = new HighContrastTheme(resources, Contrast.Current);

        using (KitHost.HighContrast())
            Assert.Equal(SystemColors.WindowTextColor, KitHost.ColorOf(label.Foreground));

        Assert.Equal(Colors.Gray, KitHost.ColorOf(label.Foreground));
    });

    private static PlacementViewModel Placement(object viewModel) =>
        new("test", "test", viewModel, NotchState.Panel, WidgetSize.Small, PlacementSlot.Center);

    private sealed class Summarised(string summary) : IAccessibleSummary
    {
        public string AccessibleSummary => summary;
    }

    private sealed class FakeTextScale : ITextScaleSource
    {
        public double Factor { get; set; } = 1;

        public bool Disposed { get; private set; }

        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

        public void Dispose() => Disposed = true;
    }
}
