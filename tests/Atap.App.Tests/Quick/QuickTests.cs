using Atap.Composition;
using Atap.Core.Placement;
using Atap.Core.Widgets;
using Atap.Features.Language;
using Atap.Features.NotchSize;
using Atap.Features.Quick;
using Atap.Notch;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Quick;

/// <summary>The tray icon, its menu, Pause alerts for 1 hour and the quick panel.</summary>
public sealed class QuickTests : IAsyncLifetime
{
    private readonly TestServices _services = new();
    private readonly NotchControl _notch = new();
    private readonly FakeShell _shell = new();
    private NotchSizeFeature _size = null!;
    private QuickFeature _feature = null!;

    public async Task InitializeAsync()
    {
        _services.Settings.Load();
        _services.Layout.Load();
        _services.Settings.Register(new QuickDescriptor().Settings);
        _services.Settings.Register(new NotchSizeDescriptor().Settings);
        _services.Settings.Register(new LanguageFeatureDescriptor().Settings);
        _size = new NotchSizeFeature(_services.Settings, _notch, _services.Commands, _services.Scheduler, _services.Time, NotchSizeDescriptor.FeatureId);
        await _size.StartAsync();
        _feature = new QuickFeature(
            _services.Settings, _notch, _services.Commands, _services.Scheduler, _services.Time, _services.Layout, _services.Text,
            QuickDescriptor.FeatureId, _shell);
        await _feature.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _feature.DisposeAsync();
        await _size.DisposeAsync();
        _services.Dispose();
    }

    [Fact]
    public void TheTrayIcon_ShowsByDefault_AndFollowsItsSetting()
    {
        Assert.True(_shell.TrayShown);

        _services.Settings.Set(QuickDescriptor.TrayIconKey, false);
        Assert.False(_shell.TrayShown);

        _services.Settings.Set(QuickDescriptor.TrayIconKey, true);
        Assert.True(_shell.TrayShown);
    }

    [Fact]
    public async Task Stopping_RemovesTheTrayIcon()
    {
        await _feature.DisposeAsync();

        Assert.True(_shell.Disposed);
    }

    [Fact]
    public void ClickingTheTrayIcon_OpensTheQuickPanel()
    {
        _shell.ClickTray();

        Assert.NotNull(_shell.Panel);
    }

