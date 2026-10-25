using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Platform.Clipboard;
using Atap.Platform.Input;
using Atap.Widgets.Shelf;
using History = Atap.Core.Clipboard.ClipboardHistory;

[assembly: AtapWidget(typeof(ClipboardDescriptor))]

namespace Atap.Widgets.Shelf;

/// <summary>Recent copied text. Constants only.</summary>
public sealed class ClipboardDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "clipboard";
    public const string KeepKey = "keepHistory";

    public string TypeId => TypeIdValue;

    public string NameKey => "clipboard.name";

    public string DescriptionKey => "clipboard.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    // Machine scope: whether this PC keeps the history never travels with sync.
    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(KeepKey, "clipboard.keep", SettingType.Bool, false) { DescriptionKey = "clipboard.keepHint", Scope = SettingScope.Machine },
    ];

    public WidgetCapabilities Capabilities { get; } = new(WidgetPermissions.Clipboard, [], "clipboard.privacy");

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Shelf/ClipboardViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) =>
        new ClipboardWidget(context, () => new WindowsClipboardWatcher(context.Shared.Acquire<MessageWindow>()));
}

/// <summary>
/// Listens to the clipboard only while running, and keeps the history in memory, so it is gone when Atap closes or
/// the widget is turned off. With "Keep my history" on, it is also saved on this PC with Windows data protection,
/// <see cref="SaveDelay"/> after the last change, and read back on start. Turning the setting off deletes the file.
/// </summary>
public sealed class ClipboardWidget : WidgetBase
{
    /// <summary>A burst of copies is saved once.</summary>
    internal static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    private readonly Func<IClipboardWatcher> _createWatcher;
    private readonly History _history = new();
    private readonly ClipboardViewModel _viewModel;
    private IClipboardWatcher? _watcher;
    private ClipboardHistoryStore? _store;
    private IDisposable? _pendingSave;

    internal ClipboardWidget(WidgetContext context, Func<IClipboardWatcher> createWatcher)
        : base(context)
    {
        _createWatcher = createWatcher;
        _viewModel = new ClipboardViewModel(context.Text, CopyBack, Remove, Clear);
    }

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        var store = new ClipboardHistoryStore(Context.Storage);
        if (settings.GetBool(ClipboardDescriptor.KeepKey))
        {
            _store = store;
            _history.Restore(store.Load());
        }
        else
        {
            store.Delete();
        }
        _watcher = _createWatcher();
        _watcher.Copied += OnCopied;
        _viewModel.Show(_history.Items);
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        if (_watcher is not null)
        {
            _watcher.Copied -= OnCopied;
            _watcher.Dispose();
            _watcher = null;
        }
        if (_pendingSave is not null)
        {
            _pendingSave.Dispose();
            _pendingSave = null;
            _store?.Save(_history.Items);
        }
        _store = null;
        _history.Clear();
        _viewModel.Show(_history.Items);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnCopied(object? sender, string text)
    {
        if (_history.Add(text))
            Changed();
    }

    private void CopyBack(string text)
    {
        _watcher?.Copy(text);
        if (_history.Add(text))
            Changed();
    }

    private void Remove(string text)
    {
        _history.Remove(text);
        Changed();
    }

    private void Clear()
    {
        _history.Clear();
        Changed();
    }

    private void Changed()
    {
        _viewModel.Show(_history.Items);
        if (_store is not { } store)
            return;
        _pendingSave?.Dispose();
        // A save missed during sleep still happens after wake.
        _pendingSave = Context.Scheduler.At(Context.Time.GetUtcNow() + SaveDelay, () =>
        {
            _pendingSave = null;
            store.Save(_history.Items);
        }, new ScheduleOptions(Missed: MissedRun.RunOnce));
    }
}

/// <summary>One copied item: its first line, and buttons to copy it again or remove it.</summary>
public sealed record ClipRow(string Preview, string Text, IRelayCommand Copy, IRelayCommand Remove, string CopyName, string RemoveName);

public sealed class ClipboardViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private readonly Action<string> _copy;
    private readonly Action<string> _remove;
    private IReadOnlyList<string> _items = [];

    public ClipboardViewModel(ILocalizer text, Action<string> copy, Action<string> remove, Action clear)
    {
        _text = text;
        _copy = copy;
        _remove = remove;
        ClearCommand = new RelayCommand(clear);
        _text.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<ClipRow> Rows { get; } = [];

    public IRelayCommand ClearCommand { get; }

    public bool HasItems => Rows.Count > 0;

    public bool IsEmpty => Rows.Count == 0;

    public string AccessibleSummary => _text.Format("clipboard.accessible", ("count", Rows.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)));

    internal void Show(IReadOnlyList<string> items)
    {
        _items = items.ToList();
        Rows.Clear();
        foreach (var item in _items)
        {
            var preview = History.Preview(item);
            Rows.Add(new ClipRow(
                preview,
                item,
                new RelayCommand(() => _copy(item)),
                new RelayCommand(() => _remove(item)),
                _text.Format("clipboard.copyItem", ("item", preview)),
                _text.Format("clipboard.removeItem", ("item", preview))));
        }
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(AccessibleSummary));
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => Show(_items);
}
