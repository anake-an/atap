using Atap.Core.Commands;
using Atap.Core.Placement;
using Atap.Notch;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Notch;

/// <summary>The notch host on fake monitors and windows, so no real window opens. The layout lives in a temp folder.</summary>
public sealed class NotchHostTests : IDisposable
{
    private static readonly DisplayMonitor Main = new(@"\\?\DISPLAY#MAIN", "Main", new PixelRect(0, 0, 1920, 1080), IsPrimary: true);
    private static readonly DisplayMonitor Side = new(@"\\?\DISPLAY#SIDE", "Side", new PixelRect(1920, 0, 2560, 1440), IsPrimary: false);

    private readonly TestServices _services = new();
    private readonly List<FakeWindow> _windows = [];

    public NotchHostTests() => _services.Layout.Load();

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Start_PlacesOneWindowOnThePrimary() => Sta.Run(() =>
    {
        using var host = Start();

        var window = Assert.Single(_windows);
        Assert.Same(Main, window.TargetMonitor);
        Assert.Equal(NotchAnchor.TopCenter, window.Anchor);
        Assert.Equal(1, window.Shows);
    });

    [Fact]
    public void AnchorChangedThroughTheLayout_MovesTheWindow() => Sta.Run(() =>
    {
        using var host = Start();
        var changes = CountChanges(host);

        // What a settings card or onboarding does.
        _services.Layout.Update(d => d.Placement.Anchor = NotchAnchor.TopLeft);

        var window = Assert.Single(_windows);
        Assert.Equal(NotchAnchor.TopLeft, window.Anchor);
        Assert.Equal(2, window.Places);
        Assert.Equal(1, changes());
    });

    [Fact]
    public void OffsetChangedThroughTheLayout_MovesTheWindow() => Sta.Run(() =>
    {
        using var host = Start();

        _services.Layout.Update(d => d.Placement.Offset = 40);

        Assert.Equal(40, Assert.Single(_windows).Offset);
    });

    [Fact]
    public void MonitorChangedThroughTheLayout_AddsAndClosesWindows() => Sta.Run(() =>
    {
        using var host = Start();

        _services.Layout.Update(d => d.Placement.Monitor = MonitorSelection.All());
        Assert.Equal(2, _windows.Count);
        Assert.Equal([Main, Side], _windows.Select(w => w.TargetMonitor!));

        _services.Layout.Update(d => d.Placement.Monitor = MonitorSelection.Specific(Side));
        // The window already on the side monitor stays, and the other one closes.
        Assert.True(_windows[0].Closed);
        Assert.False(_windows[1].Closed);
        Assert.Same(Side, _windows[1].TargetMonitor);
    });

    [Fact]
    public void LayoutChangeThatKeepsThePlacement_LeavesTheWindowsAlone() => Sta.Run(() =>
    {
        using var host = Start();
        var changes = CountChanges(host);

        _services.Layout.Update(_ => { });
        _services.Layout.Update(d => d.Placement.Anchor = NotchAnchor.TopCenter);

        Assert.Equal(1, Assert.Single(_windows).Places);
        Assert.Equal(0, changes());
    });

    [Fact]
    public void SetAnchor_SavesAndMovesOnce() => Sta.Run(() =>
    {
        using var host = Start();
        var changes = CountChanges(host);

        host.SetAnchor(NotchAnchor.RightCenter);

        Assert.Equal(NotchAnchor.RightCenter, _services.Layout.Document.Placement.Anchor);
        var window = Assert.Single(_windows);
        Assert.Equal(NotchAnchor.RightCenter, window.Anchor);
        Assert.Equal(2, window.Places);
        Assert.Equal(1, changes());
    });

    [Fact]
    public void BeforeStart_LayoutChangesOpenNoWindow() => Sta.Run(() =>
    {
        using var host = Create();

        _services.Layout.Update(d => d.Placement.Anchor = NotchAnchor.TopRight);
        Assert.Empty(_windows);

        host.Start(Menu(host));
        Assert.Equal(NotchAnchor.TopRight, Assert.Single(_windows).Anchor);
    });

    [Fact]
    public void AfterDispose_LayoutChangesAreIgnored() => Sta.Run(() =>
    {
        var host = Start();
        host.Dispose();

        _services.Layout.Update(d => d.Placement.Anchor = NotchAnchor.TopRight);

        var window = Assert.Single(_windows);
        Assert.True(window.Closed);
        Assert.Equal(1, window.Places);
    });

    private static Func<int> CountChanges(NotchHost host)
    {
        var count = 0;
        host.Changed += (_, _) => count++;
        return () => count;
    }

    private NotchHost Create() => new(_services.Layout, Sta.Dispatcher, () => [Main, Side], _ =>
    {
        var window = new FakeWindow();
        _windows.Add(window);
        return window;
    });

    private NotchHost Start()
    {
        var host = Create();
        host.Start(Menu(host));
        return host;
    }

    private NotchMenuViewModel Menu(NotchHost host) => new(host, updates: null, new CommandRegistry(), _services.Text);

    private sealed class FakeWindow : INotchWindow
    {
        public DisplayMonitor? TargetMonitor { get; private set; }

        public NotchAnchor Anchor { get; private set; }

        public int Offset { get; private set; }

        public int Places { get; private set; }

        public int Shows { get; private set; }

        public bool Closed { get; private set; }

        public void Place(DisplayMonitor monitor, NotchAnchor anchor, int offset)
        {
            TargetMonitor = monitor;
            Anchor = anchor;
            Offset = offset;
            Places++;
        }

        public void UpdateVisibility() => Shows++;

        public void Close() => Closed = true;
    }
}
