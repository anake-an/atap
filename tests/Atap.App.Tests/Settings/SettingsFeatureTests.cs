using Atap.Composition;
using Atap.Core.Commands;
using Atap.Features.SettingsWindow;
using Atap.Notch;
using Atap.Settings.Shell;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Settings;

public sealed class SettingsFeatureTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly NotchControl _notch = new();

    public SettingsFeatureTests() => _services.Settings.Load();

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task SettingsOpen_IsTheSecondLaunchCommand_AndSitsInTheNotchMenu()
    {
        var feature = new SettingsWindowFeature(_services.Commands, _ => new FakeSession());
        await feature.StartAsync();

        var command = Assert.Single(_services.Commands.All, c => c.Id == SecondInstance.CommandId);
        Assert.Equal(AppCommands.OpenSettings, command.Id);
        Assert.Equal(SettingsWindowDescriptor.MenuGroup, command.MenuGroup);
        Assert.NotEqual(command.TitleKey, _services.Text[command.TitleKey]);
        Assert.NotEqual(command.KeywordsKey, _services.Text[command.KeywordsKey!]);
    }

    [Fact]
    public async Task RunningItAgain_BringsTheSameWindowToTheFront()
    {
        var opened = new List<FakeSession>();
        var feature = new SettingsWindowFeature(_services.Commands, page =>
        {
            var session = new FakeSession { FirstPage = page };
            opened.Add(session);
            return session;
        });
        await feature.StartAsync();

        Assert.True(await _services.Commands.TryRunAsync(AppCommands.OpenSettings));
        Assert.True(await _services.Commands.TryRunAsync(AppCommands.OpenSettings, "about"));

        var session = Assert.Single(opened);
        Assert.Null(session.FirstPage);
        Assert.Equal(new string?[] { "about" }, session.Shown);

        // Once closed, the next run opens a new window.
        session.Close();
        Assert.Null(feature.Session);
        await _services.Commands.TryRunAsync(AppCommands.OpenSettings, "privacy");
        Assert.Equal(2, opened.Count);
        Assert.Equal("privacy", opened[1].FirstPage);
    }

    [Fact]
    public async Task StoppingTheFeature_ClosesTheWindow_AndRemovesTheCommand()
    {
        var host = CreateHost(new SettingsWindowDescriptor());
        await host.StartAsync();
        Assert.True(_services.Commands.Contains(AppCommands.OpenSettings));

        await host.DisposeAsync();

        Assert.False(_services.Commands.Contains(AppCommands.OpenSettings));
    }

    [Fact]
    public async Task DisposeAsync_ClosesAnOpenWindow()
    {
        var session = new FakeSession();
        var feature = new SettingsWindowFeature(_services.Commands, _ => session);
        await feature.StartAsync();
        await _services.Commands.TryRunAsync(AppCommands.OpenSettings);

        await feature.DisposeAsync();

        Assert.True(session.Closed);
    }

    [Fact]
    public async Task ShowAtapOff_HidesTheNotch_UntilItIsOnAgain()
    {
        var descriptor = new ShowAtapDescriptor();
        _services.Settings.Register(descriptor.Settings);
        await using var host = CreateHost(descriptor);
        await host.StartAsync();
        Assert.Empty(host.RunningIds);
        Assert.False(_notch.IsSet(NotchFlag.Hide));

        _services.Settings.Set(ShowAtapDescriptor.ShowKey, false);
        await WaitUntil(() => host.RunningIds.Count == 1);
        Assert.True(_notch.IsSet(NotchFlag.Hide));

        _services.Settings.Set(ShowAtapDescriptor.ShowKey, true);
        await WaitUntil(() => host.RunningIds.Count == 0);
        Assert.False(_notch.IsSet(NotchFlag.Hide));
    }

    [Fact]
    public void ShowAtap_StaysOnThisPc()
    {
        var setting = Assert.Single(new ShowAtapDescriptor().Settings);

        Assert.Equal(Atap.Core.Widgets.SettingScope.Machine, setting.Scope);
        Assert.Equal(SettingsSections.General, setting.SectionId);
        Assert.Equal(true, setting.DefaultValue);
    }

    private FeatureHost CreateHost(IFeatureDescriptor descriptor) => new(
        [descriptor],
        _services.Settings,
        AppMode.Normal,
        (d, lifetime) => _services.CreateFeatureContext(d, lifetime, AppMode.Normal, _notch),
        _notch.ClearOwner,
        _services.Warn);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class FakeSession : ISettingsSession
    {
        public string? FirstPage { get; init; }

        public List<string?> Shown { get; } = [];

        public bool Closed { get; private set; }

        event EventHandler? ISettingsSession.Closed
        {
            add => _closed += value;
            remove => _closed -= value;
        }

        private EventHandler? _closed;

        public void Show(string? pageOrSection) => Shown.Add(pageOrSection);

        public void Close()
        {
            Closed = true;
            _closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
