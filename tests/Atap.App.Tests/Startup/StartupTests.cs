using Atap.Core.Sync;
using Atap.Core.Widgets;
using Atap.Features.SettingsWindow;
using Atap.Features.Startup;
using Atap.Platform.Startup;
using Atap.Tests.Infrastructure;
using Microsoft.Win32;

namespace Atap.Tests.Startup;

public sealed class StartupTests : IDisposable
{
    private const string Exe = @"C:\Users\amin\AppData\Local\Atap\Atap.exe";

    private readonly TestServices _services = new();
    private readonly FakeRunKey _key = new();
    private readonly List<string> _log = [];

    public StartupTests()
    {
        _services.Settings.Load();
        _services.Settings.Register(new StartupDescriptor().Settings);
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Register_WritesTheQuotedPathOnce()
    {
        var startup = new RunKeyStartup(_key, Exe);

        startup.Register();
        startup.Register();

        Assert.Equal($"\"{Exe}\"", _key.Values[RunKeyStartup.ValueName]);
        Assert.Equal(1, _key.Writes);
        Assert.True(startup.IsRegistered);
    }

    [Fact]
    public void Register_FixesAnEntryForAnotherPath()
    {
        _key.Values[RunKeyStartup.ValueName] = "\"D:\\Old\\Atap.exe\"";
        var startup = new RunKeyStartup(_key, Exe);
        Assert.False(startup.IsRegistered);

        startup.Register();

        Assert.Equal($"\"{Exe}\"", _key.Values[RunKeyStartup.ValueName]);
    }

    [Fact]
    public void Unregister_RemovesTheEntry_AndDoesNothingWhenItIsGone()
    {
        var startup = new RunKeyStartup(_key, Exe);
        startup.Register();

        startup.Unregister();
        startup.Unregister();

        Assert.Empty(_key.Values);
        Assert.Equal(1, _key.Deletes);
    }

    [Theory]
    [InlineData(@"C:\Users\amin\AppData\Local\Atap\current\Atap.exe", true, @"C:\Users\amin\AppData\Local\Atap\Atap.exe")]
    [InlineData(@"C:\Users\amin\AppData\Local\Atap\current\Atap.exe", false, @"C:\Users\amin\AppData\Local\Atap\current\Atap.exe")]
    [InlineData(@"D:\Tools\Atap\Atap.exe", true, @"D:\Tools\Atap\Atap.exe")]
    [InlineData(@"C:\Users\amin\scoop\apps\atap\current\Atap.exe", false, @"C:\Users\amin\scoop\apps\atap\current\Atap.exe")]
    public void LaunchPath_PrefersTheVelopackStub(string process, bool stubExists, string expected)
    {
        Assert.Equal(expected, RunKeyStartup.LaunchPath(process, path => stubExists && path == expected));
    }

    [Fact]
    public void Repair_AddsAMissingEntry_AndRepointsOneWhoseExeIsGone_ButLeavesAWorkingOne()
    {
        const string other = @"D:\Portable\Atap.exe";
        var exists = false;
        var startup = new RunKeyStartup(_key, Exe, path => exists && path == other);

        startup.Repair();
        Assert.Equal($"\"{Exe}\"", _key.Values[RunKeyStartup.ValueName]);

        _key.Values[RunKeyStartup.ValueName] = $"\"{other}\"";
        exists = true;
        startup.Repair();
        Assert.Equal($"\"{other}\"", _key.Values[RunKeyStartup.ValueName]);

        exists = false;
        startup.Repair();
        Assert.Equal($"\"{Exe}\"", _key.Values[RunKeyStartup.ValueName]);
    }

    [Fact]
    public async Task Feature_AtStart_LeavesTheEntryAlone_WhenNoChoiceIsSaved()
    {
        // Defaults after an unreadable or recovered settings file must not switch Start with Windows off.
        _key.Values[RunKeyStartup.ValueName] = $"\"{Exe}\"";
        var feature = new StartupFeature(_services.Settings, new RunKeyStartup(_key, Exe), (m, e) => _log.Add(m));

        await feature.StartAsync();

        Assert.Equal($"\"{Exe}\"", _key.Values[RunKeyStartup.ValueName]);
        Assert.Equal(0, _key.Deletes);
    }

    [Fact]
    public async Task Feature_AtStart_DoesNotTakeOverAWorkingEntry()
    {
        const string installed = @"C:\Users\amin\AppData\Local\Atap\Atap.exe";
        _key.Values[RunKeyStartup.ValueName] = $"\"{installed}\"";
        _services.Settings.Set(StartupDescriptor.EnabledKey, true);
        var portable = new RunKeyStartup(_key, @"D:\Downloads\Atap\Atap.exe", path => path == installed);
        var feature = new StartupFeature(_services.Settings, portable, (m, e) => _log.Add(m));

        await feature.StartAsync();

        Assert.Equal($"\"{installed}\"", _key.Values[RunKeyStartup.ValueName]);
    }

    [Theory]
    [InlineData(@"""C:\Users\amin\AppData\Local\Atap\Atap.exe""", true)]
    [InlineData(@"""C:\Users\amin\AppData\Local\AtapOther\Atap.exe""", false)]
    [InlineData(@"""D:\Portable\Atap.exe""", false)]
    public void Uninstall_RemovesOnlyAnEntryInsideThisInstall(string value, bool removed)
    {
        _key.Values[RunKeyStartup.ValueName] = value;

        Atap.Composition.UninstallCleanup.RemoveFromStartup(_key, @"C:\Users\amin\AppData\Local\Atap");

        Assert.Equal(removed, !_key.Values.ContainsKey(RunKeyStartup.ValueName));
    }

    [Fact]
    public async Task Feature_FollowsTheSetting()
    {
        _key.Values[RunKeyStartup.ValueName] = "\"stale\"";
        var feature = new StartupFeature(_services.Settings, new RunKeyStartup(_key, Exe), (m, e) => _log.Add(m));

        await feature.StartAsync();
        // No choice saved yet, so the entry is left as it is.
        Assert.Equal("\"stale\"", _key.Values[RunKeyStartup.ValueName]);

        _services.Settings.Set(StartupDescriptor.EnabledKey, true);
        Assert.Equal($"\"{Exe}\"", _key.Values[RunKeyStartup.ValueName]);

        _services.Settings.Set(StartupDescriptor.EnabledKey, false);
        Assert.Empty(_key.Values);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task Feature_StoppingLeavesTheEntry_AndIgnoresOtherSettings()
    {
        var feature = new StartupFeature(_services.Settings, new RunKeyStartup(_key, Exe), (m, e) => _log.Add(m));
        await feature.StartAsync();
        _services.Settings.Set(StartupDescriptor.EnabledKey, true);
        var writes = _key.Writes;

        _services.Settings.Set("other.key", 5);
        Assert.Equal(writes, _key.Writes);

        await feature.DisposeAsync();
        _services.Settings.Set(StartupDescriptor.EnabledKey, false);

        // Quitting Atap must not change whether it starts with Windows.
        Assert.Equal($"\"{Exe}\"", _key.Values[RunKeyStartup.ValueName]);
    }

    [Fact]
    public async Task Feature_LogsWhenWindowsRefuses()
    {
        _key.Refuse = true;
        _services.Settings.Set(StartupDescriptor.EnabledKey, true);
        var feature = new StartupFeature(_services.Settings, new RunKeyStartup(_key, Exe), (m, e) => _log.Add(m));

        await feature.StartAsync();

        Assert.Single(_log);
    }

    [Fact]
    public void TheSetting_IsOff_StaysOnThisPc_AndNeverSyncs()
    {
        var setting = Assert.Single(new StartupDescriptor().Settings);

        Assert.Equal(StartupDescriptor.EnabledKey, setting.Key);
        Assert.Equal(false, setting.DefaultValue);
        Assert.Equal(SettingScope.Machine, setting.Scope);
        Assert.Contains(setting.Key, SyncScope.AlwaysMachineKeys);
        Assert.Equal(WindowsStartup.IsSupported ? Atap.Composition.SettingsSections.General : null, setting.SectionId);
    }

    [Fact]
    public void ShowAtap_NeverHidesTheNotchInSafeMode()
    {
        var descriptor = new ShowAtapDescriptor();
        _services.Settings.Register(descriptor.Settings);
        _services.Settings.Set(ShowAtapDescriptor.ShowKey, false);

        Assert.True(descriptor.IsEnabled(_services.Settings.Values, Atap.Composition.AppMode.Normal));
        Assert.False(descriptor.IsEnabled(_services.Settings.Values, Atap.Composition.AppMode.Safe));
    }

    [Fact]
    public void CurrentUserRunKey_ReadsWritesAndDeletes()
    {
        var subKey = $@"Software\Atap.Tests\{Guid.NewGuid():N}";
        try
        {
            var key = new CurrentUserRunKey(subKey);
            Assert.Null(key.Get("Atap"));

            key.Set("Atap", "\"C:\\Atap.exe\"");
            Assert.Equal("\"C:\\Atap.exe\"", key.Get("Atap"));

            key.Delete("Atap");
            key.Delete("Atap");
            Assert.Null(key.Get("Atap"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            bool empty;
            using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\Atap.Tests"))
                empty = parent is { SubKeyCount: 0, ValueCount: 0 };
            if (empty)
                Registry.CurrentUser.DeleteSubKey(@"Software\Atap.Tests", throwOnMissingSubKey: false);
        }
    }

    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Writes { get; private set; }

        public int Deletes { get; private set; }

        public bool Refuse { get; set; }

        public string? Get(string name) => Values.GetValueOrDefault(name);

        public void Set(string name, string value)
        {
            if (Refuse)
                throw new UnauthorizedAccessException("No.");
            Writes++;
            Values[name] = value;
        }

        public void Delete(string name)
        {
            Deletes++;
            Values.Remove(name);
        }
    }
}
