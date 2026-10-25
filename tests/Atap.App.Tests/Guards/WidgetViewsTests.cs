using System.Windows;
using System.Windows.Automation;
using System.Windows.Data;
using Atap.Composition;
using Atap.Core.Notch;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Guards;

/// <summary>Every built-in widget ships a view for each state it supports, and every view has a screen reader name.</summary>
public class WidgetViewsTests
{
    public static TheoryData<string> TypeIds()
    {
        var data = new TheoryData<string>();
        foreach (var descriptor in Discovery.Widgets(typeof(App).Assembly).Descriptors)
            data.Add(descriptor.TypeId);
        return data;
    }

    [Fact]
    public void Catalog_HasNoProblems()
    {
        var catalog = Discovery.Widgets(typeof(App).Assembly);

        Assert.Empty(catalog.Problems);
        Assert.Contains(catalog.Descriptors, d => d.TypeId == "media");
        Assert.Contains(catalog.Descriptors, d => d.TypeId == "clock");
    }

    [Theory]
    [MemberData(nameof(TypeIds))]
    public void EverySupportedState_HasATemplate(string typeId) => Sta.Run(() =>
    {
        var descriptor = Descriptor(typeId);
        var keys = Keys(Load(descriptor));

        foreach (var state in descriptor.SupportedStates)
        {
            var prefix = $"{typeId}.{state}";
            Assert.True(
                keys.Any(k => k == typeId || k == prefix || k.StartsWith(prefix + ".", StringComparison.Ordinal)),
                $"{typeId} supports {state} but has no template for it.");
        }
    });

    [Theory]
    [MemberData(nameof(TypeIds))]
    public void EveryKey_StartsWithTheTypeId(string typeId) => Sta.Run(() =>
    {
        var keys = Keys(Load(Descriptor(typeId)));

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(
            key == typeId || key.StartsWith(typeId + ".", StringComparison.Ordinal),
            $"{typeId} views define '{key}', which could clash with another widget."));
    });

    [Theory]
    [MemberData(nameof(TypeIds))]
    public void EveryTemplateRoot_HasAnAutomationName(string typeId) => Sta.Run(() =>
    {
        var views = Load(Descriptor(typeId));

        foreach (var key in Keys(views))
        {
            if (views[key] is not DataTemplate template)
                continue;
            var root = Assert.IsAssignableFrom<FrameworkElement>(template.LoadContent());
            var named = BindingOperations.IsDataBound(root, AutomationProperties.NameProperty)
                || !string.IsNullOrWhiteSpace(AutomationProperties.GetName(root));
            Assert.True(named, $"The root of '{key}' has no AutomationProperties.Name.");
        }
    });

    [Fact]
    public void EverySurface_HasATemplate() => Sta.Run(() =>
    {
        foreach (var surface in Enum.GetValues<NotchSurface>().Where(s => s != NotchSurface.Hidden))
            Assert.IsAssignableFrom<DataTemplate>(Application.Current.TryFindResource($"surface.{surface}"));
    });

    [Fact]
    public void EverySurface_BuildsInAWindow() => Sta.Run(() =>
    {
        // Resource lookups inside templates only fail when the template is applied, so apply each one.
        foreach (var surface in Enum.GetValues<NotchSurface>().Where(s => s != NotchSurface.Hidden))
        {
            var presenter = new System.Windows.Controls.ContentPresenter
            {
                Content = new SurfaceStandIn(),
                ContentTemplate = (DataTemplate)Application.Current.FindResource($"surface.{surface}"),
            };
            var window = new Window { Content = presenter, ShowActivated = false, WindowStyle = WindowStyle.None, Width = 1, Height = 1 };
            try
            {
                presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                window.Measure(new Size(400, 200));
            }
            finally
            {
                window.Close();
            }
        }
    });

    [Fact]
    public void AppResources_HoldNoWidgetViews() => Sta.Run(() =>
    {
        // Widget views are merged by WidgetViewLoader while an instance exists, never up front.
        foreach (var descriptor in Discovery.Widgets(typeof(App).Assembly).Descriptors)
            Assert.Null(Application.Current.TryFindResource(descriptor.TypeId + ".Compact.Left") ?? Application.Current.TryFindResource(descriptor.TypeId));
    });

    [Fact]
    public void AppXaml_MergesNoWidgetViews()
    {
        var merged = AppXaml.MergedSources();

        Assert.All(merged, source => Assert.False(
            source.StartsWith("Widgets/", StringComparison.OrdinalIgnoreCase),
            $"App.xaml merges {source}. Widget views load only while the widget is on."));
        foreach (var descriptor in Discovery.Widgets(typeof(App).Assembly).Descriptors)
        {
            var views = AppXaml.Normalize(descriptor.ViewsSource.OriginalString);
            Assert.DoesNotContain(merged, source => string.Equals(source, views, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AppXaml_MergesTheThemeAndTheSurfaces()
    {
        var merged = AppXaml.MergedSources();

        Assert.Contains("Ui/Theme/Theme.xaml", merged, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Notch/Surfaces/Surfaces.xaml", merged, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppXaml_HasOnlyMergedDictionaries()
    {
        // The test UI thread copies the merged list, so anything else in App.xaml would not reach the guards.
        var application = System.Xml.Linq.XDocument.Load(AppXaml.FilePath).Root!;
        var resources = Assert.Single(application.Elements(), e => e.Name.LocalName == "Application.Resources");
        var dictionary = Assert.Single(resources.Elements());

        Assert.Equal("ResourceDictionary", dictionary.Name.LocalName);
        Assert.All(dictionary.Elements(), e => Assert.Equal("ResourceDictionary.MergedDictionaries", e.Name.LocalName));
    }

    /// <summary>Has the members the surface templates bind to, with nothing in them.</summary>
    private sealed class SurfaceStandIn
    {
        public System.Windows.Controls.Orientation SlotOrientation => System.Windows.Controls.Orientation.Horizontal;

        public Atap.Core.NotchState State => Atap.Core.NotchState.Idle;

        public bool IsVertical => false;

        public object[] LeftItems { get; } = [];

        public object[] CenterItems { get; } = [];

        public object[] RightItems { get; } = [];

        public object? SurfaceContent => null;
    }

    private static IWidgetDescriptor Descriptor(string typeId)
    {
        Assert.True(Discovery.Widgets(typeof(App).Assembly).TryGet(typeId, out var descriptor));
        return descriptor;
    }

    private static ResourceDictionary Load(IWidgetDescriptor descriptor) =>
        (ResourceDictionary)Application.LoadComponent(descriptor.ViewsSource);

    private static List<string> Keys(ResourceDictionary dictionary) => dictionary.Keys.OfType<string>().ToList();
}
