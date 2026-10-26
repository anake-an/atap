using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Input;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.Hotkeys;
using Atap.Features.Panel;
using Atap.Notch;
using Atap.Platform.Input;

[assembly: AtapFeature(typeof(HotkeysDescriptor))]

namespace Atap.Features.Hotkeys;

/// <summary>
/// Keyboard shortcuts that work everywhere. Each setting <c>hotkeys.&lt;commandId&gt;</c> runs that command. The panel
/// shortcut is two in one: tap it to open or close the panel, hold it to peek at the open notch. A shortcut is
/// registered only while its command exists, so a feature that is off holds no keys. Not in safe mode.
/// </summary>
internal sealed class HotkeysDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "hotkeys";
    public const string Prefix = "hotkeys.";
    public const string PanelKey = Prefix + PanelFeature.ToggleCommand;
    public const string CommandBarKey = Prefix + "commandbar.open";

    /// <summary>Opens the command bar ready to ask AI. Unset by default, and does nothing while Ask AI is off.</summary>
    public const string AskAiKey = Prefix + Ai.AiDescriptor.AskCommand;

    /// <summary>Every shortcut setting, in the order Settings shows them.</summary>
    public static IReadOnlyList<string> Keys { get; } = [PanelKey, CommandBarKey, AskAiKey];

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(PanelKey, "hotkeys.panel", SettingType.Hotkey, HotkeyDefaults.Panel)
        {
            SectionId = SettingsSections.Behavior,
            DescriptionKey = "hotkeys.panelHint",
        },
        new(CommandBarKey, "hotkeys.commandBar", SettingType.Hotkey, HotkeyDefaults.CommandBar)
        {
            SectionId = SettingsSections.Behavior,
            DescriptionKey = "hotkeys.commandBarHint",
        },
        new(AskAiKey, "hotkeys.askAi", SettingType.Hotkey, string.Empty)
        {
            SectionId = SettingsSections.Behavior,
            DescriptionKey = "hotkeys.askAiHint",
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => mode != AppMode.Safe;

    public IAppFeature Create(FeatureContext context) => new HotkeysFeature(
        context.Settings, context.Commands, context.Notch, context.Time,
        new WindowsHotkeyHost(context.Shared.Acquire<MessageWindow>()), new DispatcherTicker(), FeatureId);
}

/// <summary>Keeps Windows' hotkeys in line with the settings and the registered commands, and runs the panel tap and hold.</summary>
internal sealed class HotkeysFeature(
    IAppSettingsView settings,
    ICommandRegistry commands,
    INotchControl notch,
    TimeProvider time,
    IHotkeyHost host,
    ITicker ticker,
    string ownerId) : IAppFeature
{
    private readonly Dictionary<int, Binding> _registered = [];
    private List<Binding> _wanted = [];
    private int _nextId = 1;
    private PeekGesture? _peek;
    private int _peekKey;
    private IDisposable? _peekCheck;

    /// <summary>Setting keys Windows refused, because another app holds the same keys.</summary>
    public IReadOnlySet<string> Refused => HotkeyConflicts.Keys;

    public Task StartAsync()
    {
        host.Pressed += OnPressed;
        settings.Changed += OnSettingsChanged;
        commands.Changed += OnCommandsChanged;
        Sync();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        host.Pressed -= OnPressed;
        settings.Changed -= OnSettingsChanged;
        commands.Changed -= OnCommandsChanged;
        StopPeek();
        host.Dispose();
        _registered.Clear();
        HotkeyConflicts.Set([]);
        // The feature host clears the peek flag once the feature has stopped.
        return ValueTask.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Any(k => k.StartsWith(HotkeysDescriptor.Prefix, StringComparison.Ordinal)))
            Sync();
    }

    private void OnCommandsChanged(object? sender, EventArgs e) => Sync();

    /// <summary>
    /// Registers what the settings ask for whose command exists. Nothing changes when nothing would: commands come
    /// and go often while Atap starts, and every new key lets go of the old one first.
    /// </summary>
    private void Sync()
    {
        var wanted = HotkeysDescriptor.Keys
            .Select(key => (Key: key, Text: settings.Values.GetString(key)))
            .Where(s => HotkeyChord.TryParse(s.Text, out _))
            .Select(s => new Binding(s.Key, s.Key[HotkeysDescriptor.Prefix.Length..], HotkeyChord.Parse(s.Text)))
            .Where(b => commands.Contains(b.CommandId))
            .ToList();
        if (wanted.SequenceEqual(_wanted))
            return;
        _wanted = wanted;

        foreach (var id in _registered.Keys.ToList())
            host.Unregister(id);
        _registered.Clear();

        var refused = new List<string>();
        foreach (var binding in wanted)
        {
            var id = _nextId++;
            if (host.Register(id, binding.Chord))
                _registered[id] = binding;
            else
                refused.Add(binding.Key);
        }
        HotkeyConflicts.Set(refused);
    }

    private void OnPressed(object? sender, int id)
    {
        if (!_registered.TryGetValue(id, out var binding))
            return;
        if (binding.CommandId != PanelFeature.ToggleCommand)
        {
            _ = commands.TryRunAsync(binding.CommandId);
            return;
        }
        // Windows reports only the press. A short check while the key stays down tells a tap from a hold.
        if (_peek is not null)
            return;
        _peek = new PeekGesture(Now());
        _peekKey = binding.Chord.VirtualKey;
        _peekCheck = ticker.Start(PeekGesture.CheckEvery, CheckPeek);
    }

    private void CheckPeek()
    {
        if (_peek is null)
            return;
        switch (_peek.Update(Now(), host.IsKeyDown(_peekKey)))
        {
            case PeekAction.TogglePanel:
                _ = commands.TryRunAsync(PanelFeature.ToggleCommand);
                break;
            case PeekAction.StartPeek:
                notch.SetFlag(NotchFlag.Peek, ownerId, on: true);
                break;
            case PeekAction.EndPeek:
                notch.SetFlag(NotchFlag.Peek, ownerId, on: false);
                break;
        }
        if (_peek.IsDone)
            StopPeek();
    }

    private void StopPeek()
    {
        _peekCheck?.Dispose();
        _peekCheck = null;
        _peek = null;
    }

    private TimeSpan Now() => time.GetUtcNow() - DateTimeOffset.UnixEpoch;

    private sealed record Binding(string Key, string CommandId, HotkeyChord Chord);
}
