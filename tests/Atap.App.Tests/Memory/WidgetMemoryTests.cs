using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Atap.Composition;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;
using Xunit.Abstractions;

namespace Atap.Tests.Memory;

/// <summary>
/// The widgets of a busy notch, created with their views shown and then removed, again and again. Once
/// they are gone, nothing of them may stay in memory, and their views leave the app resources.
/// </summary>
[Collection(MemoryProbe.Collection)]
public sealed class WidgetMemoryTests(ITestOutputHelper output) : IDisposable
{
    private static readonly string[] Busy =
    [
        "media", "lyrics", "focus-timer", "alarm", "waktu-solat", "audio-mixer", "audio-output", "missed-alerts", "clock",
    ];

    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task RemovedWidgets_AndTheirViews_AreCollected()
    {
        var target = Sta.Run(() => new ResourceDictionary());
        var watched = new List<WeakReference>();
        for (var round = 0; round < 5; round++)
        {
            watched.AddRange(Sta.Run(() => ShowAndRemove(target)));
            await MemoryProbe.SettleAsync();
            output.WriteLine($"round {round}: {MemoryProbe.Numbers()}, alive {watched.Count(w => w.IsAlive)} of {watched.Count}");
        }

        Assert.Equal(0, Sta.Run(() => target.MergedDictionaries.Count));
        MemoryProbe.AssertCollected(watched);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private List<WeakReference> ShowAndRemove(ResourceDictionary target)
    {
        var host = new WidgetHost(
            Discovery.Widgets(typeof(App).Assembly),
            _services.CreateContext,
            new WidgetViewLoader(target),
            (_, descriptor) => new SettingValues(descriptor.Settings, null),
            _services.Time,
            _services.Scheduler,
            ui: null,
            _services.Warn);
        // Created, not started, so no timers, system events or network run.
        host.ApplyAsync(Busy).GetAwaiter().GetResult();
        Assert.Equal(Busy.Length, host.Widgets.Count);

        var window = MemoryProbe.OffscreenWindow();
        window.Resources.MergedDictionaries.Add(target);
        var panel = new WrapPanel();
        foreach (var widget in host.Widgets)
        {
            foreach (var template in Templates(target, widget.Descriptor.TypeId))
                panel.Children.Add(new ContentControl { Content = widget.ViewModel, ContentTemplate = template });
        }
        window.Content = panel;
        window.Show();
        window.UpdateLayout();

        var watched = new List<WeakReference> { new(window) };
        watched.AddRange(host.Widgets.Select(w => new WeakReference(w)));
        watched.AddRange(host.Widgets.Select(w => new WeakReference(w.ViewModel)));

        window.Close();
        window.Content = null;
        window.Resources.MergedDictionaries.Remove(target);
        host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return watched;
    }

    // Each widget's own templates, so a view model is shown only in the views made for it.
    private static List<DataTemplate> Templates(ResourceDictionary target, string typeId) =>
        target.MergedDictionaries
            .SelectMany(d => d.Keys.OfType<string>().Where(k => k.StartsWith(typeId + ".", StringComparison.Ordinal)).Select(k => d[k]))
            .OfType<DataTemplate>()
            .ToList();
}
