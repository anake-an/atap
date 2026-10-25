using Atap.Core.Tests.Widgets;
using Atap.Core.Widgets;

[assembly: AtapWidget(typeof(CatalogSamples.Alpha))]
[assembly: AtapWidget(typeof(CatalogSamples.Beta))]
[assembly: AtapWidget(typeof(CatalogSamples.AlphaAgain))]
[assembly: AtapWidget(typeof(CatalogSamples.BadId))]
[assembly: AtapWidget(typeof(CatalogSamples.Throws))]
[assembly: AtapWidget(typeof(CatalogSamples.NoDefaultConstructor))]
[assembly: AtapWidget(typeof(string))]

namespace Atap.Core.Tests.Widgets;

public static class CatalogSamples
{
    public sealed class Alpha() : SampleDescriptor("alpha");

    public sealed class Beta() : SampleDescriptor("beta", multi: true);

    public sealed class AlphaAgain() : SampleDescriptor("alpha");

    public sealed class BadId() : SampleDescriptor("Bad_Id");

    public sealed class Throws : SampleDescriptor
    {
        public Throws()
            : base("throws") => throw new InvalidOperationException("boom");
    }

    public sealed class NoDefaultConstructor(string typeId) : SampleDescriptor(typeId);

    public abstract class SampleDescriptor(string typeId, bool multi = false) : IWidgetDescriptor
    {
        public string TypeId => typeId;

        public string NameKey => $"{typeId}.name";

        public string DescriptionKey => $"{typeId}.description";

        public WidgetGroup Group => WidgetGroup.Custom;

        public IReadOnlyList<WidgetSize> SupportedSizes => [WidgetSize.Small];

        public IReadOnlyList<NotchState> SupportedStates => [NotchState.Idle];

        public IReadOnlyList<SettingDefinition> Settings => [];

        public WidgetCapabilities Capabilities => WidgetCapabilities.None;

        public bool AllowsMultipleInstances => multi;

        public bool WorksWithoutPlacement => false;

        public Uri ViewsSource => new($"/Tests;component/{typeId}.xaml", UriKind.Relative);

        public IWidget Create(WidgetContext context) => throw new NotSupportedException();
    }
}

public class WidgetCatalogTests
{
    private static readonly WidgetCatalog Discovered = WidgetCatalog.Discover(typeof(WidgetCatalogTests).Assembly);

    [Fact]
    public void Discover_FindsRegisteredDescriptorsInOrder()
    {
        Assert.Equal(["alpha", "beta"], Discovered.Descriptors.Select(d => d.TypeId));
        Assert.IsType<CatalogSamples.Alpha>(Discovered.Descriptors[0]);
    }

    [Fact]
    public void Discover_RejectsADuplicateTypeIdAndKeepsTheFirst()
    {
        Assert.Contains(Discovered.Problems, p => p.Source == "alpha" && p.Reason == WidgetRejection.DuplicateTypeId);
        Assert.True(Discovered.TryGet("alpha", out var alpha));
        Assert.IsType<CatalogSamples.Alpha>(alpha);
    }

    [Fact]
    public void Discover_RejectsAnInvalidTypeId()
    {
        Assert.Contains(Discovered.Problems, p => p.Source == "Bad_Id" && p.Reason == WidgetRejection.InvalidTypeId);
        Assert.False(Discovered.TryGet("Bad_Id", out _));
    }

    [Fact]
    public void Discover_ReportsAConstructorThatThrows()
    {
        var problem = Assert.Single(Discovered.Problems, p => p.Reason == WidgetRejection.CreateFailed);
        Assert.IsType<InvalidOperationException>(problem.Exception);
    }

    [Fact]
    public void Discover_RejectsTypesThatAreNotDescriptors()
    {
        Assert.Contains(Discovered.Problems, p => p.Source == typeof(string).FullName && p.Reason == WidgetRejection.InvalidType);
        Assert.Contains(Discovered.Problems, p => p.Source == typeof(CatalogSamples.NoDefaultConstructor).FullName && p.Reason == WidgetRejection.InvalidType);
    }

    [Theory]
    [InlineData("alpha", true)]
    [InlineData("alpha#second", false)] // single-instance type
    [InlineData("beta", true)]
    [InlineData("beta#nas", true)]
    [InlineData("beta#", false)]
    [InlineData("gamma", false)] // unknown type
    [InlineData("Alpha", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryResolve_FollowsTheInstanceIdRules(string? instanceId, bool expected)
    {
        Assert.Equal(expected, Discovered.TryResolve(instanceId, out var descriptor));
        Assert.Equal(expected, descriptor is not null);
    }

    [Fact]
    public void FromDescriptors_LeavesOutInvalidAndDuplicateIds()
    {
        var catalog = WidgetCatalog.FromDescriptors([new FakeDescriptor("media"), new FakeDescriptor("media"), new FakeDescriptor("x")]);

        Assert.Equal(["media"], catalog.Descriptors.Select(d => d.TypeId));
        Assert.Equal(2, catalog.Problems.Count);
    }
}
