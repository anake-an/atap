using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Atap.Core.Input;
using Atap.Core.Localization;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Settings.Form;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Settings;

public sealed class SettingsFormTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly FakeSecretStore _secrets = new();

    public SettingsFormTests() => _services.Settings.Load();

    public void Dispose() => _services.Dispose();

    /// <summary>One setting of every type, each with a sensible default.</summary>
    internal static IReadOnlyList<SettingDefinition> EveryType() =>
    [
        new("test.bool", "test.bool", SettingType.Bool, true),
        new("test.int", "test.int", SettingType.Int, 5) { Min = 1, Max = 10 },
        new("test.string", "test.string", SettingType.String, "hello"),
        new("test.choice", "test.choice", SettingType.Choice, "a", ["a", "b", "c"]),
        new("test.double", "test.double", SettingType.Double, 1.5),
        new("test.time", "test.time", SettingType.Time, "07:30"),
        new("test.duration", "test.duration", SettingType.Duration, 300),
        new("test.url", "test.url", SettingType.Url, "https://example.com"),
        new("test.secret", "test.secret", SettingType.Secret, null),
        new("test.folder", "test.folder", SettingType.Folder, @"C:\Music"),
        new("test.file", "test.file", SettingType.File, @"C:\Music\a.mp3"),
        new("test.hotkey", "test.hotkey", SettingType.Hotkey, "Ctrl+Alt+K"),
        new("test.timeZone", "test.timeZone", SettingType.TimeZone, "UTC"),
        new("test.list", "test.list", SettingType.StringList, new[] { "one" }),
        new("test.custom", "test.custom", SettingType.Custom, null) { EditorKey = "test.editor" },
    ];

    [Fact]
    public void EveryType_IsCovered()
    {
        Assert.Equal(Enum.GetValues<SettingType>().ToHashSet(), EveryType().Select(d => d.Type).ToHashSet());
    }

    [Theory]
    [InlineData(SettingType.Bool, typeof(ToggleField))]
    [InlineData(SettingType.Int, typeof(NumberField))]
    [InlineData(SettingType.Double, typeof(NumberField))]
    [InlineData(SettingType.Choice, typeof(ChoiceField))]
    [InlineData(SettingType.String, typeof(TextField))]
    [InlineData(SettingType.Url, typeof(TextField))]
    [InlineData(SettingType.Time, typeof(TextField))]
    [InlineData(SettingType.Folder, typeof(TextField))]
    [InlineData(SettingType.File, typeof(TextField))]
    [InlineData(SettingType.Secret, typeof(SecretField))]
    [InlineData(SettingType.Duration, typeof(DurationField))]
    [InlineData(SettingType.Hotkey, typeof(HotkeyField))]
    [InlineData(SettingType.TimeZone, typeof(TimeZoneField))]
    [InlineData(SettingType.StringList, typeof(StringListField))]
    [InlineData(SettingType.Custom, typeof(CustomField))]
    public void EveryType_GetsItsField(SettingType type, Type fieldType)
    {
        using var form = Form(EveryType().Where(d => d.Type == type).ToArray());

        Assert.IsType(fieldType, Assert.Single(form.Fields));
    }

    [Fact]
    public void EveryType_Renders_WithControlsNamedByTheirLabel() => Sta.Run(() =>
    {
        using var form = Form([.. EveryType()]);
        var host = SettingsUi.Render(form);

        foreach (var field in form.Fields)
        {
            var row = SettingsUi.Find<ContentPresenter>(host).First(p => ReferenceEquals(p.Content, field));
            var inputs = SettingsUi.Visible<Control>(row)
                .Where(c => c is TextBox or PasswordBox or ToggleButton or Selector or Slider && c.TemplatedParent is not Control)
                .ToList();
            var expected = field.Definition.Type switch
            {
                SettingType.Bool => typeof(CheckBox),
                SettingType.Int => typeof(Slider),
                SettingType.Choice => typeof(ListBox),
                SettingType.Secret => typeof(PasswordBox),
                SettingType.Hotkey => typeof(HotkeyBox),
                SettingType.Custom => null,
                _ => typeof(TextBox),
            };
            if (expected is null)
            {
                // No editor named "test.editor" exists, so the field says so instead of showing nothing.
                Assert.Contains(SettingsUi.Find<TextBlock>(row), t => t.Text == ((CustomField)field).MissingText);
                continue;
            }
            Assert.Contains(inputs, c => c.GetType() == expected);
            foreach (var input in inputs.Where(c => c is not ListBoxItem and not ComboBoxItem))
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(input)), $"A {input.GetType().Name} for {field.Key} has no name.");
        }
    });

    [Fact]
    public void ChoiceWithMoreThanThreeOptions_IsADropDown() => Sta.Run(() =>
    {
        using var form = Form(new SettingDefinition("test.many", "test.many", SettingType.Choice, "a", ["a", "b", "c", "d"]));
        var host = SettingsUi.Render(form);

        var field = Assert.IsType<ChoiceField>(Assert.Single(form.Fields));
        Assert.False(field.IsSegmented);
        Assert.Single(SettingsUi.Visible<ComboBox>(host));
        Assert.Empty(SettingsUi.Visible<ListBox>(host));
    });

    [Fact]
    public void InvalidInput_KeepsTheLastValidValue_AndShowsAMessage()
    {
        var definition = new SettingDefinition("test.url", "test.url", SettingType.Url, "https://old.example");
        using var form = Form(definition);
        var field = (TextField)form.Fields[0];

        field.Text = "not an address";

        Assert.Equal(SettingValidator.UrlKey, field.ErrorKey);
        Assert.True(field.HasError);
        Assert.Equal(_services.Text[SettingValidator.UrlKey], field.Error);
        Assert.Equal("https://old.example", _services.Settings.Values.GetString(definition.Key));
        Assert.False(_services.Settings.Values.Raw.ContainsKey(definition.Key));

        field.Text = "https://new.example";

        Assert.Null(field.ErrorKey);
        Assert.Equal("https://new.example", _services.Settings.Values.GetString(definition.Key));
    }

    [Fact]
    public void Numbers_AreClampedToMinAndMax()
    {
        var definition = new SettingDefinition("test.int", "test.int", SettingType.Int, 5) { Min = 1, Max = 10 };
        using var form = Form(definition);
        var field = (NumberField)form.Fields[0];

        field.Text = "50";

        Assert.Equal(10, _services.Settings.Values.GetInt(definition.Key));
        Assert.Equal("10", field.Text);
        Assert.True(field.HasSlider);

        field.SliderValue = 3.4;
        Assert.Equal(3, _services.Settings.Values.GetInt(definition.Key));
    }

    [Fact]
    public void Time_IsWrittenAsHHmm()
    {
        var definition = new SettingDefinition("test.time", "test.time", SettingType.Time, "07:30");
        using var form = Form(definition);
        var field = (TextField)form.Fields[0];

        field.Text = "6:05";

        Assert.Equal("06:05", field.Text);
        Assert.Equal(new TimeOnly(6, 5), _services.Settings.Values.GetTime(definition.Key));
    }

    [Fact]
    public void Secret_NeverReachesTheSettingsFile()
    {
        var definition = new SettingDefinition("test.token", "test.token", SettingType.Secret, null);
        using var form = Form(definition);
        var field = (SecretField)form.Fields[0];

        Assert.True(field.Save("plain-secret-value"));

        var reference = _services.Settings.Values.GetSecretRef(definition.Key);
        Assert.NotNull(reference);
        Assert.Equal("plain-secret-value", _secrets.Get(reference));
        Assert.True(field.IsSaved);
        // Writes are batched, so the pending one is written before the file is read.
        _services.Settings.Flush();
        var file = File.ReadAllText(_services.Paths.SettingsFile);
        Assert.DoesNotContain("plain-secret-value", file);
        Assert.Contains(reference, file);

        // A new value replaces the old one in the store.
        Assert.True(field.Save("second-value"));
        Assert.Contains(reference, _secrets.Deleted);
        Assert.DoesNotContain("second-value", File.ReadAllText(_services.Paths.SettingsFile));

        var second = _services.Settings.Values.GetSecretRef(definition.Key)!;
        field.RemoveCommand.Execute(null);
        Assert.Null(_services.Settings.Values.GetSecretRef(definition.Key));
        Assert.Contains(second, _secrets.Deleted);
        Assert.Empty(_secrets.Values);
    }

    [Fact]
    public void Secret_InWidgetSettings_NeverReachesTheLayoutFile()
    {
        _services.Layout.Load();
        var definition = new SettingDefinition("token", "test.token", SettingType.Secret, null);
        var target = new WidgetSettingsTarget(_services.Layout, "json-url#nas", [definition]);
        using var form = new SettingsFormViewModel([definition], new SettingsFormContext(target, _services.Text, _secrets));

        Assert.True(((SecretField)form.Fields[0]).Save("widget-secret"));

        var file = File.ReadAllText(_services.Paths.LayoutFile);
        Assert.DoesNotContain("widget-secret", file);
        Assert.StartsWith(SettingValues.SecretPrefix, _services.Layout.Document.GetWidgetSettings("json-url#nas")["token"].GetString());
    }

    [Fact]
    public void Secret_WithoutASafeStore_SavesNothing()
    {
        _secrets.IsAvailable = false;
        var definition = new SettingDefinition("test.token", "test.token", SettingType.Secret, null);
        using var form = Form(definition);
        var field = (SecretField)form.Fields[0];

        Assert.False(field.Save("plain-secret-value"));

        Assert.Equal(SecretField.UnavailableKey, field.ErrorKey);
        Assert.Null(_services.Settings.Values.GetSecretRef(definition.Key));
        Assert.False(File.Exists(_services.Paths.SettingsFile));
    }

    [Fact]
    public void Hotkey_UsedByAnotherField_IsRefused()
    {
        var panel = new SettingDefinition("test.panel", "startup.enabled", SettingType.Hotkey, "Ctrl+Alt+K");
        var other = new SettingDefinition("test.other", "test.other", SettingType.Hotkey, "");
        using var form = Form(panel, other);
        var field = (HotkeyField)form.Fields[1];

        Assert.False(field.Capture(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4B));

        Assert.Equal(HotkeyField.TakenKey, field.ErrorKey);
        Assert.Contains(form.Fields[0].Label, field.Error);
        Assert.Equal(string.Empty, _services.Settings.Values.GetString(other.Key));

        Assert.False(field.Capture(HotkeyModifiers.None, 0x4B));
        Assert.Equal(SettingValidator.HotkeyModifierKey, field.ErrorKey);

        Assert.True(field.Capture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x4B));
        Assert.Equal("Ctrl+Shift+K", _services.Settings.Values.GetString(other.Key));
        Assert.Null(field.ErrorKey);
    }

    [Fact]
    public void VisibleWhen_ShowsTheFieldOnlyWhileTheOtherSettingMatches()
    {
        var toggle = new SettingDefinition("test.on", "test.on", SettingType.Bool, false);
        var dependent = new SettingDefinition("test.extra", "test.extra", SettingType.String, "") { VisibleWhen = ("test.on", true) };
        using var form = Form(toggle, dependent);

        Assert.False(form.Fields[1].IsVisible);
        ((ToggleField)form.Fields[0]).IsOn = true;
        Assert.True(form.Fields[1].IsVisible);
        _services.Settings.Set(toggle.Key, false);
        Assert.False(form.Fields[1].IsVisible);
    }

    [Fact]
    public void AdvancedFields_GoUnderMoreOptions_AndGroupsKeepTheirFirstOrder()
    {
        using var form = Form(
            new SettingDefinition("test.a", "test.a", SettingType.Bool, false) { GroupKey = "group.one" },
            new SettingDefinition("test.b", "test.b", SettingType.Bool, false),
            new SettingDefinition("test.c", "test.c", SettingType.Bool, false) { GroupKey = "group.one" },
            new SettingDefinition("test.d", "test.d", SettingType.Bool, false) { Advanced = true });

        Assert.Equal(new string?[] { "group.one", null }, form.Groups.Select(g => g.TitleKey));
        Assert.Equal(new[] { "test.a", "test.c" }, form.Groups[0].Fields.Select(f => f.Key));
        Assert.Equal("test.d", Assert.Single(form.AdvancedFields).Key);
        Assert.True(form.HasAdvanced);
    }

    [Fact]
    public void KeyOrder_PutsThoseKeysFirst()
    {
        var definitions = new[]
        {
            new SettingDefinition("x.language", "x", SettingType.Bool, false),
            new SettingDefinition("x.other", "x", SettingType.Bool, false),
            new SettingDefinition("x.show", "x", SettingType.Bool, false),
        };
        using var form = new SettingsFormViewModel(definitions, Context(), keyOrder: ["x.show", "x.startup", "x.language"]);

        Assert.Equal(new[] { "x.show", "x.language", "x.other" }, form.Fields.Select(f => f.Key));
    }

    [Fact]
    public void Duration_ShowsTheLargestUnit_AndWritesSeconds()
    {
        var definition = new SettingDefinition("test.duration", "test.duration", SettingType.Duration, 300);
        using var form = Form(definition);
        var field = (DurationField)form.Fields[0];

        Assert.Equal("5", field.Text);
        Assert.Equal(60, field.Unit!.Seconds);

        field.Text = "2";
        Assert.Equal(TimeSpan.FromMinutes(2), _services.Settings.Values.GetDuration(definition.Key));

        field.Unit = field.Units.Single(u => u.Seconds == 3600);
        Assert.Equal(TimeSpan.FromHours(2), _services.Settings.Values.GetDuration(definition.Key));

        field.Text = "soon";
        Assert.Equal(SettingValidator.NumberKey, field.ErrorKey);
        Assert.Equal(TimeSpan.FromHours(2), _services.Settings.Values.GetDuration(definition.Key));
    }

    [Fact]
    public void List_AddsAndRemovesItems_UpToMaxItems()
    {
        var definition = new SettingDefinition("test.list", "test.list", SettingType.StringList, null) { MaxItems = 2 };
        using var form = Form(definition);
        var field = (StringListField)form.Fields[0];

        field.NewItem = "first";
        field.AddCommand.Execute(null);
        field.NewItem = "second";
        field.AddCommand.Execute(null);

        Assert.Equal(new[] { "first", "second" }, _services.Settings.Values.GetStringList(definition.Key));
        Assert.Equal(string.Empty, field.NewItem);
        Assert.True(field.IsFull);
        Assert.False(field.AddCommand.CanExecute(null));

        field.Items[0].Remove.Execute(null);
        Assert.Equal(new[] { "second" }, _services.Settings.Values.GetStringList(definition.Key));
        Assert.False(field.IsFull);
    }

    [Fact]
    public void TimeZone_SearchFiltersTheList_AndPickingWritesTheId()
    {
        var definition = new SettingDefinition("test.zone", "test.zone", SettingType.TimeZone, "UTC");
        using var form = Form(definition);
        var field = (TimeZoneField)form.Fields[0];
        var all = field.Matches.Count;

        field.Query = "Singapore";
        Assert.InRange(field.Matches.Count, 1, all - 1);

        field.Selected = field.Matches[0];
        Assert.Equal(field.Matches[0].Id, _services.Settings.Values.GetString(definition.Key));
    }

    [Fact]
    public void ChangesMadeElsewhere_ShowInTheForm()
    {
        var definition = new SettingDefinition("test.flag", "test.flag", SettingType.Bool, false);
        using var form = Form(definition);

        _services.Settings.Set(definition.Key, true);

        Assert.True(((ToggleField)form.Fields[0]).IsOn);
    }

    [Fact]
    public void ReadOnlySettings_SayTheChangeIsNotSaved()
    {
        var path = Path.Combine(_services.Paths.Roaming, "newer.json");
        Directory.CreateDirectory(_services.Paths.Roaming);
        File.WriteAllText(path, """{ "schemaVersion": 99, "values": {} }""");
        var store = new SettingsStore(path);
        store.Load();
        var definition = new SettingDefinition("test.flag", "test.flag", SettingType.Bool, false);
        store.Register([definition]);
        using var form = new SettingsFormViewModel([definition], new SettingsFormContext(new AppSettingsTarget(store), _services.Text, _secrets));
        var field = (ToggleField)form.Fields[0];

        field.IsOn = true;

        Assert.True(field.IsOn);
        Assert.Equal(SettingField.ReadOnlyKey, field.ErrorKey);
        Assert.DoesNotContain("test.flag", File.ReadAllText(path));
    }

    [Fact]
    public void Labels_FollowTheLanguage()
    {
        var text = new Localizer(new AssemblyStringResources(typeof(App).Assembly), Localizer.English);
        var definition = new SettingDefinition("startup.enabled", "startup.enabled", SettingType.Bool, false);
        _services.Settings.Register([definition]);
        using var form = new SettingsFormViewModel([definition], new SettingsFormContext(new AppSettingsTarget(_services.Settings), text, _secrets));
        var field = (ToggleField)form.Fields[0];
        Assert.Equal("Start with Windows", field.Label);
        Assert.Equal("Off", field.StateText);

        text.SetLanguage(Localizer.Malay);

        Assert.Equal("Mula bersama Windows", field.Label);
        Assert.Equal("Mati", field.StateText);
    }

    [Fact]
    public void PickingALanguage_SwitchesInOneClick_AndKeepsTheChoiceSelected() => Sta.Run(() =>
    {
        // The language field relabels its own options when the language changes, in the middle of the click.
        var text = new Localizer(new AssemblyStringResources(typeof(App).Assembly), Localizer.English);
        var definition = new SettingDefinition("language.choice", "language.choice", SettingType.Choice, "system", ["system", Localizer.English, Localizer.Malay]);
        _services.Settings.Register([definition]);
        _services.Settings.Changed += (_, e) =>
        {
            if (e.Keys.Contains("language.choice"))
                text.SetLanguage(_services.Settings.Values.GetString("language.choice") == Localizer.Malay ? Localizer.Malay : Localizer.English);
        };
        using var form = new SettingsFormViewModel([definition], new SettingsFormContext(new AppSettingsTarget(_services.Settings), text, _secrets));
        var host = SettingsUi.Render(form);
        var list = Assert.Single(SettingsUi.Visible<ListBox>(host));
        var field = Assert.IsType<ChoiceField>(form.Fields[0]);

        list.SelectedItem = field.Options.Single(o => o.Value == Localizer.Malay);
        SettingsUi.Layout(host);

        Assert.Equal(Localizer.Malay, _services.Settings.Values.GetString("language.choice"));
        Assert.Equal(Localizer.Malay, text.Language);
        Assert.Equal(Localizer.Malay, field.Selected?.Value);
        Assert.Equal(Localizer.Malay, (list.SelectedItem as ChoiceOption)?.Value);
        Assert.Equal("Bahasa Melayu", field.Selected?.Label);

        // Back again: every label changes this time, and the pick still holds after one click.
        list.SelectedItem = field.Options.Single(o => o.Value == "system");
        SettingsUi.Layout(host);

        Assert.Equal("system", _services.Settings.Values.GetString("language.choice"));
        Assert.Equal(Localizer.English, text.Language);
        Assert.Equal("system", field.Selected?.Value);
        Assert.Same(field.Selected, list.SelectedItem);
        Assert.Contains(field.Selected, field.Options);
    });

    [Fact]
    public void Relabelling_NeverWritesAPick() => Sta.Run(() =>
    {
        var text = new Localizer(new AssemblyStringResources(typeof(App).Assembly), Localizer.English);
        var definition = new SettingDefinition("language.choice", "language.choice", SettingType.Choice, "system", ["system", Localizer.English, Localizer.Malay]);
        _services.Settings.Register([definition]);
        _services.Settings.Set("language.choice", Localizer.English);
        using var form = new SettingsFormViewModel([definition], new SettingsFormContext(new AppSettingsTarget(_services.Settings), text, _secrets));
        var host = SettingsUi.Render(form);
        var writes = 0;
        _services.Settings.Changed += (_, _) => writes++;

        text.SetLanguage(Localizer.Malay);
        SettingsUi.Layout(host);
        text.SetLanguage(Localizer.English);
        SettingsUi.Layout(host);

        Assert.Equal(0, writes);
        Assert.Equal(Localizer.English, ((ChoiceField)form.Fields[0]).Selected?.Value);
    });

    [Fact]
    public void WidgetSettings_ChangeOnlyThatInstance()
    {
        _services.Layout.Load();
        _services.Layout.Update(d =>
        {
            d.SetWidgetSetting("clock", "use24Hour", true);
            d.SetWidgetSetting("clock#second", "use24Hour", true);
        });
        var definition = new SettingDefinition("use24Hour", "clock.use24Hour", SettingType.Bool, true);
        var before = Snapshot("clock#second");
        var target = new WidgetSettingsTarget(_services.Layout, "clock", [definition]);
        using var form = new SettingsFormViewModel([definition], new SettingsFormContext(target, _services.Text, _secrets));
        var changes = 0;
        _services.Layout.Changed += (_, _) => changes++;

        ((ToggleField)form.Fields[0]).IsOn = false;

        Assert.Equal(1, changes);
        Assert.False(_services.Layout.Document.GetWidgetSettings("clock")["use24Hour"].GetBoolean());
        Assert.Equal(before, Snapshot("clock#second"));
        _services.Layout.Flush();
        using (var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_services.Paths.LayoutFile)))
            Assert.False(saved.RootElement.GetProperty("widgetSettings").GetProperty("clock").GetProperty("use24Hour").GetBoolean());

        // Writing the same value again changes nothing, so nothing restarts.
        ((ToggleField)form.Fields[0]).IsOn = false;
        Assert.Equal(1, changes);

        Assert.Equal(SettingWriteResult.Applied, target.Write("use24Hour", null));
        Assert.False(_services.Layout.Document.WidgetSettings.ContainsKey("clock"));
        Assert.Equal(before, Snapshot("clock#second"));
    }

    [Fact]
    public void Dispose_StopsFollowingTheSettings()
    {
        var definition = new SettingDefinition("test.flag", "test.flag", SettingType.Bool, false);
        var form = Form(definition);
        var field = (ToggleField)form.Fields[0];

        form.Dispose();
        _services.Settings.Set(definition.Key, true);

        Assert.False(field.IsOn);
    }

    private string Snapshot(string id) => System.Text.Json.JsonSerializer.Serialize(_services.Layout.Document.GetWidgetSettings(id));

    private SettingsFormContext Context() => new(new AppSettingsTarget(_services.Settings), _services.Text, _secrets);

    private SettingsFormViewModel Form(params SettingDefinition[] definitions)
    {
        _services.Settings.Register(definitions);
        return new SettingsFormViewModel(definitions, Context());
    }
}
