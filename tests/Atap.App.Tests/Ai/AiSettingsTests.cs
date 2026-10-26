using Atap.Composition;
using Atap.Core.Ai;
using Atap.Core.Settings;
using Atap.Core.Sync;
using Atap.Core.Widgets;
using Atap.Features.Ai;
using Atap.Features.NetworkPrivacy;
using Atap.Platform.Secrets;
using Atap.Settings.Cards.Ai;
using Atap.Settings.Cards.Privacy.ClearData;
using Atap.Settings.Shell;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Ai;

/// <summary>Ask AI's settings, where its keys are kept, and the Settings card that saves, checks and removes them.</summary>
public sealed class AiSettingsTests : IDisposable
{
    private const string Key = "sk-proj-TEST-not-a-real-key-123";

    private readonly TestServices _services = new();
    private readonly DpapiSecretStore _secrets;
    private readonly List<AiAccount> _checked = [];
    private readonly List<Uri> _opened = [];
    private AiError _checkResult = AiError.None;

    public AiSettingsTests()
    {
        _services.Settings.Load();
        _services.Settings.Register(new AiDescriptor().Settings);
        _services.Settings.Register(new NetworkPrivacyDescriptor().Settings);
        _secrets = new DpapiSecretStore(_services.Paths.SecretsFile);
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public void EverySetting_StaysOnThisPc_AndKeysAreSecrets()
    {
        var settings = new AiDescriptor().Settings;

        Assert.All(settings, s => Assert.StartsWith("ai.", s.Key));
        Assert.All(settings, s => Assert.Equal(SettingScope.Machine, s.Scope));
        // The card shows them, not the generated form.
        Assert.All(settings, s => Assert.Null(s.SectionId));
        var keys = settings.Where(s => s.Key.StartsWith(AiDescriptor.KeyPrefix, StringComparison.Ordinal)).ToList();
        Assert.Equal(AiProviders.Ids.Count, keys.Count);
        Assert.All(keys, s => Assert.Equal(SettingType.Secret, s.Type));
        Assert.All(settings, s => Assert.NotEqual(s.Label, _services.Text[s.Label]));
    }

    [Fact]
    public void ItIsOffByDefault_AndInSafeMode()
    {
        var descriptor = new AiDescriptor();
        var on = new SettingValues(descriptor.Settings, new Dictionary<string, System.Text.Json.JsonElement>
        {
            [AiDescriptor.EnabledKey] = System.Text.Json.JsonSerializer.SerializeToElement(true),
        });

        Assert.False(descriptor.IsEnabled(new SettingValues(descriptor.Settings, null), AppMode.Normal));
        Assert.True(descriptor.IsEnabled(on, AppMode.Normal));
        Assert.False(descriptor.IsEnabled(on, AppMode.Safe));
        Assert.Equal([AiDescriptor.EnabledKey], descriptor.EnabledBy);
    }

    [Fact]
    public void Sync_SkipsEveryAskAiSetting_KeysFirstOfAll()
    {
        var scope = SyncDefinitions.Scope(new AiDescriptor().Settings, []);

        foreach (var setting in new AiDescriptor().Settings)
            Assert.Equal(SyncUnitKind.Excluded, scope.Classify(SyncUnitNames.SettingPrefix + setting.Key));
        // Even a key with no definition, say from a newer Atap.
        Assert.True(SyncScope.IsAlwaysMachineKey("ai.something.new"));
    }

    [Fact]
    public void TheCard_IsOnTheHomePage_WithATitle()
    {
        var card = new AiCard();

        Assert.Equal(SettingsPages.Home, SettingsPages.Find(card.SectionId)?.Id);
        Assert.NotEqual(card.TitleKey, _services.Text[card.TitleKey]);
        Assert.Contains(typeof(AiCard), Discovery.SettingsCards(typeof(App).Assembly));
    }

    [Fact]
    public async Task Save_TrimsTheKey_LocksItAway_AndChecksIt()
    {
        using var card = Card();
        card.SelectedService = card.Services.Single(s => s.Provider == AiProvider.OpenAi);

        await card.SaveKeyAsync($"  {Key}\r\n");

        var reference = _services.Settings.Values.GetSecretRef(AiDescriptor.KeyFor(AiProvider.OpenAi));
        Assert.NotNull(reference);
        Assert.Equal(Key, _secrets.Get(reference!));
        Assert.True(card.HasKey);
        Assert.Equal("Key saved on this PC.", card.KeyState);
        var account = Assert.Single(_checked);
        Assert.Equal(AiProvider.OpenAi, account.Provider);
        Assert.True(account.HasKey);
        Assert.Equal("Your key works.", card.Message);
        Assert.False(card.HasWarning);

        // settings.json holds only the reference.
        _services.Settings.Flush();
        Assert.DoesNotContain(Key, File.ReadAllText(_services.Paths.SettingsFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AKeyForAnotherService_IsSavedWithAWarning()
    {
        using var card = Card();
        card.SelectedService = card.Services.Single(s => s.Provider == AiProvider.OpenAi);

        await card.SaveKeyAsync("sk-ant-api03-something");

        Assert.True(card.HasKey);
        Assert.Equal("This looks like a key for Claude. Check that you picked the right service.", card.Warning);
    }

    [Fact]
    public async Task AKeyTheServiceRefuses_SaysSoInPlainWords()
    {
        _checkResult = AiError.KeyNotAccepted;
        using var card = Card();

        await card.SaveKeyAsync(Key);

        Assert.Equal("Claude did not accept your key. Check it in Settings.", card.Message);
    }

    [Theory]
    [InlineData("   ", "Paste a key first.")]
    [InlineData("two words", "That does not look like a key. Copy it again from Claude.")]
    public async Task SomethingThatIsNotAKey_IsNotSaved(string pasted, string message)
    {
        using var card = Card();

        await card.SaveKeyAsync(pasted);

        Assert.Null(_services.Settings.Values.GetSecretRef(AiDescriptor.KeyFor(AiProvider.Anthropic)));
        Assert.Empty(_checked);
        Assert.Equal(message, card.Message);
    }

    [Fact]
    public async Task SavingAgain_ReplacesTheOldKey()
    {
        using var card = Card();
        await card.SaveKeyAsync("first-key");
        var first = _services.Settings.Values.GetSecretRef(AiDescriptor.KeyFor(AiProvider.Anthropic))!;

        await card.SaveKeyAsync("second-key");

        Assert.Null(_secrets.Get(first));
        Assert.Equal("second-key", _secrets.Get(_services.Settings.Values.GetSecretRef(AiDescriptor.KeyFor(AiProvider.Anthropic))!));
    }

    [Fact]
    public async Task RemoveKey_ForgetsItEverywhere()
    {
        using var card = Card();
        await card.SaveKeyAsync(Key);
        var reference = _services.Settings.Values.GetSecretRef(AiDescriptor.KeyFor(AiProvider.Anthropic))!;
        Assert.True(card.RemoveKeyCommand.CanExecute(null));

        card.RemoveKeyCommand.Execute(null);

        Assert.Null(_services.Settings.Values.GetSecretRef(AiDescriptor.KeyFor(AiProvider.Anthropic)));
        Assert.Null(_secrets.Get(reference));
        Assert.False(card.HasKey);
        Assert.Equal("No key saved yet.", card.KeyState);
        Assert.False(card.RemoveKeyCommand.CanExecute(null));
    }

    [Fact]
    public void GetAKey_OpensThatServicesKeyPage()
    {
        using var card = Card();
        card.SelectedService = card.Services.Single(s => s.Provider == AiProvider.Gemini);

        card.GetKeyCommand.Execute(null);

        Assert.Equal([new Uri("https://aistudio.google.com/app/apikey")], _opened);
        Assert.Equal("The company behind Gemini bills you for what you ask, not Atap. Check their prices first.", card.BillingText);
    }

    [Fact]
    public void YourOwnServer_IsUnderAdvanced_AndWarnsAboutPlainHttp()
    {
        using var card = Card();
        Assert.False(card.UsesOwnServer);

        card.UsesOwnServer = true;

        Assert.Equal(AiProvider.Custom, AiSetup.Provider(_services.Settings.Values));
        Assert.False(card.HasKeyPage);
        Assert.False(card.CanPickService);
        Assert.True(card.HasServerWarning);
        Assert.Equal("This address uses http, so your key crosses your home network unlocked. Use https if your server has it.", card.ServerWarning);
        card.Server = "not an address";
        card.SaveServerCommand.Execute(null);
        Assert.True(card.HasServerError);
        card.Server = "https://ai.example.com/v1";
        card.SaveServerCommand.Execute(null);
        Assert.False(card.HasServerError);
        Assert.False(card.HasServerWarning);
        Assert.Equal("https://ai.example.com/v1", _services.Settings.Values.GetString(AiDescriptor.ServerKey));

        // Your own server works without a key.
        Assert.NotNull(AiSetup.Account(_services.Settings.Values, _secrets, out var problem));
        Assert.Equal(AiSetupProblem.None, problem);

        card.UsesOwnServer = false;
        Assert.Equal(AiProvider.Anthropic, AiSetup.Provider(_services.Settings.Values));
    }

    [Fact]
    public void FastOrSmarter_PicksTheModel_AndAnotherNameCanBeTyped()
    {
        using var card = Card();
        Assert.Equal("claude-haiku-5-5", AiSetup.Model(_services.Settings.Values, AiProvider.Anthropic));

        card.SelectedSpeed = card.Speeds.Single(s => s.Speed == AiSpeed.Smarter);
        Assert.Equal("claude-sonnet-5-5", AiSetup.Model(_services.Settings.Values, AiProvider.Anthropic));

        card.Model = "bad name?";
        Assert.True(card.HasModelError);
        card.Model = "claude-opus-5-5";
        Assert.False(card.HasModelError);
        Assert.Equal("claude-opus-5-5", AiSetup.Model(_services.Settings.Values, AiProvider.Anthropic));

        // A new service starts again from Fast or Smarter.
        card.SelectedService = card.Services.Single(s => s.Provider == AiProvider.OpenAi);
        Assert.Equal(AiModels.Default(AiProvider.OpenAi, AiSpeed.Smarter), AiSetup.Model(_services.Settings.Values, AiProvider.OpenAi));
    }

    [Fact]
    public void WithoutAKey_NothingCanBeAsked()
    {
        Assert.Null(AiSetup.Account(_services.Settings.Values, _secrets, out var problem));
        Assert.Equal(AiSetupProblem.NoKey, problem);

        _services.Settings.Set(AiDescriptor.KeyFor(AiProvider.Anthropic), "secret:0123456789abcdef");
        Assert.Null(AiSetup.Account(_services.Settings.Values, _secrets, out problem));
        Assert.Equal(AiSetupProblem.KeyFromOtherPc, problem);
    }

    [Fact]
    public async Task ClearSavedData_RemovesTheKeysToo()
    {
        AiSetup.SaveKey(_services.Settings, _secrets, AiProvider.Anthropic, "one-key");
        AiSetup.SaveKey(_services.Settings, _secrets, AiProvider.Gemini, "two-key");
        var references = AiProviders.Ids.Select(id => _services.Settings.Values.GetSecretRef(AiDescriptor.KeyPrefix + id)).OfType<string>().ToList();
        Assert.Equal(2, references.Count);
        var model = new ClearDataViewModel(new SavedData(_services.Http, _services.Paths.Cache), _services.Text, run => Task.FromResult(run()),
            new AiKeyCleaner(_services.Settings, _secrets));
        Assert.True(model.HasKeys);

        await model.ClearCommand.ExecuteAsync(null);

        Assert.All(references, r => Assert.Null(_secrets.Get(r)));
        Assert.All(AiProviders.Ids, id => Assert.Null(_services.Settings.Values.GetSecretRef(AiDescriptor.KeyPrefix + id)));
        Assert.False(model.HasKeys);
        Assert.True(model.HasKeysRemoved);
    }

    [Fact]
    public void NetworkFeatures_ListsAskAi_AndFollowsItsSwitch()
    {
        using var card = new Atap.Settings.Cards.Privacy.NetworkFeatures.NetworkFeaturesViewModel(
            WidgetCatalog.FromDescriptors([]), _services.Layout, updateChecks: null, _services.Text, _services.Settings);

        var row = Assert.Single(card.Items);
        Assert.Equal("Ask AI", row.Name);
        Assert.Equal(
            "Sends what you type, and copied text if you choose, to the service you picked, only when you press Enter. Your key stays on this PC.",
            row.Detail);
        Assert.False(row.IsOn);
        Assert.Equal("Turn on in Home, under Ask AI", row.TurnOnHint);

        _services.Settings.Set(AiDescriptor.EnabledKey, true);

        Assert.True(Assert.Single(card.Items).IsOn);
    }

    [Fact]
    public void TheLog_HoldsTheProviderStatusAndKindOnly()
    {
        Assert.Equal("Ask AI to openai did not work: KeyNotAccepted, HTTP 401.", AiFeature.Describe(new AiAttempt(AiProvider.OpenAi, 401, AiError.KeyNotAccepted)));
        Assert.Null(AiFeature.Describe(new AiAttempt(AiProvider.OpenAi, 200, AiError.None)));
        Assert.Null(AiFeature.Describe(new AiAttempt(AiProvider.OpenAi, null, AiError.Stopped)));
    }

    [Fact]
    public void EveryError_HasPlainWords_InBothLanguages()
    {
        foreach (var error in Enum.GetValues<AiError>().Where(e => e != AiError.None))
        {
            foreach (var provider in Enum.GetValues<AiProvider>())
            {
                var key = AiText.ErrorKey(error, provider);
                Assert.NotEqual(key, _services.Text[key]);
                Assert.DoesNotContain("{", AiText.Error(_services.Text, error, provider));
            }
        }
    }

    private AiCardViewModel Card() => new(
        _services.Settings,
        _secrets,
        (account, _) =>
        {
            _checked.Add(account);
            return Task.FromResult(_checkResult);
        },
        _services.Text,
        _opened.Add);
}
