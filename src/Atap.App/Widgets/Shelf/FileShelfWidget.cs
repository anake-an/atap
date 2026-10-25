using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Productivity.Shelf;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Shelf;

[assembly: AtapWidget(typeof(FileShelfDescriptor))]

namespace Atap.Widgets.Shelf;

/// <summary>Files and folders dropped on the notch to use later. Constants only.</summary>
public sealed class FileShelfDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "file-shelf";
    public const string DataFile = "shelf.json";

    public string TypeId => TypeIdValue;

    public string NameKey => "file-shelf.name";

    public string DescriptionKey => "file-shelf.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities { get; } = new(WidgetPermissions.Files, []);

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Shelf/FileShelfViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new FileShelfWidget(context, Open, PathExists);

    internal static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void Open(string path)
    {
        if (!PathExists(path))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No app opens this file. Nothing to do.
        }
    }
}

/// <summary>
/// Keeps the paths only, never copies of the files, in machine storage. Files that were moved or deleted stay on
/// the shelf greyed out until removed. It checks them when shown, not on a timer.
/// </summary>
public sealed class FileShelfWidget : WidgetBase
{
    private readonly Action<string> _open;
    private readonly Func<string, bool> _exists;
    private readonly FileShelfViewModel _viewModel;
    private ShelfDocument _document = new();

    internal FileShelfWidget(WidgetContext context, Action<string> open, Func<string, bool> exists)
        : base(context)
    {
        _open = open;
        _exists = exists;
        _viewModel = new FileShelfViewModel(context.Text, Add, Remove, Clear, open);
    }

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _document = Context.Storage.Load(FileShelfDescriptor.DataFile, ShelfJsonContext.Default.ShelfDocument, new ShelfDocument(), DataScope.Machine).Normalize();
        Context.Visibility.Changed += OnVisibilityChanged;
        Show();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        if (Context.Visibility.Value is WidgetVisibility.Expanded or WidgetVisibility.Panel)
            Show();
    }

    private void Add(IReadOnlyList<string> paths)
    {
        if (_document.Add(paths))
            Save();
    }

    private void Remove(string path)
    {
        if (_document.Remove(path))
            Save();
    }

    private void Clear()
    {
        _document.Paths.Clear();
        Save();
    }

    private void Save()
    {
        Context.Storage.Save(FileShelfDescriptor.DataFile, _document, ShelfJsonContext.Default.ShelfDocument, DataScope.Machine);
        Show();
    }

    private void Show() => _viewModel.Show(_document.Paths.Select(p => (p, _exists(p))).ToList());
}

/// <summary>One shelf item. Missing items are greyed and cannot be opened or dragged.</summary>
public sealed record ShelfRow(string Name, string Path, bool Exists, IRelayCommand Open, IRelayCommand Remove, string AccessibleName, string RemoveName)
{
    public double Opacity => Exists ? 1 : 0.45;
}

public sealed class FileShelfViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private readonly Action<IReadOnlyList<string>> _add;
    private readonly Action<string> _remove;
    private readonly Action<string> _open;
    private IReadOnlyList<(string Path, bool Exists)> _items = [];

    public FileShelfViewModel(ILocalizer text, Action<IReadOnlyList<string>> add, Action<string> remove, Action clear, Action<string> open)
    {
        _text = text;
        _add = add;
        _remove = remove;
        _open = open;
        ClearCommand = new RelayCommand(clear);
        _text.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<ShelfRow> Rows { get; } = [];

    public IRelayCommand ClearCommand { get; }

    public bool HasItems => Rows.Count > 0;

    public bool IsEmpty => Rows.Count == 0;

    public string AccessibleSummary => _text.Format("file-shelf.accessible", ("count", Rows.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)));

    /// <summary>Called by the drop target with the dropped paths.</summary>
    public void Drop(IReadOnlyList<string> paths) => _add(paths);

    internal void Show(IReadOnlyList<(string Path, bool Exists)> items)
    {
        _items = items;
        Rows.Clear();
        foreach (var (path, exists) in items)
        {
            var name = System.IO.Path.GetFileName(path);
            if (name.Length == 0)
                name = path;
            var accessible = exists ? name : _text.Format("file-shelf.missing", ("name", name));
            Rows.Add(new ShelfRow(
                name,
                path,
                exists,
                new RelayCommand(() => _open(path), () => exists),
                new RelayCommand(() => _remove(path)),
                accessible,
                _text.Format("file-shelf.removeItem", ("name", name))));
        }
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(AccessibleSummary));
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => Show(_items);
}
