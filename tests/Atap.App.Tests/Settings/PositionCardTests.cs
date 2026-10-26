using Atap.Core.Commands;
using Atap.Core.Placement;
using Atap.Settings.Cards.Position;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Settings;

public sealed class PositionCardTests : IDisposable
{
    private static readonly DisplayMonitor Side = new(@"\\?\DISPLAY#SIDE", "ARZOPA", new PixelRect(1920, 0, 1920, 1080), IsPrimary: false);

    private readonly TestServices _services = new();

    public PositionCardTests() => _services.Layout.Load();

    public void Dispose() => _services.Dispose();

    [Fact]
    public void MovingTheNotch_KeepsTheChosenScreenList_SoTheBoxKeepsItsSelection()
    {
        _services.Layout.Update(d => d.Placement.Monitor = MonitorSelection.Specific(Side));
        using var card = new PositionCardViewModel(_services.Layout, new FixedMonitors(Side), new CommandRegistry(), _services.Text);
        var screens = card.Screens;

        card.SelectedAnchor = card.Anchors.Single(a => a.Anchor == NotchAnchor.RightCenter);

        Assert.Same(screens, card.Screens);
        Assert.Equal("ARZOPA", card.SelectedScreen?.Label);
    }

    private sealed class FixedMonitors(params DisplayMonitor[] monitors) : IMonitorList
    {
        public IReadOnlyList<DisplayMonitor> Monitors { get; } = monitors;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}
