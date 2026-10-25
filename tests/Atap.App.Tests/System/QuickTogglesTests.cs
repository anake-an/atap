using Atap.Composition;
using Atap.Core.Widgets;
using Atap.Platform.Toggles;
using Atap.Tests.Infrastructure;
using Atap.Widgets.QuickToggles;

namespace Atap.Tests.SystemWidgets;

public sealed class QuickTogglesTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new("quick-toggles");

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task MissingThings_AreHidden_AndAFailureIsReported()
    {
        var dark = new FakeToggle("darkMode", state: false, works: true);
        var bluetooth = new FakeToggle("bluetooth", state: null, works: true);
        var mic = new FakeToggle("micMute", state: false, works: false);
        var view = await StartAsync(dark, bluetooth, mic);

        Assert.Equal(["darkMode", "micMute"], view.Tiles.Select(t => t.Id));

        await view.Tiles[0].Flip.ExecuteAsync(null);
        Assert.True(view.Tiles[0].IsOn);
        Assert.False(view.HasFailure);

        await view.Tiles[1].Flip.ExecuteAsync(null);
        Assert.False(view.Tiles[1].IsOn);
        Assert.True(view.HasFailure);
    }

    private async Task<QuickTogglesViewModel> StartAsync(params ISystemToggle[] toggles)
    {
        var descriptor = new QuickTogglesDescriptor();
        var widget = new QuickTogglesWidget(
            _services.CreateContext(descriptor.TypeId, descriptor, _lifetime, new ObservableValue<WidgetVisibility>(WidgetVisibility.Panel)), toggles);
        await widget.StartAsync(SettingValues.Empty);
        return Assert.IsType<QuickTogglesViewModel>(widget.ViewModel);
    }

    private sealed class FakeToggle(string id, bool? state, bool works) : ISystemToggle
    {
        private bool? _state = state;

        public string Id => id;

        public Task<bool?> ReadAsync() => Task.FromResult(_state);

        public Task<bool> SetAsync(bool on)
        {
            if (works)
                _state = on;
            return Task.FromResult(works);
        }
    }
}
