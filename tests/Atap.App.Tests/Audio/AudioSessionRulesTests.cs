using Atap.Platform.Audio;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Audio;

namespace Atap.Tests.Audio;

/// <summary>The mixer names each app plainly and shows one row per app.</summary>
public sealed class AudioSessionRulesTests : IDisposable
{
    private const string SystemSessionId = "{0.0.0.00000000}.{6a97f3ed-8dd8-43c2-9a4c-2b4b5ddf8e1a}|#%b{A9EF3FD9-4240-455E-A4D5-F2B3301887B2}";

    // How Windows names its own sessions: a reference to a string in a system file.
    private const string ResourceName = @"@%SystemRoot%\System32\AudioSrv.Dll,-202";

    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Names_UseTheSessionName_ThenTheProgram_ThenTheProcess()
    {
        Assert.Equal("Zoom meeting", AudioSessionRules.NameOf(Session("1", display: "Zoom meeting", description: "Zoom")));
        Assert.Equal("Spotify", AudioSessionRules.NameOf(Session("2", description: "Spotify", product: "Spotify")));
        Assert.Equal("Apple Music", AudioSessionRules.NameOf(Session("3", product: "Apple Music", process: "AppleMusic")));
        Assert.Equal("game", AudioSessionRules.NameOf(Session("4", process: "game")));
    }

    [Fact]
    public void Names_NeverShowAnIdOrAResourceReference()
    {
        Assert.Equal("Teams", AudioSessionRules.NameOf(Session("1", display: ResourceName, description: "Teams")));
        Assert.Equal("Teams", AudioSessionRules.NameOf(Session("2", display: "{0.0.0.00000000}.{6a97f3ed}", description: "Teams")));
        Assert.Equal(string.Empty, AudioSessionRules.NameOf(Session(SystemSessionId)));
        Assert.Equal(string.Empty, AudioSessionRules.NameOf(Session(SystemSessionId, system: true)));
    }

    [Fact]
    public void Mixer_CallsTheSystemSessionSystemSounds_AndAnUnknownAppAnotherApp()
    {
        using var view = new AudioMixerViewModel(_services.Text, (_, _) => { }, _ => { });
        var rows = AudioSessionRules.Merge([Session(SystemSessionId, system: true, display: ResourceName), Session("x")]);

        view.Show(rows);

        Assert.Equal(["System sounds", "Another app"], view.Rows.Select(r => r.Name));
        Assert.DoesNotContain(view.Rows, r => r.Name.Contains('{', StringComparison.Ordinal));
    }

    [Fact]
    public void Merge_PutsAHelperUnderItsApp()
    {
        var rows = AudioSessionRules.Merge(
        [
            Session("music", description: "Apple Music", product: "Apple Music", process: "AppleMusic", exe: @"C:\Apps\AppleMusic.exe", volume: 70),
            Session("spotify", description: "Spotify", product: "Spotify", process: "Spotify", exe: @"C:\Apps\Spotify.exe"),
            Session("agent", description: "AMPLibraryAgent", product: "Apple Music", process: "AMPLibraryAgent", exe: @"C:\Apps\AMPLibraryAgent.exe", muted: true),
        ]);

        Assert.Equal(["Apple Music", "Spotify"], rows.Select(r => r.Name));
        Assert.Equal(["music", "agent"], AudioSessionRules.SplitIds(rows[0].Id));
        Assert.Equal(70, rows[0].VolumePercent);
        Assert.False(rows[0].Muted);
    }

    [Fact]
    public void Merge_PutsAStoreAppHelperUnderItsApp_EvenWithItsOwnProductName()
    {
        const string package = @"C:\Program Files\WindowsApps\AppleInc.AppleMusicWin_1.1540.23042.0_x64__nzyj5cx40ttqa\";
        var rows = AudioSessionRules.Merge(
        [
            Session("agent", description: "AMPLibraryAgent", product: "AMPLibraryAgent", process: "AMPLibraryAgent", exe: package + "AMPLibraryAgent.exe"),
            Session("music", description: "Apple Music", product: "Apple Music", process: "AppleMusic", exe: package + "AppleMusic.exe", volume: 70),
            Session("other", description: "Other", product: "Other", process: "Other", exe: @"C:\Program Files\WindowsApps\Other.App_2.0.0.0_x64__abc\Other.exe"),
        ]);

        Assert.Equal(["Apple Music", "Other"], rows.Select(r => r.Name));
        Assert.Equal(["agent", "music"], AudioSessionRules.SplitIds(rows[0].Id));
        Assert.Equal(70, rows[0].VolumePercent);
    }

    [Fact]
    public void Merge_JoinsSessionsOfOneProgram_ButNotWindowsPrograms()
    {
        const string windows = "Microsoft® Windows® Operating System";
        var rows = AudioSessionRules.Merge(
        [
            Session("tab1", description: "Firefox", exe: @"C:\Firefox\firefox.exe", muted: true),
            Session("tab2", description: "Firefox", exe: @"C:\FIREFOX\firefox.exe", muted: true),
            Session("player", description: "Media Player", product: windows, exe: @"C:\Windows\player.exe"),
            Session("narrator", description: "Narrator", product: windows, exe: @"C:\Windows\narrator.exe"),
        ]);

        Assert.Equal(["Firefox", "Media Player", "Narrator"], rows.Select(r => r.Name));
        Assert.True(rows[0].Muted);
        Assert.Equal(2, AudioSessionRules.SplitIds(rows[0].Id).Count);
    }

    private static AudioSessionInfo Session(
        string id,
        bool system = false,
        string? display = null,
        string? description = null,
        string? product = null,
        string? process = null,
        string? exe = null,
        int volume = 50,
        bool muted = false) =>
        new(id, system, display, description, product, process, exe, volume, muted);
}
