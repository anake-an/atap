using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Input;
using Atap.Core.Notch;
using Atap.Core.Widgets;
using Atap.Features.Gestures;
using Atap.Features.Hotkeys;
using Atap.Features.Panel;
using Atap.Notch;
using Atap.Platform.Input;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Input;

/// <summary>Hotkeys follow the settings and the commands, the panel hotkey taps and holds, and gestures run commands.</summary>
[Collection(HotkeyConflictsCollection.Name)]
public sealed class InputTests : IAsyncLifetime
{
    private const int SpaceKey = 0x20;

    private readonly TestServices _services = new();
    private readonly NotchControl _notch = new();
    private readonly FakeHotkeyHost _host = new();
    private readonly FakeTicker _ticker = new();
    private readonly List<string> _ran = [];
    private HotkeysFeature _hotkeys = null!;

    public async Task InitializeAsync()
    {
        _services.Settings.Load();
        _services.Settings.Register(new HotkeysDescriptor().Settings);
        _services.Settings.Register(new GesturesDescriptor().Settings);
        _hotkeys = new HotkeysFeature(_services.Settings, _services.Commands, _notch, _services.Time, _host, _ticker, HotkeysDescriptor.FeatureId);
        await _hotkeys.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _hotkeys.DisposeAsync();
        _services.Dispose();
    }

    [Fact]
    public void AShortcut_IsRegisteredOnlyWhileItsCommandExists()
    {
        Assert.Empty(_host.Active);

        using (Command(PanelFeature.ToggleCommand))
            Assert.Equal(["Ctrl+Alt+Space"], _host.Active.Values.Select(c => c.ToString()));

        Assert.Empty(_host.Active);
    }

    [Fact]
    public void Rebinding_LetsGoOfTheOldKeys()
    {
        using var panel = Command(PanelFeature.ToggleCommand);

        _services.Settings.Set(HotkeysDescriptor.PanelKey, "Ctrl+Shift+P");

        Assert.Equal(["Ctrl+Shift+P"], _host.Active.Values.Select(c => c.ToString()));
    }

    [Fact]
    public void ClearingAShortcut_RegistersNothing()
    {
        using var panel = Command(PanelFeature.ToggleCommand);

        _services.Settings.Set(HotkeysDescriptor.PanelKey, string.Empty);

        Assert.Empty(_host.Active);
    }

    [Fact]
    public void OtherCommandsComingAndGoing_DoNotReRegister()
    {
        using var panel = Command(PanelFeature.ToggleCommand);
        var registrations = _host.Registrations;

        using (Command("something.else"))
        {
        }

        Assert.Equal(registrations, _host.Registrations);
    }

    [Fact]
    public void KeysAnotherAppHolds_AreShownAsUsedElsewhere()
    {
        _host.Refuse("Ctrl+Alt+Space");

        using var panel = Command(PanelFeature.ToggleCommand);

        Assert.Contains(HotkeysDescriptor.PanelKey, HotkeyConflicts.Keys);
        Assert.Empty(_host.Active);
    }

    [Fact]
    public void AnyOtherShortcut_RunsItsCommand()
    {
        using var bar = Command("commandbar.open");

        _host.Press("Ctrl+Alt+K");

        Assert.Equal(["commandbar.open"], _ran);
        Assert.False(_ticker.IsRunning);
    }

    [Fact]
    public void TappingThePanelShortcut_TogglesThePanel_AndStopsChecking()
    {
        using var panel = Command(PanelFeature.ToggleCommand);
        _host.Down.Add(SpaceKey);

        _host.Press("Ctrl+Alt+Space");
        Assert.True(_ticker.IsRunning);
        Tick(50);

        _host.Down.Clear();
        Tick(50);

        Assert.Equal([PanelFeature.ToggleCommand], _ran);
        Assert.False(_ticker.IsRunning);
        Assert.False(_notch.IsSet(NotchFlag.Peek));
    }

    [Fact]
    public void HoldingThePanelShortcut_PeeksUntilRelease()
    {
        using var panel = Command(PanelFeature.ToggleCommand);
        _host.Down.Add(SpaceKey);
        _host.Press("Ctrl+Alt+Space");

        Tick(150);
        Tick(200);
        Assert.True(_notch.IsSet(NotchFlag.Peek));

        _host.Down.Clear();
        Tick(50);

        Assert.False(_notch.IsSet(NotchFlag.Peek));
        Assert.Empty(_ran);
        Assert.False(_ticker.IsRunning);
    }

    [Fact]
    public void AStuckKey_StopsTheCheckAfterThirtySeconds()
    {
        using var panel = Command(PanelFeature.ToggleCommand);
        _host.Down.Add(SpaceKey);
        _host.Press("Ctrl+Alt+Space");

        Tick(400);
        Tick(30_000);

        Assert.False(_ticker.IsRunning);
        Assert.False(_notch.IsSet(NotchFlag.Peek));
    }

