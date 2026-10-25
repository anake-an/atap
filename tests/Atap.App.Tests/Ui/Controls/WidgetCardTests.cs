using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Atap.Tests.Infrastructure;
using Atap.Ui.Controls;

namespace Atap.Tests.Ui.Controls;

public class WidgetCardTests
{
    [Fact]
    public void Template_LoadsWithHeaderIconAndContent() => Sta.Run(() =>
    {
        var cloud = KitHost.Icon("icon.cloud");
        var body = new TextBlock { Text = "31°" };
        var card = new WidgetCard { Header = "Weather", Icon = cloud, Content = body };
        using var host = new KitHost(card);

        Assert.Equal(Visibility.Visible, KitHost.Named<StackPanel>(card, "HeaderRow").Visibility);
        Assert.Same(cloud, KitHost.Named<StrokeIcon>(card, "HeaderIcon").Data);
        Assert.True(body.IsVisible);
        var chrome = KitHost.Named<Border>(card, "Chrome");
        Assert.Equal(KitHost.Token("CardColor"), KitHost.ColorOf(chrome.Background));
        Assert.Equal(Application.Current.FindResource("CardCornerRadius"), chrome.CornerRadius);
    });

    [Fact]
    public void WithoutHeaderOrIcon_TheirPartsCollapse() => Sta.Run(() =>
    {
        var plain = new WidgetCard { Content = "Body" };
        var titled = new WidgetCard { Header = "Notes", Content = "Body" };
        using var plainHost = new KitHost(plain);
        using var titledHost = new KitHost(titled);

        Assert.Equal(Visibility.Collapsed, KitHost.Named<StackPanel>(plain, "HeaderRow").Visibility);
        Assert.Equal(Visibility.Visible, KitHost.Named<StackPanel>(titled, "HeaderRow").Visibility);
        Assert.Equal(Visibility.Collapsed, KitHost.Named<StrokeIcon>(titled, "HeaderIcon").Visibility);
    });

    [Fact]
    public void ScreenReaderName_IsTheHeaderUnlessSet() => Sta.Run(() =>
    {
        var card = new WidgetCard { Header = "Prayer times" };
        using var host = new KitHost(card);
        var peer = KitHost.Peer(card);

        Assert.Equal("Prayer times", peer.GetName());
        Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());

        card.SetBinding(AutomationProperties.NameProperty, new Binding("Summary") { Source = new { Summary = "Asar 16:13, in 23 minutes" } });
        Assert.Equal("Asar 16:13, in 23 minutes", peer.GetName());
    });

    [Fact]
    public void Header_Binds() => Sta.Run(() =>
    {
        var card = new WidgetCard();
        card.SetBinding(HeaderedContentControl.HeaderProperty, new Binding("Title") { Source = new { Title = "Battery" } });
        using var host = new KitHost(card);

        Assert.Equal("Battery", card.Header);
        Assert.Equal(Visibility.Visible, KitHost.Named<StackPanel>(card, "HeaderRow").Visibility);
    });

    [Fact]
    public void TakesKeyboardFocus_WithTheCardRing() => Sta.Run(() =>
    {
        var card = new WidgetCard { Header = "Clock" };
        using var host = new KitHost(card);

        Assert.True(card.Focusable);
        Assert.Same(Application.Current.FindResource("focus.card"), card.FocusVisualStyle);
        Assert.True(card.Focus());
    });

    [Fact]
    public void EnterAndSpace_RunTheCommand() => Sta.Run(() =>
    {
        var command = new CountingCommand();
        var card = new WidgetCard { Header = "Focus timer", Command = command, CommandParameter = 25 };
        using var host = new KitHost(card);

        Press(card, Key.Enter);
        Press(card, Key.Space);
        Press(card, Key.A);

        Assert.Equal(2, command.Runs);
        Assert.Equal(25, command.LastParameter);
    });

    [Fact]
    public void Keys_FromAButtonInside_AreLeftToTheButton() => Sta.Run(() =>
    {
        var command = new CountingCommand();
        var inner = new IconButton { Label = "Pause" };
        var card = new WidgetCard { Command = command, Content = inner };
        using var host = new KitHost(card);

        Press(inner, Key.Enter);

        Assert.Equal(0, command.Runs);
    });

    [Fact]
    public void Invoke_IsThereOnlyWithACommand() => Sta.Run(() =>
    {
        var card = new WidgetCard { Header = "Notes" };
        using var host = new KitHost(card);
        var peer = KitHost.Peer(card);
        Assert.Null(peer.GetPattern(PatternInterface.Invoke));
        Assert.False(card.HasCommand);

        var command = new CountingCommand();
        card.Command = command;
        Assert.True(card.HasCommand);
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
        invoke.Invoke();
        KitHost.Pump();

        Assert.Equal(1, command.Runs);
    });

    [Fact]
    public void Activate_RespectsCanExecuteAndIsEnabled() => Sta.Run(() =>
    {
        var blocked = new CountingCommand(canExecute: false);
        var command = new CountingCommand();
        var card = new WidgetCard { Command = blocked };

        Assert.False(card.Activate());
        card.Command = command;
        card.IsEnabled = false;
        Assert.False(card.Activate());
        card.IsEnabled = true;
        Assert.True(card.Activate());
        Assert.Equal(0, blocked.Runs);
        Assert.Equal(1, command.Runs);
    });

    [Fact]
    public void HighContrast_UsesWindowColoursWithAnEdge() => Sta.Run(() =>
    {
        var card = new WidgetCard { Header = "Weather", Icon = KitHost.Icon("icon.cloud") };
        using var host = new KitHost(card);
        var chrome = KitHost.Named<Border>(card, "Chrome");

        using (KitHost.HighContrast())
        {
            Assert.Equal(SystemColors.WindowColor, KitHost.ColorOf(chrome.Background));
            Assert.Equal(SystemColors.WindowTextColor, KitHost.ColorOf(chrome.BorderBrush));
            Assert.Equal(new Thickness(1), chrome.BorderThickness);
            Assert.Equal(SystemColors.WindowTextColor, KitHost.ColorOf(KitHost.Named<StrokeIcon>(card, "HeaderIcon").Foreground));
        }

        Assert.Equal(KitHost.Token("CardColor"), KitHost.ColorOf(chrome.Background));
    });

    private static void Press(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target) ?? throw new InvalidOperationException("Not in a window.");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
    }
}
