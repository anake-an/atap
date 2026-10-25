using Atap.Composition;
using Atap.Core.Widgets;
using Atap.Platform.Audio;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Audio;

namespace Atap.Tests.Audio;

public sealed class AudioWidgetsTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new("audio");
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);
    private readonly FakeDevices _devices = new();

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task Output_WhenWindowsRefuses_OpensSoundSettings()
    {
        var descriptor = new AudioOutputDescriptor();
        var widget = new AudioOutputWidget(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility), _devices);
        await widget.StartAsync(SettingValues.Empty);
        var view = Assert.IsType<AudioOutputViewModel>(widget.ViewModel);

        view.Rows.Single(r => !r.IsDefault).Pick.Execute(null);

        Assert.Equal(1, _devices.SettingsOpened);
        Assert.True(view.Failed);
    }

    [Fact]
    public async Task Mixer_ReadsAppsOnlyWhileShown_AndStepsTheVolume()
    {
        var descriptor = new AudioMixerDescriptor();
        var widget = new AudioMixerWidget(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility), _devices);
        await widget.StartAsync(SettingValues.Empty);
        var view = Assert.IsType<AudioMixerViewModel>(widget.ViewModel);
        Assert.Equal(0, _devices.AppReads);

        _visibility.Value = WidgetVisibility.Panel;
        Assert.Equal(1, _devices.AppReads);
        view.Rows[0].Up.Execute(null);
        Assert.Equal(60, _devices.LastVolume);

        _visibility.Value = WidgetVisibility.Hidden;
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    private sealed class FakeDevices : IAudioDevices
    {
        public int SettingsOpened { get; private set; }

        public int AppReads { get; private set; }

        public int LastVolume { get; private set; }

        public IReadOnlyList<AudioOutput> Outputs() => [new("a", "Speakers", true), new("b", "Headphones", false)];

        public bool TrySetDefault(string id) => false;

        public void OpenSoundSettings() => SettingsOpened++;

        public IReadOnlyList<AppAudio> Apps()
        {
            AppReads++;
            return [new AppAudio("s1", "Spotify", 50, false)];
        }

        public void SetAppVolume(string id, int percent) => LastVolume = percent;

        public void SetAppMuted(string id, bool muted)
        {
        }
    }
}
