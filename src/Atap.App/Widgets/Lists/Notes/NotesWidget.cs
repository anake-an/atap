using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.Productivity;
using Atap.Core.Productivity.Lists;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Lists.Notes;

[assembly: AtapWidget(typeof(NotesDescriptor))]

namespace Atap.Widgets.Lists.Notes;

/// <summary>Quick notes. Constants only.</summary>
public sealed class NotesDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "notes";

    public string TypeId => TypeIdValue;

    public string NameKey => "notes.name";

    public string DescriptionKey => "notes.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Lists/Notes/NotesViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new NotesWidget(context);
}

/// <summary>
/// Notes in user data. Typing saves one second after the last key, and stopping saves at once, so nothing is lost
/// and a fast typist causes one write. Empty notes are dropped when the panel closes.
/// </summary>
public sealed class NotesWidget : WidgetBase
{
    public const string DataFile = "notes.json";
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly NotesViewModel _viewModel;
    private NotesDocument _document = new();
    private IDisposable? _pendingSave;

    public NotesWidget(WidgetContext context)
        : base(context)
    {
        _viewModel = new NotesViewModel(context.Text, new NotesActions(Edit, New, TogglePin, Delete));
    }

    public override object ViewModel => _viewModel;

    private DateTimeOffset Now => Context.Time.GetUtcNow();

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _document = Context.Storage.Load(DataFile, ListsJsonContext.Default.NotesDocument, new NotesDocument()).Normalize();
        Context.Visibility.Changed += OnVisibilityChanged;
        _viewModel.Show(_document);
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        SaveNow();
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void Edit(string id, string text)
    {
        var edit = _document.Edit(id, text, Now);
        if (!edit.Changed)
            return;
        _document = edit.Document;
        _pendingSave?.Dispose();
        _pendingSave = Context.Scheduler.At(Now + SaveDelay, SaveNow, new ScheduleOptions(TimeSpan.Zero, false, MissedRun.RunOnce));
    }

    private string? New()
    {
        if (_document.IsFull)
            return null;
        var note = Note.Create(ItemIds.New(), Now);
        _document = _document.Upsert(note);
        SaveNow();
        return note.Id;
    }

    private void TogglePin(string id)
    {
        if (_document.Find(id) is not { } note)
            return;
        _document = _document.Upsert(note.WithPinned(!note.Pinned));
        SaveNow();
        _viewModel.Show(_document, id);
    }

    private void Delete(string id)
    {
        _document = _document.Remove(id);
        SaveNow();
        _viewModel.Show(_document);
    }

    /// <summary>Leaving the panel drops empty notes, so New never leaves blanks behind.</summary>
    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        if (Context.Visibility.Value == WidgetVisibility.Panel)
            return;
        var cleaned = _document.RemoveEmpty();
        if (cleaned.Notes.Count != _document.Notes.Count)
        {
            _document = cleaned;
            SaveNow();
        }
        _viewModel.Show(_document);
    }

    private void SaveNow()
    {
        _pendingSave?.Dispose();
        _pendingSave = null;
        Context.Storage.Save(DataFile, _document, ListsJsonContext.Default.NotesDocument);
    }
}

public sealed record NotesActions(Action<string, string> Edit, Func<string?> New, Action<string> TogglePin, Action<string> Delete);

public sealed partial class NotesViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private readonly NotesActions _actions;
    private IReadOnlyList<Note> _notes = [];
    private int _index;
    private string _editText = string.Empty;

    public NotesViewModel(ILocalizer text, NotesActions actions)
    {
        _text = text;
        _actions = actions;
        _text.LanguageChanged += OnLanguageChanged;
    }

    private Note? Current => _index >= 0 && _index < _notes.Count ? _notes[_index] : null;

    public bool HasNote => Current is not null;

    public bool HasNone => Current is null;

    /// <summary>The text being edited. Each change goes to the widget, which saves it a second later.</summary>
    public string EditText
    {
        get => _editText;
        set
        {
            if (!SetProperty(ref _editText, value ?? string.Empty) || Current is not { } note)
                return;
            _actions.Edit(note.Id, _editText);
            OnPropertyChanged(nameof(Preview));
        }
    }

    /// <summary>The note as the open notch shows it.</summary>
    public string Preview => Current is null ? _text["notes.empty"] : _editText;

    public bool IsPinned => Current?.Pinned == true;

    public string PinLabel => _text[IsPinned ? "notes.unpin" : "notes.pin"];

    /// <summary>"2 of 5".</summary>
    public string Position => _notes.Count == 0 ? string.Empty
        : _text.Format("notes.position", ("number", (_index + 1).ToString(CultureInfo.CurrentCulture)), ("count", _notes.Count.ToString(CultureInfo.CurrentCulture)));

    public bool CanGoBack => _index > 0;

    public bool CanGoForward => _index < _notes.Count - 1;

    public string AccessibleSummary => Current is { } note && note.Title.Length > 0
        ? _text.Format("notes.accessible", ("title", note.Title))
        : _text["notes.accessibleEmpty"];

    public void Show(NotesDocument document, string? selectId = null)
    {
        var keep = selectId ?? Current?.Id;
        _notes = document.Ordered();
        var found = keep is null ? -1 : _notes.ToList().FindIndex(n => n.Id == keep);
        _index = found >= 0 ? found : 0;
        _editText = Current?.Text ?? string.Empty;
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    [RelayCommand]
    private void New()
    {
        if (_actions.New() is not { } id)
            return;
        _notes = [new Note { Id = id }, .. _notes];
        _index = 0;
        _editText = string.Empty;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void Previous() => Move(-1);

    [RelayCommand]
    private void Next() => Move(1);

    [RelayCommand]
    private void Pin()
    {
        if (Current is { } note)
            _actions.TogglePin(note.Id);
    }

    [RelayCommand]
    private void Delete()
    {
        if (Current is { } note)
            _actions.Delete(note.Id);
    }

    private void Move(int step)
    {
        var target = Math.Clamp(_index + step, 0, Math.Max(0, _notes.Count - 1));
        if (target == _index)
            return;
        _index = target;
        _editText = Current?.Text ?? string.Empty;
        OnPropertyChanged(string.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