    [Fact]
    public async Task Stopping_LetsGoOfEveryKey()
    {
        using var panel = Command(PanelFeature.ToggleCommand);

        await _hotkeys.DisposeAsync();

        Assert.True(_host.Disposed);
        Assert.Empty(HotkeyConflicts.Keys);
    }

    [Fact]
    public void Hotkeys_AreOffInSafeMode()
    {
        Assert.False(new HotkeysDescriptor().IsEnabled(SettingValues.Empty, AppMode.Safe));
    }

    [Theory]
    [InlineData(120, GestureCommands.VolumeUp)]
    [InlineData(-120, GestureCommands.VolumeDown)]
    public async Task TheWheel_ChangesTheVolume(int delta, string command)
    {
        await using var gestures = await Gestures();
        using var up = Command(GestureCommands.VolumeUp);
        using var down = Command(GestureCommands.VolumeDown);

        Assert.True(_notch.Offer(new NotchInput(NotchInputKind.Wheel, NotchSurface.Compact) { Delta = delta }));

        Assert.Equal([command], _ran);
    }

    [Fact]
    public async Task WithoutAVolumeCommand_TheWheelIsLeftAlone()
    {
        await using var gestures = await Gestures();

        Assert.False(_notch.Offer(new NotchInput(NotchInputKind.Wheel, NotchSurface.Idle) { Delta = 120 }));
    }

    [Fact]
    public async Task InTheOpenPanel_TheWheelIsLeftAlone()
    {
        await using var gestures = await Gestures();
        using var up = Command(GestureCommands.VolumeUp);

        Assert.False(_notch.Offer(new NotchInput(NotchInputKind.Wheel, NotchSurface.Panel) { Delta = 120 }));
    }

    [Fact]
    public async Task TurnedOff_TheWheelIsLeftAlone()
    {
        await using var gestures = await Gestures();
        using var up = Command(GestureCommands.VolumeUp);
        _services.Settings.Set(GesturesDescriptor.WheelVolumeKey, false);

        Assert.False(_notch.Offer(new NotchInput(NotchInputKind.Wheel, NotchSurface.Idle) { Delta = 120 }));
    }

    [Fact]
    public async Task AMiddleClick_PlaysOrPauses()
    {
        await using var gestures = await Gestures();
        using var play = Command(GestureCommands.PlayPause);

        Assert.True(_notch.Offer(new NotchInput(NotchInputKind.MiddleClick, NotchSurface.Compact)));

        Assert.Equal([GestureCommands.PlayPause], _ran);
    }

    private async Task<GesturesFeature> Gestures()
    {
        var gestures = new GesturesFeature(_services.Settings, _services.Commands, _notch);
        await gestures.StartAsync();
        return gestures;
    }

    private IDisposable Command(string id) => _services.Commands.Register(new AtapCommand(id, id, _ =>
    {
        _ran.Add(id);
        return Task.CompletedTask;
    }));

    private void Tick(int ms)
    {
        _services.Time.Advance(TimeSpan.FromMilliseconds(ms));
        _ticker.Tick();
    }

    private sealed class FakeHotkeyHost : IHotkeyHost
    {
        private readonly HashSet<string> _refused = [];

        public Dictionary<int, HotkeyChord> Active { get; } = [];

        public HashSet<int> Down { get; } = [];

        public int Registrations { get; private set; }

        public bool Disposed { get; private set; }

        public event EventHandler<int>? Pressed;

        public void Refuse(string chord) => _refused.Add(chord);

        public void Press(string chord) => Pressed?.Invoke(this, Active.Single(a => a.Value.ToString() == chord).Key);

        public bool Register(int id, HotkeyChord chord)
        {
            Registrations++;
            if (_refused.Contains(chord.ToString()))
                return false;
            Active[id] = chord;
            return true;
        }

        public void Unregister(int id) => Active.Remove(id);

        public bool IsKeyDown(int virtualKey) => Down.Contains(virtualKey);

        public void Dispose()
        {
            Active.Clear();
            Disposed = true;
        }
    }

    private sealed class FakeTicker : ITicker
    {
        private Action? _tick;

        public bool IsRunning => _tick is not null;

        public IDisposable Start(TimeSpan every, Action tick)
        {
            Assert.Equal(PeekGesture.CheckEvery, every);
            _tick = tick;
            return new Stop(this);
        }

        public void Tick() => _tick?.Invoke();

        private sealed class Stop(FakeTicker owner) : IDisposable
        {
            public void Dispose() => owner._tick = null;
        }
    }
}

/// <summary>Tests that read or write the shared refused-hotkeys list run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HotkeyConflictsCollection
{
    public const string Name = "Hotkey conflicts";
}
