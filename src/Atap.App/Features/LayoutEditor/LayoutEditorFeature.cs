using System.IO;
using System.Text.Json;
using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Layout;
using Atap.Core.Widgets;
using Atap.Features.LayoutEditor;
using Atap.Notch;

[assembly: AtapFeature(typeof(LayoutEditorDescriptor))]

namespace Atap.Features.LayoutEditor;

/// <summary>Edit layout: the "layout.edit" command in the notch menu, Settings and the command bar.</summary>
internal sealed class LayoutEditorDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "layoutEditor";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings => [];

    public IReadOnlyCollection<string> EnabledBy => [];

    // Safe mode keeps the layout as it is, so a broken widget cannot be placed again from there.
    public bool IsEnabled(SettingValues settings, AppMode mode) => mode != AppMode.Safe;

    public IAppFeature Create(FeatureContext context) => new LayoutEditorFeature(
        context.Commands,
        context.Notch,
        () => LayoutEditorWindow.Open(
            new LayoutEditorViewModel(context.Layout, context.Services.Catalog, context.Text, new LayoutBackup(context.Paths.LayoutFile)),
            EditorScreen.ForNotch(context.Services.NotchHost)));
}

/// <summary>Opens one editor at a time and puts the notch in edit mode while it is open.</summary>
internal sealed class LayoutEditorFeature(ICommandRegistry commands, INotchControl notch, Func<ILayoutEditorWindow> open) : IAppFeature
{
    private ILayoutEditorWindow? _window;

    public Task StartAsync()
    {
        commands.Register(new AtapCommand(NotchMenuViewModel.EditLayoutCommandId, "layouteditor.command", _ =>
        {
            Open();
            return Task.CompletedTask;
        })
        {
            KeywordsKey = "layouteditor.keywords",
            MenuGroup = "layout",
            MenuOrder = 0,
        });
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _window?.Close();
        _window = null;
        notch.SetFlag(NotchFlag.Editing, LayoutEditorDescriptor.FeatureId, false);
        return ValueTask.CompletedTask;
    }

    private void Open()
    {
        if (_window is { } existing)
        {
            existing.Activate();
            return;
        }
        var window = open();
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_window, window))
                return;
            _window = null;
            notch.SetFlag(NotchFlag.Editing, LayoutEditorDescriptor.FeatureId, false);
        };
        _window = window;
        notch.SetFlag(NotchFlag.Editing, LayoutEditorDescriptor.FeatureId, true);
    }
}

/// <summary>The editor window as the feature sees it.</summary>
internal interface ILayoutEditorWindow
{
    event EventHandler? Closed;

    void Activate();

    void Close();
}

/// <summary>
/// layout.prev.json beside layout.json: the layout as it was before the last Done, so Settings can bring it back.
/// Written to a temp file, then moved over the old copy.
/// </summary>
internal sealed class LayoutBackup(string layoutFile)
{
    public string FilePath { get; } = Path.Combine(Path.GetDirectoryName(layoutFile) ?? ".", "layout.prev.json");

    public bool Exists => File.Exists(FilePath);

    public bool TrySave(LayoutDocument document)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(document, LayoutStore.JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public LayoutDocument? TryLoad()
    {
        try
        {
            var document = JsonSerializer.Deserialize<LayoutDocument>(File.ReadAllText(FilePath), LayoutStore.JsonOptions);
            document?.Normalize();
            return document;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
