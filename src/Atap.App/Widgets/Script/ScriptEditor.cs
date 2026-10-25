using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Settings.Form;

namespace Atap.Widgets.Script;

/// <summary>
/// The program editor in a script widget's settings. The user types the program, its options one per line and an
/// optional folder, sees exactly what will run, and presses Approve. Only Approve saves, and it saves the spec shown.
/// So the stored spec is always one the user approved on this PC, unless the file was changed outside Atap.
/// </summary>
public sealed class ScriptEditorViewModel : ObservableObject
{
    private readonly ScriptStore _store;
    private readonly ILocalizer _text;
    private readonly IPathPicker? _picker;
    private readonly IReadOnlyList<string> _cloudFolders;
    private string _program = string.Empty;
    private string _arguments = string.Empty;
    private string _workingFolder = string.Empty;
    private ScriptSpec? _saved;
    private bool _savedApproved;
    private bool _saveFailed;

    /// <param name="cloudFolders">Synced folders to warn about. Null reads the OneDrive folders from the environment.</param>
    internal ScriptEditorViewModel(ScriptStore store, ILocalizer text, IPathPicker? picker, IReadOnlyList<string>? cloudFolders = null)
    {
        _store = store;
        _text = text;
        _picker = picker;
        _cloudFolders = cloudFolders ?? OneDriveFolders();
        ApproveCommand = new RelayCommand(Approve, () => CanApprove);
        RemoveCommand = new RelayCommand(Remove, () => _saved is not null);
        BrowseProgramCommand = new RelayCommand(BrowseProgram);
        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        Reload();
        // Weak, so an editor the settings window dropped does not stay alive through the app-wide localizer.
        WeakEventManager<ILocalizer, EventArgs>.AddHandler(text, nameof(ILocalizer.LanguageChanged), OnLanguageChanged);
    }

    public IRelayCommand ApproveCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    public IRelayCommand BrowseProgramCommand { get; }

    public IRelayCommand BrowseFolderCommand { get; }

    public string Program
    {
        get => _program;
        set => Edit(ref _program, value);
    }

    /// <summary>The options, one per line. Each line is one argument, exactly as typed. Empty lines are left out.</summary>
    public string Arguments
    {
        get => _arguments;
        set => Edit(ref _arguments, value);
    }

    public string WorkingFolder
    {
        get => _workingFolder;
        set => Edit(ref _workingFolder, value);
    }

    public bool CanBrowse => _picker is not null;

    /// <summary>What Approve would save and run, built from the fields as they are now.</summary>
    public ScriptSpec Pending
    {
        get
        {
            var folder = _workingFolder.Trim();
            return new ScriptSpec(_program.Trim(), SplitLines(_arguments), folder.Length > 0 ? folder : null);
        }
    }

    public bool IsBlank => _program.Trim().Length == 0 && _arguments.Trim().Length == 0 && _workingFolder.Trim().Length == 0;

    public ScriptSpecProblem Problem => Pending.Validate();

    /// <summary>What is wrong with the fields, in plain words, or empty.</summary>
    public string ProblemText => IsBlank || Problem == ScriptSpecProblem.None ? string.Empty : _text[$"script.editor.problem.{Problem}"];

    public bool HasProblem => ProblemText.Length > 0;

    /// <summary>The program, each option on its own line in quotes, and the folder it starts in, exactly as they will run.</summary>
    public IReadOnlyList<string> Review
    {
        get
        {
            if (!HasReview)
                return [];
            var spec = Pending;
            var lines = new List<string> { _text.Format("script.editor.reviewProgram", ("path", spec.Executable)) };
            if (spec.Arguments.Count == 0)
                lines.Add(_text["script.editor.reviewNoArguments"]);
            for (var i = 0; i < spec.Arguments.Count; i++)
            {
                lines.Add(_text.Format(
                    "script.editor.reviewArgument",
                    ("number", (i + 1).ToString(CultureInfo.CurrentCulture)),
                    ("value", "\"" + spec.Arguments[i] + "\"")));
            }
            var folder = spec.WorkingFolder ?? Path.GetDirectoryName(spec.Executable) ?? string.Empty;
            lines.Add(_text.Format("script.editor.reviewFolder", ("path", folder)));
            return lines;
        }
    }

    public bool HasReview => !IsBlank && Problem == ScriptSpecProblem.None;

    /// <summary>
    /// True when the program, the folder or an option is inside a synced cloud folder such as OneDrive. Whoever can change
    /// that file through sync then decides what runs here. A warning only, since many people keep their scripts there.
    /// </summary>
    public bool HasCloudWarning => !IsBlank && UsesCloudFolder(Pending, _cloudFolders);

