using System.Text.Json;
using Atap.Core.Layout;
using Atap.Core.Sync;
using Atap.Core.Widgets;
using Atap.Features.Sync;
using Atap.Platform.OneDrive;
using Atap.Settings.Cards.General.Sync;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Sync;

/// <summary>Two PCs, each with its own temp folder, sharing one sync folder as OneDrive would.</summary>
public sealed class SyncFeatureTests : IDisposable
{
    private const string OtherDevice = "9f8c3a52-7d1e-4c6b-a0e2-5b3f1d2c4e6a";

    private static readonly SettingDefinition[] Definitions =
    [
        new("clock.style", "x", SettingType.Choice, "plain", ["plain", "bold", "compact"]),
        new("malaysia.state", "x", SettingType.String, string.Empty),
        new("localApi.enabled", "x", SettingType.Bool, false) { Scope = SettingScope.Machine },
        new("startup.enabled", "x", SettingType.Bool, false) { Scope = SettingScope.Machine },
        new(SyncDescriptor.EnabledKey, "x", SettingType.Bool, false) { Scope = SettingScope.Machine },
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "Atap.Tests", "onedrive-" + Guid.NewGuid().ToString("N"));
    private readonly List<Pc> _pcs = [];

    private string Folder => Path.Combine(_root, SyncFileNames.FolderUnderOneDrive);

    private string BundlePath => Path.Combine(Folder, SyncFileNames.Bundle);