    [Fact]
    public async Task PauseAlerts_KeepsThemQuietForAnHour_AndTicksTheMenu()
    {
        var command = _services.Commands.All.Single(c => c.Id == QuickFeature.PauseAlertsCommand);
        Assert.Equal(NotchSizeFeature.MenuGroup, command.MenuGroup);

        Assert.True(await _services.Commands.TryRunAsync(QuickFeature.PauseAlertsCommand));
        Assert.True(_notch.IsSet(NotchFlag.QuietAlerts));
        Assert.True(command.IsChecked!());

        _services.Time.Advance(TimeSpan.FromMinutes(59));
        Assert.True(_notch.IsSet(NotchFlag.QuietAlerts));

        _services.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(_notch.IsSet(NotchFlag.QuietAlerts));
        Assert.False(_feature.AreAlertsPaused);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task PauseAlertsAgain_EndsThePause()
    {
        _feature.SetAlertsPaused(true);

        Assert.True(await _services.Commands.TryRunAsync(QuickFeature.PauseAlertsCommand));

        Assert.False(_notch.IsSet(NotchFlag.QuietAlerts));
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void ThePause_LeavesTuckedAwayQuietAsItWas()
    {
        _services.Settings.Set(NotchSizeDescriptor.SizeKey, NotchSizeDescriptor.Tucked);
        _feature.SetAlertsPaused(true);
        _feature.SetAlertsPaused(false);

        // Tucked away holds its own quiet flag.
        Assert.True(_notch.IsSet(NotchFlag.QuietAlerts));
    }

    [Fact]
    public void TheTrayMenu_HasTheUsualItems_WithTicksForWhatIsOn()
    {
        _feature.SetAlertsPaused(true);

        var menu = _feature.TrayMenu();
        var headers = menu.Select(i => i.Header).ToList();

        Assert.Equal(_services.Text["quick.command.open"], headers[0]);
        Assert.True(menu[0].IsDefault);
        Assert.Contains(_services.Text["quick.hideNotch"], headers);
        Assert.Contains(_services.Text["quick.menu.settings"], headers);
        Assert.Contains(_services.Text["quick.menu.about"], headers);
        Assert.Equal(_services.Text["quick.menu.quit"], headers[^1]);
        Assert.True(menu.Single(i => i.Header == _services.Text["quick.command.pauseAlerts"]).IsChecked);
        Assert.Contains(menu, i => i.Header is null);
    }

    [Fact]
    public void HideForAnHour_FromTheTrayMenu_ThenShowsAgain()
    {
        var hide = _feature.TrayMenu().Single(i => i.Header == _services.Text["quick.hideNotch"]);
        hide.Run!();
        Assert.True(_size.IsHiddenForAnHour);

        var shown = _feature.TrayMenu().Single(i => i.Header == _services.Text["quick.hideNotch"]);
        Assert.True(shown.IsChecked);
        shown.Run!();

        Assert.False(_size.IsHiddenForAnHour);
    }

    [Fact]
    public void TheQuickPanel_ChangesSizePositionAndLanguage()
    {
        using var panel = Panel();

        panel.Size = NotchSizeDescriptor.Slim;
        panel.Position = nameof(NotchAnchor.TopRight);
        panel.Language = "ms";

        Assert.True(_notch.IsSet(NotchFlag.Slim));
        Assert.Equal(NotchAnchor.TopRight, _services.Layout.Document.Placement.Anchor);
        Assert.Equal("ms", _services.Settings.Values.GetString(LanguageFeatureDescriptor.ChoiceKey));
        Assert.Equal(3, panel.Sizes.Count);
        Assert.Equal(5, panel.Positions.Count);
        Assert.Equal(3, panel.Languages.Count);
    }

    [Fact]
    public void TheQuickPanel_HidesTheNotchAndPausesAlerts()
    {
        using var panel = Panel();

        panel.IsNotchHidden = true;
        panel.AreAlertsPaused = true;

        Assert.True(_size.IsHiddenForAnHour);
        Assert.True(_feature.AreAlertsPaused);
        Assert.True(panel.IsNotchHidden);

        panel.IsNotchHidden = false;
        Assert.False(_notch.IsSet(NotchFlag.Hide));
    }

    [Fact]
    public void TheQuickPanel_FollowsChangesMadeElsewhere()
    {
        using var panel = Panel();
        var changed = new List<string?>();
        panel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _services.Settings.Set(NotchSizeDescriptor.SizeKey, NotchSizeDescriptor.Tucked);
        _services.Layout.Update(d => d.Placement.Anchor = NotchAnchor.LeftCenter);

        Assert.Contains(nameof(QuickPanelViewModel.Size), changed);
        Assert.Contains(nameof(QuickPanelViewModel.Position), changed);
        Assert.Equal(NotchSizeDescriptor.Tucked, panel.Size);
    }

    [Fact]
    public void QuickSettings_RunsInSafeModeToo()
    {
        Assert.True(new QuickDescriptor().IsEnabled(SettingValues.Empty, AppMode.Safe));
    }

    private QuickPanelViewModel Panel()
    {
        _feature.OpenQuickPanel();
        return Assert.IsType<QuickPanelViewModel>(_shell.Panel);
    }

    private sealed class FakeShell : IQuickShell
    {
        private Action? _open;

        public bool TrayShown { get; private set; }

        public bool Disposed { get; private set; }

        public QuickPanelViewModel? Panel { get; private set; }

        public void SetTrayIcon(bool shown, Action open, Action menu)
        {
            TrayShown = shown;
            _open = open;
        }

        public void ClickTray() => _open?.Invoke();

        public void ToggleQuickPanel(QuickPanelViewModel viewModel) => Panel = viewModel;

        public void ShowTrayMenu(IReadOnlyList<TrayMenuItem> items)
        {
        }

        public void Dispose() => Disposed = true;
    }
}
