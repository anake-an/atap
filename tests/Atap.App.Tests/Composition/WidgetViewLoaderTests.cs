using System.Windows;
using Atap.Composition;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Composition;

public class WidgetViewLoaderTests
{
    [Fact]
    public void Acquire_MergesOncePerTypeAndTheLastReleaseRemoves() => Sta.Run(() =>
    {
        var target = new ResourceDictionary();
        var loader = new WidgetViewLoader(target);
        var clock = Descriptor("clock");

        var first = loader.Acquire(clock);
        var second = loader.Acquire(clock);
        Assert.Single(target.MergedDictionaries);
        Assert.NotNull(target["clock"]);

        first.Dispose();
        first.Dispose();
        Assert.Single(target.MergedDictionaries);

        second.Dispose();
        Assert.Empty(target.MergedDictionaries);
        Assert.Empty(loader.LoadedTypeIds);
    });

    [Fact]
    public void MediaViews_AreNotMergedWhileMediaIsDisabled() => Sta.Run(() =>
    {
        using var services = new TestServices();
        var target = new ResourceDictionary();
        var host = new WidgetHost(
            Discovery.Widgets(typeof(App).Assembly),
            services.CreateContext,
            new WidgetViewLoader(target),
            (_, descriptor) => new SettingValues(descriptor.Settings, null),
            services.Time,
            services.Scheduler,
            ui: null,
            services.Warn);

        // Only the clock is enabled. Instances are created, not started, so no timers or system events run.
        host.ApplyAsync(["clock"]).GetAwaiter().GetResult();

        Assert.True(target.Contains("clock"));
        Assert.False(ContainsKey(target, "media.Compact.Left"));
        Assert.False(ContainsKey(target, "media.Expanded.Medium"));

        host.ApplyAsync(["clock", "media"]).GetAwaiter().GetResult();
        Assert.True(ContainsKey(target, "media.Compact.Left"));

        host.ApplyAsync(["clock"]).GetAwaiter().GetResult();
        Assert.False(ContainsKey(target, "media.Compact.Left"));

        host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Assert.Empty(target.MergedDictionaries);
    });

    private static bool ContainsKey(ResourceDictionary dictionary, string key) =>
        dictionary.Contains(key) || dictionary.MergedDictionaries.Any(d => ContainsKey(d, key));

    private static IWidgetDescriptor Descriptor(string typeId)
    {
        Assert.True(Discovery.Widgets(typeof(App).Assembly).TryGet(typeId, out var descriptor));
        return descriptor;
    }
}
