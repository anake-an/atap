using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Shapes;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Tests.Infrastructure;
using Atap.Ui.Controls;

namespace Atap.Tests.Ui.Controls;

/// <summary>The "value.&lt;Kind&gt;" views for the custom widgets, in Ui/Controls/Values.xaml.</summary>
public class ValueTemplatesTests
{
    public static TheoryData<ValueTemplateKind> Kinds() => new(Enum.GetValues<ValueTemplateKind>());

    public static TheoryData<ValueProblem> Problems() => new(Enum.GetValues<ValueProblem>().Where(p => p != ValueProblem.None));

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryKind_HasATemplateThatLoadsWithAName(ValueTemplateKind kind) => Sta.Run(() =>
    {
        var view = Sample(kind);
        using var shown = Show(view);

        var selector = shown.Presenter.ContentTemplateSelector;
        Assert.Same(Application.Current.FindResource(ValueTemplateSelector.KeyFor(kind)), selector.SelectTemplate(view, shown.Presenter));
        Assert.True(BindingOperations.IsDataBound(shown.Root, AutomationProperties.NameProperty));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(shown.Root)));
    });

    [Fact]
    public void Number_ShowsTheValueAndUnit() => Sta.Run(() =>
    {
        using var shown = Show(new ValueView { Kind = ValueTemplateKind.Number, Text = "72.5", Unit = "%", Number = 72.5, Level = ValueLevel.Normal });

        Assert.Equal("72.5%", shown.Text("Main"));
        Assert.Equal("72.5%", AutomationProperties.GetName(shown.Root));
        Assert.Equal(Visibility.Collapsed, shown.Named<StatusDot>("AlertDot").Visibility);
        Assert.Equal(Visibility.Collapsed, shown.Named<TextBlock>("Problem").Visibility);
    });

    [Theory]
    [InlineData(ValueLevel.Normal, "TextColor", false, "72.5%")]
    [InlineData(ValueLevel.Warning, "ProductivityAccentColor", true, "72.5%. Warning")]
    [InlineData(ValueLevel.Critical, "SystemAccentColor", true, "72.5%. Critical")]
    public void Number_ShowsTheThresholdState(ValueLevel level, string colour, bool dot, string name) => Sta.Run(() =>
    {
        using var shown = Show(new ValueView { Kind = ValueTemplateKind.Number, Text = "72.5", Unit = "%", Number = 72.5, Level = level });

        Assert.Equal(KitHost.Token(colour), KitHost.ColorOf(shown.Named<TextBlock>("Main").Foreground));
        Assert.Equal(dot ? Visibility.Visible : Visibility.Collapsed, shown.Named<StatusDot>("AlertDot").Visibility);
        Assert.Equal(level, shown.Named<StatusDot>("AlertDot").Level);
        Assert.Equal(name, AutomationProperties.GetName(shown.Root));
    });

    [Theory]
    [InlineData(ValueLevel.Normal, "CustomAccentColor")]
    [InlineData(ValueLevel.Warning, "ProductivityAccentColor")]
    [InlineData(ValueLevel.Critical, "SystemAccentColor")]
    public void Progress_ShowsTheThresholdStateOnTheBar(ValueLevel level, string colour) => Sta.Run(() =>
    {
        var view = new ValueView
        {
            Kind = ValueTemplateKind.Progress,
            Text = "72",
            Unit = "%",
            Detail = "36.2 / 50 GB",
            Number = 36.2,
            Fraction = 0.724,
            Level = level,
        };
        using var shown = Show(view);

        var bar = shown.Named<ProgressPill>("Bar");
        Assert.Equal(0.724, bar.Value, 3);
        Assert.Equal(level, bar.Level);
        Assert.Equal(KitHost.Token(colour), KitHost.ColorOf(KitHost.Named<System.Windows.Controls.Border>(bar, "PART_Indicator").Background));
        Assert.Equal("72%", shown.Text("Main"));
        Assert.Equal("36.2 / 50 GB", shown.Text("Detail"));
        Assert.StartsWith("72%. 36.2 / 50 GB", AutomationProperties.GetName(shown.Root), StringComparison.Ordinal);
    });

    [Theory]
    [InlineData(ValueLevel.Normal, "MediaAccentColor")]
    [InlineData(ValueLevel.Warning, "ProductivityAccentColor")]
    [InlineData(ValueLevel.Critical, "SystemAccentColor")]
    public void StatusDot_ShowsTheThresholdState(ValueLevel level, string colour) => Sta.Run(() =>
    {
        using var shown = Show(new ValueView { Kind = ValueTemplateKind.StatusDot, Text = "online", Level = level });

        var dot = shown.Named<StatusDot>("Dot");
        Assert.Equal(level, dot.Level);
        Assert.Equal(KitHost.Token(colour), KitHost.ColorOf(KitHost.Named<Ellipse>(dot, "Dot").Fill));
        Assert.Equal("online", shown.Text("Main"));
    });

    [Fact]
    public void StatusDot_SaysAnUnknownStatus() => Sta.Run(() =>
    {
        using var shown = Show(new ValueView { Kind = ValueTemplateKind.StatusDot, Text = "rebooting", Level = ValueLevel.Unknown });

        Assert.Equal("rebooting. Status unknown", AutomationProperties.GetName(shown.Root));
    });

    [Fact]
    public void List_ShowsItsItemsAndHowManyMore() => Sta.Run(() =>
    {
        var view = new ValueView
        {
            Kind = ValueTemplateKind.List,
            Text = "5",
            Number = 5,
            ItemCount = 5,
            Level = ValueLevel.Critical,
            Items =
            [
                new ValueListItem("Disk C", "91%", ValueLevel.Critical),
                new ValueListItem("Disk D", "40%", ValueLevel.Normal),
                new ValueListItem("Backups", null, ValueLevel.Normal),
            ],
        };
        using var shown = Show(view);

        var rows = KitHost.All<Grid>(shown.Root).Where(g => g.Name == "Row").ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("Disk C. 91%. Critical", AutomationProperties.GetName(rows[0]));
        Assert.Equal("Disk D. 40%", AutomationProperties.GetName(rows[1]));
        Assert.Equal("Backups", AutomationProperties.GetName(rows[2]));
        Assert.Equal(KitHost.Token("SystemAccentColor"), KitHost.ColorOf(KitHost.Named<TextBlock>(rows[0], "ItemValue").Foreground));
        Assert.Equal(KitHost.Token("TextColor"), KitHost.ColorOf(KitHost.Named<TextBlock>(rows[1], "ItemValue").Foreground));
        // An item without a value has no dot and no value text.
        Assert.Equal(Visibility.Collapsed, KitHost.Named<StatusDot>(rows[2], "Dot").Visibility);
        Assert.Equal(Visibility.Collapsed, KitHost.Named<TextBlock>(rows[2], "ItemValue").Visibility);

        var more = shown.Named<TextBlock>("More");
        Assert.Equal("2 more", more.Text);
        Assert.Equal(Visibility.Visible, more.Visibility);
        Assert.Equal("5 items. Critical", AutomationProperties.GetName(shown.Root));
    });

    [Fact]
    public void List_WithEveryItemShown_HasNoMoreLine() => Sta.Run(() =>
    {
        var view = new ValueView
        {
            Kind = ValueTemplateKind.List,
            Text = "1",
            ItemCount = 1,
            Level = ValueLevel.Normal,
            Items = [new ValueListItem("nas", "up", ValueLevel.Normal)],
        };
        using var shown = Show(view);

        Assert.Equal(Visibility.Collapsed, shown.Named<TextBlock>("More").Visibility);
    });

    [Theory]
    [MemberData(nameof(Problems))]
    public void NoValue_ShowsWhyInPlainWords(ValueProblem problem) => Sta.Run(() =>
    {
        foreach (var kind in Enum.GetValues<ValueTemplateKind>())
        {
            using var shown = Show(ValueView.Empty(kind, problem));
            var expected = Localizer.Instance[$"controls.problem.{problem}"];

            Assert.NotEqual($"controls.problem.{problem}", expected);
            var text = shown.Named<TextBlock>("Problem");
            Assert.Equal(Visibility.Visible, text.Visibility);
            Assert.Equal(expected, text.Text);
            Assert.Equal(Visibility.Collapsed, shown.Named<FrameworkElement>("ValueRow").Visibility);
            Assert.Equal(expected, AutomationProperties.GetName(shown.Root));
        }
    });

    [Fact]
    public void Stale_SaysOutOfDate() => Sta.Run(() =>
    {
        using var shown = Show(new ValueView { Kind = ValueTemplateKind.Number, Text = "12", Unit = " GB", Level = ValueLevel.Normal, Stale = true });

        var stale = shown.Named<TextBlock>("Stale");
        Assert.Equal(Visibility.Visible, stale.Visibility);
        Assert.Equal("Out of date", stale.Text);
        Assert.EndsWith("Out of date", AutomationProperties.GetName(shown.Root), StringComparison.Ordinal);
        Assert.True(shown.Named<FrameworkElement>("ValueRow").Opacity < 1);
    });

    [Fact]
    public void Text_FollowsTheLanguage() => Sta.Run(() =>
    {
        using var shown = Show(new ValueView { Kind = ValueTemplateKind.Number, Text = "9", Level = ValueLevel.Warning, Stale = true });
        try
        {
            Localizer.Instance.SetLanguage(Localizer.Malay);
            Assert.Equal("Tidak terkini", shown.Named<TextBlock>("Stale").Text);
            Assert.Equal("9. Amaran. Tidak terkini", AutomationProperties.GetName(shown.Root));
        }
        finally
        {
            Localizer.Instance.SetLanguage(Localizer.English);
        }

        Assert.Equal("9. Warning. Out of date", AutomationProperties.GetName(shown.Root));
    });

    [Fact]
    public void Content_Binds() => Sta.Run(() =>
    {
        using var shown = Show(Sample(ValueTemplateKind.Number));
        shown.Presenter.Content = new ValueView { Kind = ValueTemplateKind.Number, Text = "88", Unit = "%", Level = ValueLevel.Critical };
        shown.Layout();

        Assert.Equal("88%", shown.Text("Main"));
        Assert.Equal(KitHost.Token("SystemAccentColor"), KitHost.ColorOf(shown.Named<TextBlock>("Main").Foreground));
    });

    [Fact]
    public void HighContrast_UsesTheSystemTextColour() => Sta.Run(() =>
    {
        using var shown = Show(new ValueView { Kind = ValueTemplateKind.Number, Text = "95", Unit = "%", Level = ValueLevel.Critical });
        var main = shown.Named<TextBlock>("Main");

        using (KitHost.HighContrast())
            Assert.Equal(SystemColors.WindowTextColor, KitHost.ColorOf(main.Foreground));

        Assert.Equal(KitHost.Token("SystemAccentColor"), KitHost.ColorOf(main.Foreground));
    });

    [Fact]
    public void ViewsFromTheCoreTemplate_Show() => Sta.Run(() =>
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number, "$.disk.usedPercent")
        {
            Unit = "%",
            Thresholds = new ThresholdSpec(Warning: 70, Critical: 90),
        };
        Assert.True(ValueTemplate.TryCreate(spec, out var template, out _));
        using var json = JsonDocument.Parse("""{ "disk": { "usedPercent": 72.5 } }""");
        var view = template.Render(json.RootElement, null, CultureInfo.InvariantCulture);

        using var shown = Show(view);

        Assert.Equal("72.5%", shown.Text("Main"));
        Assert.Equal(KitHost.Token("ProductivityAccentColor"), KitHost.ColorOf(shown.Named<TextBlock>("Main").Foreground));
        Assert.Equal("72.5%. Warning", AutomationProperties.GetName(shown.Root));
    });

    [Fact]
    public void Selector_IgnoresOtherItems() => Sta.Run(() =>
    {
        var selector = (ValueTemplateSelector)Application.Current.FindResource("value.selector");

        Assert.Null(selector.SelectTemplate("not a value", new ContentPresenter()));
    });

    private static ValueView Sample(ValueTemplateKind kind) => kind switch
    {
        ValueTemplateKind.Progress => new ValueView { Kind = kind, Text = "50", Unit = "%", Fraction = 0.5, Level = ValueLevel.Normal },
        ValueTemplateKind.List => new ValueView { Kind = kind, Text = "1", ItemCount = 1, Level = ValueLevel.Normal, Items = [new ValueListItem("a", "1", ValueLevel.Normal)] },
        _ => new ValueView { Kind = kind, Text = "ok", Level = ValueLevel.Normal },
    };

    private static Shown Show(ValueView view)
    {
        var presenter = new ContentPresenter
        {
            Content = view,
            ContentTemplateSelector = (DataTemplateSelector)Application.Current.FindResource("value.selector"),
        };
        return new Shown(presenter);
    }

    private sealed class Shown : IDisposable
    {
        private readonly KitHost _host;

        public Shown(ContentPresenter presenter)
        {
            Presenter = presenter;
            _host = new KitHost(presenter);
        }

        public ContentPresenter Presenter { get; }

        public FrameworkElement Root => KitHost.Named<FrameworkElement>(Presenter, "Root");

        public T Named<T>(string name)
            where T : FrameworkElement => KitHost.Named<T>(Root, name);

        public string Text(string name) => Named<TextBlock>(name).Text;

        public void Layout() => _host.Layout();

        public void Dispose() => _host.Dispose();
    }
}
