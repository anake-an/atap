using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Net;
using Atap.Settings.Form;

namespace Atap.Widgets.JsonUrl;

/// <summary>
/// The Test button in a JSON URL instance's settings. It reads the form's current values, fetches the link once
/// with the widget's own HTTP access, and shows the value as the notch would. The start of the reply sits under
/// it, so the value to show is easy to find. Nothing is saved.
/// </summary>
internal sealed class JsonUrlTester : ObservableObject
{
    /// <summary>How much of the reply is shown, in characters.</summary>
    public const int SampleLength = 1500;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly SettingsFormContext _form;
    private string _statusKey = "json-url.testIdle";
    private ValueView? _preview;
    private string _sample = string.Empty;

    public JsonUrlTester(SettingsFormContext form)
    {
        _form = form;
        TestCommand = new AsyncRelayCommand(TestAsync);
        // Weak, so the localizer never keeps the tester of a closed form alive.
        WeakEventManager<ILocalizer, EventArgs>.AddHandler(form.Text, nameof(ILocalizer.LanguageChanged), OnLanguageChanged);
    }

    public IAsyncRelayCommand TestCommand { get; }

    public string ButtonText => _form.Text["json-url.testButton"];

    public string Label => _form.Text["json-url.test"];

    public string Status => _form.Text[_statusKey];

    /// <summary>The value as the notch would show it, or null before a test or when the link failed.</summary>
    public ValueView? Preview => _preview;

    public bool HasPreview => _preview is not null;

    public string SampleTitle => _form.Text["json-url.reply"];

    /// <summary>The start of the reply, laid out for reading.</summary>
    public string Sample => _sample;

    public bool HasSample => _sample.Length > 0;

    /// <summary>The string key of the line above the preview, for tests.</summary>
    internal string StatusKey => _statusKey;

    internal async Task TestAsync()
    {
        if (JsonUrlSetup.Read(_form.Target.Values, _form.Secrets, out var problem) is not { } setup)
        {
            Show(problem ?? "json-url.noLink", null, string.Empty);
            return;
        }
        if (_form.Http is not { } http)
        {
            Show("json-url.testUnavailable", null, string.Empty);
            return;
        }

        Show("json-url.testing", null, string.Empty);
        // MaxAge zero always asks the site, so the test shows what it says right now.
        var result = await http.GetAsync(setup.Request(TimeSpan.Zero));
        if (result.Status is FetchStatus.Failed or FetchStatus.Blocked)
        {
            Show(JsonUrlSetup.FailureKey(result.Error), null, string.Empty);
            return;
        }

        var payload = PayloadReader.Read(result.Body.Span, PayloadFormat.Auto, PayloadReader.JsonUrlMaxBytes);
        var view = setup.Template.Render(payload, null, _form.Text.Culture);
        var sample = SampleOf(payload, result.Body.Span);
        if (result.Status == FetchStatus.Stale)
            Show("json-url.testStale", view with { Stale = true }, sample);
        else
            Show(view.HasValue ? "json-url.testOk" : "json-url.testNoValue", view, sample);
    }

    /// <summary>JSON laid out with indents, or the plain text, cut at <see cref="SampleLength"/>.</summary>
    internal static string SampleOf(PayloadReadResult payload, ReadOnlySpan<byte> body)
    {
        string text;
        if (payload.IsOk && payload.Root.ValueKind is not JsonValueKind.String)
            text = JsonSerializer.Serialize(payload.Root, Indented);
        else
            text = Encoding.UTF8.GetString(body[..Math.Min(body.Length, SampleLength * 4)]).Trim();

        var clean = new StringBuilder(Math.Min(text.Length, SampleLength + 1));
        foreach (var c in text)
        {
            if (clean.Length >= SampleLength)
            {
                clean.Append('…');
                break;
            }
            if (c is '\n' or '\t' || !char.IsControl(c))
                clean.Append(c);
        }
        return clean.ToString();
    }

    private void Show(string statusKey, ValueView? preview, string sample)
    {
        _statusKey = statusKey;
        _preview = preview;
        _sample = sample;
        OnPropertyChanged(string.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}

/// <summary>
/// Builds a <see cref="JsonUrlTester"/> from the Custom field the settings form gives the editor template.
/// Anything else gives null.
/// </summary>
[ValueConversion(typeof(CustomField), typeof(JsonUrlTester))]
internal sealed class JsonUrlTesterFor : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CustomField field ? new JsonUrlTester(field.Context) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
