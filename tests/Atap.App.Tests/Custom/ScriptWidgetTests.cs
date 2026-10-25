using System.Text;
using System.Text.Json;
using Atap.Composition;
using Atap.Core.Custom;
using Atap.Core.Layout;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Script;

namespace Atap.Tests.Custom;

public sealed class ScriptWidgetTests : IDisposable
{
    private const string Id = "script#disk";
    private static readonly ScriptSpec Spec = new(@"C:\Tools\check.exe", ["--disk", "C:"]);

    private readonly TestServices _services = new();
    private readonly List<(WidgetStatus Status, string? Key)> _statuses = [];
    private readonly OwnerLifetime _lifetime;
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);
    private readonly ScriptDescriptor _descriptor = new();
    private readonly FakeRunner _runner = new();
    private readonly WidgetContext _context;

    public ScriptWidgetTests()
    {
        _lifetime = new OwnerLifetime(Id, onStatus: (status, key) => _statuses.Add((status, key)));
        _context = _services.CreateContext(Id, _descriptor, _lifetime, _visibility);
    }

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void Descriptor_RunsPrograms_AllowsSeveral_AndKeepsTheProgramOffTheSyncedSettings()
    {
        Assert.True(_descriptor.AllowsMultipleInstances);
        Assert.True(_descriptor.Capabilities.Permissions.HasFlag(WidgetPermissions.RunsPrograms));
        var program = Assert.Single(_descriptor.Settings, s => s.Key == ScriptDescriptor.ProgramKey);
        Assert.Equal(SettingType.Custom, program.Type);
        Assert.Equal(SettingScope.Machine, program.Scope);
        Assert.Equal(30, Assert.Single(_descriptor.Settings, s => s.Key == ScriptDescriptor.IntervalKey).Min);
    }

    [Fact]
    public async Task UnapprovedSpec_NeverRuns()
    {
        // A spec written straight to the state file, as a hand edit or a copy from another PC would be.
        _context.Storage.Save(ScriptStore.FileName, new ScriptState
        {
            Program = Spec.Executable,
            Arguments = [.. Spec.Arguments],
            Approval = new string('0', 64),
        }, ScriptJson.Default.ScriptState, DataScope.Machine);
        var widget = NewWidget();

        await widget.StartAsync(Settings());
        _services.Time.Advance(TimeSpan.FromMinutes(5));

        Assert.Empty(_runner.Runs);
        Assert.Equal("script.needsApproval", View(widget).MessageKey);
        Assert.Contains((WidgetStatus.NeedsApproval, "script.needsApproval"), _statuses);
        await widget.StopAsync();
    }

    [Fact]
    public async Task ApprovalFromAnotherKey_DoesNotMatch()
    {
        // The approval is right for the spec, but under a key this PC never made.
        var foreign = ScriptSpec.CreateApprovalKey();
        Assert.True(Store().Approve(Spec));
        var state = Store().Load();
        state.Approval = Spec.ComputeApproval(foreign);
        _context.Storage.Save(ScriptStore.FileName, state, ScriptJson.Default.ScriptState, DataScope.Machine);
        var widget = NewWidget();

        await widget.StartAsync(Settings());

        Assert.Empty(_runner.Runs);
        Assert.Equal("script.needsApproval", View(widget).MessageKey);
        await widget.StopAsync();
    }

    [Fact]
    public async Task ApprovedSpec_Runs_AndAChangedSpecNeedsApprovalAgain()
    {
        Assert.True(Store().Approve(Spec));
        _runner.Output = "ok";
        var widget = NewWidget();

        await widget.StartAsync(Settings());

        Assert.Equal(Spec, Assert.Single(_runner.Runs));
        Assert.Equal("ok", View(widget).Value?.DisplayText);

        // One argument changes on disk, and the old approval stays.
        var state = Store().Load();
        state.Arguments = ["--disk", "D:"];
        _context.Storage.Save(ScriptStore.FileName, state, ScriptJson.Default.ScriptState, DataScope.Machine);
        _services.Time.Advance(TimeSpan.FromMinutes(3));

        Assert.Single(_runner.Runs);
        Assert.Equal("script.needsApproval", View(widget).MessageKey);
        Assert.Null(View(widget).Value);

        // Approving the new one runs it at once.
        var changed = new ScriptSpec(Spec.Executable, ["--disk", "D:"]);
        Assert.True(Store().Approve(changed));
        Assert.Equal(changed, _runner.Runs[^1]);
        Assert.Equal(2, _runner.Runs.Count);
        await widget.StopAsync();
    }

    [Fact]
    public async Task ARemovedInstance_IsForgotten_SoTheSameIdAddedAgainRunsNothing()
    {
        Assert.True(Store().Approve(Spec));
        var keyRef = Store().Load().ApprovalKey!;
        Assert.NotNull(_context.Secrets.Get(keyRef));
        // Another script that stays, and another widget type's state, which must never be touched.
        var kept = new ScriptStore("script#kept", new WidgetStorage("script#kept", _services.Paths), _context.Secrets);
        Assert.True(kept.Approve(Spec));
        var todo = new WidgetStorage("todo", _services.Paths);
        todo.Save("items", new ScriptLastRun(), ScriptJson.Default.ScriptLastRun, DataScope.Machine);

        var layout = new LayoutDocument { EnabledWidgets = ["clock"], Profiles = [new LayoutProfile { Id = "work", EnabledWidgets = ["script#kept"] }] };
        var forgotten = ScriptStore.ForgetRemoved(_services.Paths.MachineState, ScriptStore.InstancesIn(layout), _context.Secrets);

        Assert.Equal([Id], forgotten);
        Assert.Null(_context.Secrets.Get(keyRef));
        Assert.Equal(Spec, kept.LoadApproved());
        Assert.True(File.Exists(Path.Combine(_services.Paths.MachineState, "todo", "items.json")));

        // The same id comes back, as the layout editor or a synced layout would give it.
        var widget = NewWidget();
        await widget.StartAsync(Settings());
        _services.Time.Advance(TimeSpan.FromMinutes(5));

        Assert.Empty(_runner.Runs);
        Assert.Equal("script.notSetUp", View(widget).MessageKey);
        await widget.StopAsync();
    }

    [Fact]
    public void Approve_NeverUsesOrDeletesAnotherWidgetsSecret()
    {
        var other = _context.Secrets.Put("https://calendar.example/private.ics");
        _context.Storage.Save(ScriptStore.FileName, new ScriptState { ApprovalKey = other }, ScriptJson.Default.ScriptState, DataScope.Machine);

        Assert.True(Store().Approve(Spec));

        Assert.Equal("https://calendar.example/private.ics", _context.Secrets.Get(other));
        Assert.NotEqual(other, Store().Load().ApprovalKey);
        Assert.Equal(Spec, Store().LoadApproved());

        // Cleanup leaves it too, when a state file points at it.
        _context.Storage.Save(ScriptStore.FileName, new ScriptState { ApprovalKey = other }, ScriptJson.Default.ScriptState, DataScope.Machine);
        ScriptStore.ForgetRemoved(_services.Paths.MachineState, new HashSet<string>(), _context.Secrets);
        Assert.Equal("https://calendar.example/private.ics", _context.Secrets.Get(other));
    }

    [Fact]
    public async Task ARestart_DoesNotRunAgainWithin30Seconds_ButApproveDoes()
    {
        Assert.True(Store().Approve(Spec));
        var first = NewWidget();
        await first.StartAsync(Settings());
        await first.StopAsync();
        Assert.Single(_runner.Runs);

        // A restart, as after a settings change, 10 seconds later.
        _services.Time.Advance(TimeSpan.FromSeconds(10));
        var second = NewWidget();
        await second.StartAsync(Settings());
        Assert.Single(_runner.Runs);

        // It runs once 30 seconds have passed since the last run.
        _services.Time.Advance(TimeSpan.FromSeconds(19));
        Assert.Single(_runner.Runs);
        _services.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, _runner.Runs.Count);

        // Approve runs at once, even right after a run.
        _services.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(Store().Approve(new ScriptSpec(Spec.Executable, ["--disk", "D:"])));
        Assert.Equal(3, _runner.Runs.Count);
        await second.StopAsync();
    }

    [Fact]
    public async Task SafeMode_NeverRuns()
    {
        Assert.True(Store().Approve(Spec));
        var widget = NewWidget(safeMode: true);

        await widget.StartAsync(Settings());
        _services.Time.Advance(TimeSpan.FromMinutes(5));
        Store().Approve(new ScriptSpec(Spec.Executable));

        Assert.Empty(_runner.Runs);
        Assert.Equal("script.safeMode", View(widget).MessageKey);
        await widget.StopAsync();
    }

    [Fact]
    public void Interval_IsNeverUnder30Seconds()
    {
        // Without the definition's minimum, as from a file the settings store did not check.
        var raw = new SettingValues([], new Dictionary<string, JsonElement> { [ScriptDescriptor.IntervalKey] = JsonSerializer.SerializeToElement(5) });

        Assert.Equal(TimeSpan.FromSeconds(30), ScriptWidget.IntervalFrom(raw));
        Assert.Equal(TimeSpan.FromSeconds(30), ScriptWidget.IntervalFrom(Settings(intervalSeconds: 5)));
        Assert.Equal(TimeSpan.FromMinutes(1), ScriptWidget.IntervalFrom(Settings()));
        Assert.Equal(TimeSpan.FromMinutes(5), ScriptWidget.IntervalFrom(Settings(intervalSeconds: 300)));
    }

    [Fact]
    public async Task AShortInterval_RunsNoMoreThanEvery30Seconds()
    {
        Assert.True(Store().Approve(Spec));
        var widget = NewWidget();

        await widget.StartAsync(Settings(intervalSeconds: 5));
        Assert.Single(_runner.Runs);

        _services.Time.Advance(TimeSpan.FromSeconds(29));
        Assert.Single(_runner.Runs);

        _services.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, _runner.Runs.Count);
        await widget.StopAsync();
    }

    [Fact]
    public void Output_IsReadUpTo64KB()
    {
        var max = ScriptSpec.MaxOutputBytes;
        Assert.Equal(64 * 1024, max);

        var (exact, exactOver) = ScriptProcessRunner.ReadCapped(new MemoryStream(new byte[max]), max);
        Assert.False(exactOver);
        Assert.Equal(max, exact.Length);

        var large = new CountingStream(10 * max);
        var (over, overCap) = ScriptProcessRunner.ReadCapped(large, max);
        Assert.True(overCap);
        Assert.Empty(over);
        // It stops one byte past the cap and never reads the rest.
        Assert.Equal(max + 1, large.Consumed);
    }

    [Fact]
    public async Task RealRunner_RunsABuiltInProgramInAJob()
    {
        // hostname.exe prints one line and ends. It is not a shell.
        var hostname = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "hostname.exe");

        var result = await new ScriptProcessRunner().RunAsync(new ScriptSpec(hostname), CancellationToken.None);

        Assert.Equal(ScriptRunStatus.Done, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Environment.MachineName, Encoding.UTF8.GetString(result.Output.Span).Trim(), ignoreCase: true);
    }

    [Fact]
    public void FinalPaths_OnAShareOrWithoutADriveLetter_AreRefused()
    {
        Assert.Equal(@"C:\Tools\check.exe", ScriptProcessRunner.FromFinalPath(@"\\?\C:\Tools\check.exe"));
        Assert.Null(ScriptProcessRunner.FromFinalPath(@"\\?\UNC\server\share\check.exe"));
        Assert.Null(ScriptProcessRunner.FromFinalPath(@"\\server\share\check.exe"));
        Assert.Null(ScriptProcessRunner.FromFinalPath(@"\\?\Volume{0b1c2d3e-0000-0000-0000-000000000000}\check.exe"));
    }

    [Fact]
    public void ResolveLocal_FollowsLinks_AndRefusesMissingPaths()
    {
        var folder = Directory.CreateTempSubdirectory("atap-script-").FullName;
        try
        {
            var target = Path.Combine(folder, "real");
            Directory.CreateDirectory(target);
            var file = Path.Combine(target, "check.exe");
            File.WriteAllBytes(file, []);

            Assert.Equal(file, ScriptProcessRunner.ResolveLocal(file), ignoreCase: true);
            Assert.Null(ScriptProcessRunner.ResolveLocal(Path.Combine(folder, "missing.exe")));

            // A folder link needs Developer Mode or admin rights. Without them only the plain path is checked.
            var link = Path.Combine(folder, "link");
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
            Assert.Equal(file, ScriptProcessRunner.ResolveLocal(Path.Combine(link, "check.exe")), ignoreCase: true);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Editor_WarnsAboutOneDrive_WithoutRefusing()
    {
        var editor = new ScriptEditorViewModel(Store(), _services.Text, picker: null, cloudFolders: [@"C:\Users\a\OneDrive"]);

        editor.Program = @"C:\Tools\check.exe";
        Assert.False(editor.HasCloudWarning);

        editor.Arguments = @"-File=C:\Users\a\OneDriveOther\check.ps1";
        Assert.False(editor.HasCloudWarning);

        editor.Arguments = @"-File=C:\Users\a\onedrive\check.ps1";
        Assert.True(editor.HasCloudWarning);
        Assert.True(editor.ApproveCommand.CanExecute(null));

        editor.Arguments = string.Empty;
        editor.WorkingFolder = @"C:\Users\a\OneDrive";
        Assert.True(editor.HasCloudWarning);

        editor.WorkingFolder = string.Empty;
        editor.Program = @"C:\Users\a\OneDrive\Tools\check.exe";
        Assert.True(editor.HasCloudWarning);
    }

    [Fact]
    public async Task TooMuchOutput_SaysSo()
    {
        Assert.True(Store().Approve(Spec));
        _runner.Result = new ScriptRunResult(ScriptRunStatus.TooMuchOutput);
        var widget = NewWidget();

        await widget.StartAsync(Settings());

        Assert.Equal("script.tooMuch", View(widget).MessageKey);
        Assert.Equal("It printed too much, so Atap stopped it.", View(widget).Message);
        await widget.StopAsync();
    }

    [Fact]
    public async Task ACriticalStatus_GoesLive()
    {
        Assert.True(Store().Approve(Spec));
        _runner.Output = "{\"state\": \"down\"}";
        var settings = new SettingValues(_descriptor.Settings, new Dictionary<string, JsonElement>
        {
            [ScriptDescriptor.ShowKey] = JsonSerializer.SerializeToElement(ScriptDescriptor.ShowStatus),
            [ScriptDescriptor.PathKey] = JsonSerializer.SerializeToElement("$.state"),
        });
        var widget = NewWidget();

        await widget.StartAsync(settings);

        Assert.True(widget.IsLive);
        Assert.Equal(ValueLevel.Critical, View(widget).Value?.Level);
        await widget.StopAsync();
        Assert.False(widget.IsLive);
    }

    [Fact]
    public void Editor_ShowsWhatWillRun_AndApprovesExactlyThat()
    {
        var editor = new ScriptEditorViewModel(Store(), _services.Text, picker: null);
        Assert.False(editor.ApproveCommand.CanExecute(null));

        editor.Program = "check.exe";
        Assert.True(editor.HasProblem);
        Assert.False(editor.ApproveCommand.CanExecute(null));

        editor.Program = @"C:\Tools\check.exe";
        editor.Arguments = "--disk\r\nC:\r\n";
        Assert.False(editor.HasProblem);
        Assert.Equal(
            [@"Program: C:\Tools\check.exe", "Option 1: \"--disk\"", "Option 2: \"C:\"", @"Starts in: C:\Tools"],
            editor.Review);
        Assert.Null(Store().LoadApproved());

        editor.ApproveCommand.Execute(null);
        Assert.Equal(Spec, Store().LoadApproved());
        Assert.True(editor.IsApproved);

        // An edit is not saved, and the approved spec keeps running until the new one is approved.
        editor.Arguments = "--disk\nD:";
        Assert.False(editor.IsApproved);
        Assert.True(editor.ApproveCommand.CanExecute(null));
        Assert.Equal(Spec, Store().LoadApproved());

        editor.RemoveCommand.Execute(null);
        Assert.Null(Store().LoadApproved());
        Assert.Equal(string.Empty, editor.Program);
    }

    private ScriptStore Store() => new(Id, _context.Storage, _context.Secrets);

    private ScriptWidget NewWidget(bool safeMode = false) => new(_context, _runner, () => safeMode);

    private static ScriptViewModel View(ScriptWidget widget) => Assert.IsType<ScriptViewModel>(widget.ViewModel);

    private SettingValues Settings(int? intervalSeconds = null) => new(
        _descriptor.Settings,
        intervalSeconds is { } seconds
            ? new Dictionary<string, JsonElement> { [ScriptDescriptor.IntervalKey] = JsonSerializer.SerializeToElement(seconds) }
            : null);

    private sealed class FakeRunner : IScriptRunner
    {
        public List<ScriptSpec> Runs { get; } = [];

        public string Output { get; set; } = string.Empty;

        public ScriptRunResult? Result { get; set; }

        public Task<ScriptRunResult> RunAsync(ScriptSpec spec, CancellationToken cancellationToken)
        {
            Runs.Add(spec);
            return Task.FromResult(Result ?? new ScriptRunResult(ScriptRunStatus.Done, Encoding.UTF8.GetBytes(Output), 0));
        }
    }

    /// <summary>A stream of zeros that counts how much was read.</summary>
    private sealed class CountingStream(long length) : Stream
    {
        public long Consumed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get => Consumed; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = (int)Math.Min(count, length - Consumed);
            Array.Clear(buffer, offset, take);
            Consumed += take;
            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