    public void Dispose()
    {
        foreach (var pc in _pcs)
            pc.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task TwoPcs_ConvergeThroughTheSyncFolder_AndMachineKeysStayHome()
    {
        var a = NewPc();
        var b = NewPc();
        a.Set("clock.style", "bold");
        a.Set("localApi.enabled", true);

        await a.Feature.StartAsync();
        Assert.True(File.Exists(BundlePath));
        Assert.DoesNotContain("localApi", File.ReadAllText(BundlePath), StringComparison.Ordinal);

        b.Services.Time.Advance(TimeSpan.FromMinutes(1));
        await b.Feature.StartAsync();
        Assert.Equal("bold", b.Get("clock.style"));
        Assert.False(b.Services.Settings.Values.Raw.ContainsKey("localApi.enabled"));

        // An edit on B waits 5 quiet seconds, then goes out. A takes it 300 ms after the folder changes.
        b.Set("malaysia.state", "JHR");
        var before = File.ReadAllText(BundlePath);
        b.Services.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(before, File.ReadAllText(BundlePath));
        b.Services.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Contains("malaysia.state", File.ReadAllText(BundlePath), StringComparison.Ordinal);

        a.Services.Time.Advance(TimeSpan.FromMinutes(2));
        a.Watch.Fire();
        Assert.Null(a.Get("malaysia.state"));
        a.Services.Time.Advance(SyncFeature.SettleDelay);
        Assert.Equal("JHR", a.Get("malaysia.state"));

        // An edit on A comes back the other way.
        a.Set("clock.style", "compact");
        a.Services.Time.Advance(SyncFeature.ExportDelay);
        b.Watch.Fire();
        b.Services.Time.Advance(SyncFeature.SettleDelay);
        Assert.Equal("compact", b.Get("clock.style"));

        // Settled: neither PC writes again, and A kept its Machine key.
        var settled = File.ReadAllText(BundlePath);
        a.Feature.RunPass();
        b.Feature.RunPass();
        Assert.Equal(settled, File.ReadAllText(BundlePath));
        Assert.True(a.Services.Settings.Values.GetBool("localApi.enabled"));
        Assert.Equal(SyncHealth.Working, SyncStatus.Health);
    }

    [Fact]
    public async Task ABundle_CannotChangeMachineKeys_OrPlacement()
    {
        var a = NewPc();
        var placement = JsonSerializer.Serialize(a.Services.Layout.Document.Placement, LayoutStore.JsonOptions);
        var later = a.Services.Time.GetUtcNow().AddDays(1);
        WriteBundle(BundlePath, later,
            ("settings:localApi.enabled", "true"),
            ("settings:startup.enabled", "true"),
            ("settings:sync.enabled", "true"),
            ("settings:sync.folder", "\"C:\\\\Elsewhere\""),
            (SyncUnitNames.Placement, """{"anchor":"leftEdge","offset":300}"""),
            ("settings:clock.style", "\"bold\""));

        await a.Feature.StartAsync();

        Assert.Equal("bold", a.Get("clock.style"));
        foreach (var key in new[] { "localApi.enabled", "startup.enabled", "sync.enabled", "sync.folder" })
            Assert.False(a.Services.Settings.Values.Raw.ContainsKey(key), key);
        Assert.Equal(placement, JsonSerializer.Serialize(a.Services.Layout.Document.Placement, LayoutStore.JsonOptions));
        // The bundle A writes back drops them too.
        var written = File.ReadAllText(BundlePath);
        Assert.DoesNotContain("localApi", written, StringComparison.Ordinal);
        Assert.DoesNotContain(SyncUnitNames.Placement, written, StringComparison.Ordinal);
        Assert.DoesNotContain("sync.enabled", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AValueThatDoesNotFitItsSetting_IsNotApplied()
    {
        var a = NewPc();
        a.Set("clock.style", "bold");
        WriteBundle(BundlePath, a.Services.Time.GetUtcNow().AddDays(1), ("settings:clock.style", "\"neon\""));

        await a.Feature.StartAsync();

        Assert.Equal("bold", a.Get("clock.style"));
        Assert.Contains(a.Log, line => line.Contains("settings:clock.style", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConflictCopies_AreMerged_ThenDeleted()
    {
        var a = NewPc();
        a.Set("clock.style", "bold");
        await a.Feature.StartAsync();

        var copy = Path.Combine(Folder, "atap-sync-DESKTOP-B.json");
        WriteBundle(copy, a.Services.Time.GetUtcNow().AddMinutes(1), ("settings:malaysia.state", "\"KDH\""));
        WaitUntilReadable(copy);
        a.Services.Time.Advance(TimeSpan.FromMinutes(2));
        a.Watch.Fire();
        a.Services.Time.Advance(SyncFeature.SettleDelay);

        Assert.Equal("KDH", a.Get("malaysia.state"));
        Assert.False(File.Exists(copy));
        Assert.Contains("malaysia.state", File.ReadAllText(BundlePath), StringComparison.Ordinal);
        Assert.Contains("clock.style", File.ReadAllText(BundlePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACopyThatWillNotOpen_IsTriedAgainSoon_WithoutAFolderEvent()
    {
        var a = NewPc();
        await a.Feature.StartAsync();
        var copy = Path.Combine(Folder, "atap-sync-DESKTOP-B.json");
        WriteBundle(copy, a.Services.Time.GetUtcNow().AddMinutes(1), ("settings:malaysia.state", "\"JHR\""));
        WaitUntilReadable(copy);
        a.Services.Time.Advance(TimeSpan.FromMinutes(2));

        using (new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            a.Watch.Fire();
            a.Services.Time.Advance(SyncFeature.SettleDelay);
            Assert.Null(a.Get("malaysia.state"));
        }

        a.Services.Time.Advance(SyncFeature.CopyRetryDelay);

        Assert.Equal("JHR", a.Get("malaysia.state"));
        Assert.False(File.Exists(copy));
    }

    [Fact]
    public async Task AFileThatWillNotOpen_IsTriedAgainAtTheNextFolderEvent()
    {
        var a = NewPc();
        await a.Feature.StartAsync();
        WriteBundle(BundlePath, a.Services.Time.GetUtcNow().AddMinutes(1), ("settings:malaysia.state", "\"PRK\""));

        using (new FileStream(BundlePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            a.Services.Time.Advance(TimeSpan.FromMinutes(2));
            a.Watch.Fire();
            a.Services.Time.Advance(SyncFeature.SettleDelay);
            Assert.Null(a.Get("malaysia.state"));
            Assert.Equal(SyncHealth.Waiting, SyncStatus.Health);
        }

        a.Watch.Fire();
        a.Services.Time.Advance(SyncFeature.SettleDelay);
        Assert.Equal("PRK", a.Get("malaysia.state"));
        Assert.Equal(SyncHealth.Working, SyncStatus.Health);
    }

    [Fact]
    public async Task FirstTurnOn_KeepThisPc_BacksUpTheOneDriveFile_AndThisPcWins()
    {
        var a = NewPc();
        a.Set("clock.style", "bold");
        WriteBundle(BundlePath, a.Services.Time.GetUtcNow().AddDays(1), ("settings:clock.style", "\"plain\""));
        var onedrive = File.ReadAllText(BundlePath);
        using var card = a.Card();

        card.IsOn = true;
        Assert.True(card.IsAsking);
        Assert.False(card.IsOn);

        card.KeepThisPcCommand.Execute(null);

        Assert.True(card.IsOn);
        Assert.False(card.IsAsking);
        var backup = Assert.Single(Directory.GetDirectories(a.Services.Paths.Roaming, "backup-*"));
        Assert.Equal(onedrive, File.ReadAllText(Path.Combine(backup, SyncFileNames.Bundle)));
        Assert.Contains(backup, card.Message, StringComparison.Ordinal);

        await a.Feature.StartAsync();
        Assert.Equal("bold", a.Get("clock.style"));
        Assert.Contains("\"bold\"", File.ReadAllText(BundlePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FirstTurnOn_UseOneDrive_BacksUpThisPcsFiles_AndTakesTheOneDriveCopy()
    {
        var a = NewPc();
        a.Set("clock.style", "bold");
        WriteBundle(BundlePath, a.Services.Time.GetUtcNow().AddMinutes(-5), ("settings:clock.style", "\"compact\""));
        using var card = a.Card();

        card.IsOn = true;
        card.UseOneDriveCommand.Execute(null);

        Assert.True(card.IsOn);
        var backup = Assert.Single(Directory.GetDirectories(a.Services.Paths.Roaming, "backup-*"));
        Assert.Contains("\"bold\"", File.ReadAllText(Path.Combine(backup, "settings.json")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(backup, "layout.json")));

        // The OneDrive copy is older than the local edit, and still wins.
        await a.Feature.StartAsync();
        Assert.Equal("compact", a.Get("clock.style"));
    }

    [Fact]
    public void TurningOn_WithoutABundle_DoesNotAsk_AndCancelLeavesItOff()
    {
        var a = NewPc();
        using var card = a.Card();

        card.IsOn = true;
        Assert.False(card.IsAsking);
        Assert.True(card.IsOn);
        Assert.Empty(Directory.GetDirectories(a.Services.Paths.Roaming, "backup-*"));

        card.IsOn = false;
        Directory.CreateDirectory(Folder);
        WriteBundle(BundlePath, a.Services.Time.GetUtcNow(), ("settings:clock.style", "\"bold\""));
        card.IsOn = true;
        Assert.True(card.IsAsking);
        card.CancelCommand.Execute(null);
        Assert.False(card.IsAsking);
        Assert.False(card.IsOn);
    }

    [Fact]
    public void TheCard_ShowsTheToggle_AndTheChoiceOnlyWhileAsking() => Sta.Run(() =>
    {
        var a = NewPc();
        using var card = a.Card();
        var host = Atap.Tests.Settings.SettingsUi.Render(card, new SyncCard().ViewsSource);

        var toggle = Assert.Single(Atap.Tests.Settings.SettingsUi.Find<System.Windows.Controls.CheckBox>(host));
        Assert.Equal("Sync my settings", System.Windows.Automation.AutomationProperties.GetName(toggle));
        Assert.DoesNotContain(Atap.Tests.Settings.SettingsUi.Visible<System.Windows.Controls.Button>(host), b => Equals(b.Content, "Use the settings from OneDrive"));

        WriteBundle(BundlePath, a.Services.Time.GetUtcNow(), ("settings:clock.style", "\"bold\""));
        card.IsOn = true;
        Atap.Tests.Settings.SettingsUi.Layout(host);
        Assert.Contains(Atap.Tests.Settings.SettingsUi.Visible<System.Windows.Controls.Button>(host), b => Equals(b.Content, "Use the settings from OneDrive"));
        Assert.Contains(Atap.Tests.Settings.SettingsUi.Visible<System.Windows.Controls.Button>(host), b => Equals(b.Content, "Keep this PC's settings"));
    });

    private Pc NewPc()
    {
        var pc = new Pc(Folder);
        _pcs.Add(pc);
        return pc;
    }

    /// <summary>
    /// Waits until nothing else holds <paramref name="path"/>. On CI a virus scan can read a new file for a
    /// moment, which made a test that expects the file to open at once fail now and then.
    /// </summary>
    private static void WaitUntilReadable(string path)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < until)
            {
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>A bundle from another PC, every unit stamped <paramref name="stamp"/>.</summary>
    private static void WriteBundle(string path, DateTimeOffset stamp, params (string Name, string Json)[] units)
    {
        var version = new SyncVersion(SyncVersion.Truncate(stamp), OtherDevice);
        var bundle = new SyncBundle(OtherDevice, units.ToDictionary(
            u => u.Name,
            u => new SyncBundleUnit(version, JsonElement.Parse(u.Json)),
            StringComparer.Ordinal));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, SyncBundleWriter.Write(bundle));
    }

    private sealed class Pc : IDisposable
    {
        private readonly string _folder;
        private readonly SyncScope _scope = SyncDefinitions.Scope(Definitions, []);
        private SyncFeature? _feature;

        public Pc(string folder)
        {
            _folder = folder;
            Services.Settings.Register(Definitions);
            Services.Settings.Load();
            Services.Layout.Load();
            Stores = new SyncStores(Services.Settings, Services.Layout, Definitions, _ => null);
        }

        public TestServices Services { get; } = new();

        public SyncStores Stores { get; }

        public FakeWatch Watch { get; } = new();

        public List<string> Log { get; } = [];

        public SyncFeature Feature => _feature ??= new SyncFeature(
            Stores,
            _scope,
            new SyncLocalFiles(Services.Paths),
            () => _folder,
            Watch,
            Services.Scheduler,
            Services.Time,
            new InlineContext(),
            [],
            (message, _) => Log.Add(message));

        public void Set(string key, object value) => Assert.NotEqual(Atap.Core.Settings.SettingWriteResult.Invalid, Services.Settings.Set(key, value));

        public string? Get(string key) =>
            Services.Settings.Values.Raw.TryGetValue(key, out var value) ? value.GetString() : null;

        public SyncCardViewModel Card() => new(
            Services.Settings,
            new SyncSetup(Stores, _scope, new SyncLocalFiles(Services.Paths), Services.Time),
            () => _folder,
            Services.Text,
            picker: null);

        public void Dispose()
        {
            _feature?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Services.Layout.Dispose();
            Services.Settings.Dispose();
            Services.Dispose();
        }
    }

    internal sealed class FakeWatch : ISyncFolderWatch
    {
        private readonly List<Action> _callbacks = [];

        public IDisposable? Watch(string folder, Action changed)
        {
            _callbacks.Add(changed);
            return new Stop(() => _callbacks.Remove(changed));
        }

        public void Fire()
        {
            foreach (var callback in _callbacks.ToList())
                callback();
        }

        private sealed class Stop(Action stop) : IDisposable
        {
            public void Dispose() => stop();
        }
    }

    /// <summary>Runs posted work at once, as if it were already on the UI thread.</summary>
    private sealed class InlineContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }
}