    /// <summary>
    /// True when any path in <paramref name="spec"/> is inside one of <paramref name="folders"/>. Options are checked as
    /// text, so an option such as <c>-File=C:\Users\a\OneDrive\check.ps1</c> counts too.
    /// </summary>
    internal static bool UsesCloudFolder(ScriptSpec spec, IReadOnlyList<string> folders) =>
        folders.Any(folder => IsInside(spec.Executable, folder)
            || (spec.WorkingFolder is { } working && IsInside(working, folder))
            || spec.Arguments.Any(argument => IsInside(argument, folder)));

    /// <summary>The OneDrive folders set for this user, personal and work or school.</summary>
    internal static IReadOnlyList<string> OneDriveFolders() =>
        new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable)
            .OfType<string>()
            .Select(path => path.Trim())
            .Where(Path.IsPathFullyQualified)
            .Select(path => Path.TrimEndingDirectorySeparator(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>True when <paramref name="folder"/> appears in <paramref name="text"/> as a whole folder, not as the start of a longer name.</summary>
    private static bool IsInside(string text, string folder)
    {
        if (folder.Length == 0)
            return false;
        for (var at = text.IndexOf(folder, StringComparison.OrdinalIgnoreCase); at >= 0;
             at = text.IndexOf(folder, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = at + folder.Length;
            if (end == text.Length || text[end] is '\\' or '/' or '"' or '\'')
                return true;
        }
        return false;
    }

    /// <summary>True when the fields match the saved spec and its approval matches on this PC.</summary>
    public bool IsApproved => _savedApproved && Pending.Equals(_saved);

    public bool CanApprove => _store.CanApprove && HasReview && !IsApproved;

    public string StatusText
    {
        get
        {
            if (!_store.CanApprove)
                return _text["script.editor.noLock"];
            if (_saveFailed)
                return _text["script.editor.notSaved"];
            if (IsApproved)
                return _text["script.editor.approved"];
            if (_savedApproved)
                return _text["script.editor.changed"];
            if (_saved is null && IsBlank)
                return _text["script.editor.empty"];
            return _text["script.editor.notApproved"];
        }
    }

    /// <summary>Lines of <paramref name="text"/>, each one argument. A line break inside an argument is not possible.</summary>
    internal static IReadOnlyList<string> SplitLines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();

    private void Edit(ref string field, string? value)
    {
        if (SetProperty(ref field, value ?? string.Empty))
        {
            _saveFailed = false;
            Refresh();
        }
    }

    private void Approve()
    {
        if (!CanApprove)
            return;
        _saveFailed = !_store.Approve(Pending);
        Reload(keepFields: _saveFailed);
    }

    private void Remove()
    {
        _saveFailed = !_store.Remove();
        Reload(keepFields: _saveFailed);
    }

    private void BrowseProgram()
    {
        if (_picker?.PickFile(_program.Trim().Length > 0 ? _program.Trim() : null) is { } file)
            Program = file;
    }

    private void BrowseFolder()
    {
        var start = _workingFolder.Trim().Length > 0 ? _workingFolder.Trim() : null;
        if (_picker?.PickFolder(start) is { } folder)
            WorkingFolder = folder;
    }

    /// <summary>Shows what is saved now. Fields are filled from it, so a spec changed outside Atap can be checked and approved again.</summary>
    /// <param name="keepFields">After a failed save, so what the user typed is not lost.</param>
    private void Reload(bool keepFields = false)
    {
        var state = _store.Load();
        _saved = ScriptStore.SpecOf(state);
        _savedApproved = _saved is not null && _store.IsApproved(_saved, state);
        if (!keepFields)
        {
            _program = _saved?.Executable ?? string.Empty;
            _arguments = _saved is null ? string.Empty : string.Join(Environment.NewLine, _saved.Arguments);
            _workingFolder = _saved?.WorkingFolder ?? string.Empty;
        }
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(string.Empty);
        ApproveCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}

/// <summary>
/// Turns the settings form's custom field into a <see cref="ScriptEditorViewModel"/> for its widget instance.
/// The editor reads and writes the instance's machine state and keeps its approval key in the form's secret store.
/// Null for a field that does not belong to a widget instance.
/// </summary>
internal sealed class ScriptEditorConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CustomField { Context: { Target: WidgetSettingsTarget target } form }
            || !WidgetInstanceId.TryParse(target.InstanceId, out var id)
            || id.TypeId != ScriptDescriptor.TypeIdValue)
            return null;
        var store = new ScriptStore(target.InstanceId, new WidgetStorage(target.InstanceId, AtapPaths.Default), form.Secrets);
        return new ScriptEditorViewModel(store, form.Text, form.Picker);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
