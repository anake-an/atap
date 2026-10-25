using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Atap.Tests.Infrastructure;
using Atap.Ui.Controls;

namespace Atap.Tests.Ui.Controls;

public class IconButtonTests
{
    [Fact]
    public void Template_LoadsWithTheIcon() => Sta.Run(() =>
    {
        var play = KitHost.Icon("icon.play");
        var button = new IconButton { Icon = play, Label = "Play" };
        using var host = new KitHost(button);

        Assert.NotNull(button.Template);
        var glyph = KitHost.Named<StrokeIcon>(button, "Glyph");
        Assert.Same(play, glyph.Data);
        Assert.Equal(16, glyph.ActualWidth);
        Assert.Equal(32, button.ActualWidth);
        Assert.Equal(KitHost.Token("TextColor"), KitHost.ColorOf(glyph.Foreground));
    });

    [Fact]
    public void Label_IsTheScreenReaderNameAndTooltip() => Sta.Run(() =>
    {
        var button = new IconButton { Icon = KitHost.Icon("icon.next"), Label = "Next" };
        using var host = new KitHost(button);
        var peer = KitHost.Peer(button);

        Assert.Equal("Next", peer.GetName());
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        Assert.Equal(nameof(IconButton), peer.GetClassName());
        Assert.Equal("Next", button.ToolTip);
    });

    [Fact]
    public void AutomationName_WinsOverTheLabel() => Sta.Run(() =>
    {
        var button = new IconButton { Label = "Next" };
        AutomationProperties.SetName(button, "Next song");
        using var host = new KitHost(button);

        Assert.Equal("Next song", KitHost.Peer(button).GetName());
    });

    [Fact]
    public void Values_Bind() => Sta.Run(() =>
    {
        var model = new ButtonModel { Label = "Play", Icon = KitHost.Icon("icon.play") };
        var button = new IconButton { DataContext = model };
        button.SetBinding(IconButton.LabelProperty, new Binding(nameof(ButtonModel.Label)));
        button.SetBinding(IconButton.IconProperty, new Binding(nameof(ButtonModel.Icon)));
        using var host = new KitHost(button);
        var glyph = KitHost.Named<StrokeIcon>(button, "Glyph");
        Assert.Equal("Play", KitHost.Peer(button).GetName());

        model.Label = "Pause";
        model.Icon = KitHost.Icon("icon.pause");

        Assert.Equal("Pause", KitHost.Peer(button).GetName());
        Assert.Same(model.Icon, glyph.Data);
    });

    [Fact]
    public void Invoke_RunsTheCommand() => Sta.Run(() =>
    {
        var command = new CountingCommand();
        var button = new IconButton { Label = "Refresh", Command = command, CommandParameter = "now" };
        using var host = new KitHost(button);

        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(KitHost.Peer(button).GetPattern(PatternInterface.Invoke));
        invoke.Invoke();
        KitHost.Pump();

        Assert.Equal(1, command.Runs);
        Assert.Equal("now", command.LastParameter);
    });

    [Fact]
    public void TakesKeyboardFocus_WithARoundRing() => Sta.Run(() =>
    {
        var button = new IconButton { Label = "Play" };
        using var host = new KitHost(button);

        Assert.True(button.Focusable);
        Assert.True(button.IsTabStop);
        Assert.Same(Application.Current.FindResource("focus.round"), button.FocusVisualStyle);
        Assert.True(button.Focus());
        Assert.True(button.IsKeyboardFocused);
    });

    [Fact]
    public void HighContrast_UsesButtonColoursWithAnEdge() => Sta.Run(() =>
    {
        var button = new IconButton { Icon = KitHost.Icon("icon.play"), Label = "Play" };
        using var host = new KitHost(button);
        var glyph = KitHost.Named<StrokeIcon>(button, "Glyph");
        var chrome = KitHost.Named<Border>(button, "Chrome");

        using (KitHost.HighContrast())
        {
            Assert.Equal(SystemColors.ControlTextColor, KitHost.ColorOf(glyph.Foreground));
            Assert.Equal(SystemColors.ControlColor, KitHost.ColorOf(chrome.Background));
            Assert.Equal(new Thickness(1), chrome.BorderThickness);
        }

        Assert.Equal(KitHost.Token("TextColor"), KitHost.ColorOf(glyph.Foreground));
        Assert.Equal(new Thickness(0), chrome.BorderThickness);
    });

    [Fact]
    public void Disabled_DimsTheIcon() => Sta.Run(() =>
    {
        var button = new IconButton { Icon = KitHost.Icon("icon.play"), Label = "Play", IsEnabled = false };
        using var host = new KitHost(button);

        Assert.Equal(0.4, KitHost.Named<StrokeIcon>(button, "Glyph").Opacity, 3);
    });

    private sealed class ButtonModel : INotifyPropertyChanged
    {
        private string? _label;
        private Geometry? _icon;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string? Label
        {
            get => _label;
            set => Set(ref _label, value);
        }

        public Geometry? Icon
        {
            get => _icon;
            set => Set(ref _icon, value);
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
