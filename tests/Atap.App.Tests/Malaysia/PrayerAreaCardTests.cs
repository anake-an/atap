using Atap.Core.Malaysia;
using Atap.Features.Malaysia;
using Atap.Platform.Location;
using Atap.Settings.Cards.Malaysia.PrayerArea;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Malaysia;

public sealed class PrayerAreaCardTests : IDisposable
{
    private static readonly GeoPoint KualaLumpur = new(3.139, 101.687);

    private readonly TestServices _services = new();
    private readonly FakeLocation _location = new();
    private readonly PrayerAreaViewModel _card;

    public PrayerAreaCardTests()
    {
        _services.Settings.Register(new MalaysiaDescriptor().Settings);
        _card = new PrayerAreaViewModel(_services.Settings, _services.Text, _location);
    }

    public void Dispose()
    {
        _card.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void Start_WithNoArea_SaysHowToBegin()
    {
        Assert.False(_card.HasState);
        Assert.Empty(_card.Zones);
        Assert.Equal(16, _card.States.Count);
        Assert.Equal("Pick your state and area to see prayer times. Nothing is fetched until you do.", _card.Summary);
    }

    [Fact]
    public void PickingAState_PicksItsFirstArea()
    {
        _card.SelectedState = _card.States.Single(s => s.Code == "SGR");

        Assert.Equal("SGR", _services.Settings.Values.GetString(MalaysiaKeys.State));
        Assert.Equal("SGR01", _services.Settings.Values.GetString(MalaysiaKeys.Zone));
        Assert.Equal("SGR01", _card.SelectedZone?.Code);
        Assert.Contains("(SGR01)", _card.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void PickingAnArea_SavesIt()
    {
        _card.SelectedState = _card.States.Single(s => s.Code == "SGR");
        _card.SelectedZone = _card.Zones.Single(z => z.Code == "SGR02");

        Assert.Equal("SGR02", _services.Settings.Values.GetString(MalaysiaKeys.Zone));
    }

    [Fact]
    public async Task FindMyArea_Suggests_AndSwitchesOnlyWhenAsked()
    {
        _location.Fix = new LocationFix(LocationOutcome.Found, KualaLumpur);
        var expected = ZoneLocator.FindNearest(KualaLumpur)!.Zone;

        await _card.FindCommand.ExecuteAsync(null);

        Assert.True(_card.HasSuggestion);
        Assert.StartsWith("Looks like ", _card.FindStatus, StringComparison.Ordinal);
        Assert.Equal(string.Empty, _services.Settings.Values.GetString(MalaysiaKeys.Zone));

        _card.UseSuggestionCommand.Execute(null);

        Assert.Equal(expected.Code, _services.Settings.Values.GetString(MalaysiaKeys.Zone));
        Assert.Equal(expected.StateCodes[0], _services.Settings.Values.GetString(MalaysiaKeys.State));
        Assert.False(_card.HasFindStatus);
    }

    [Fact]
    public async Task FindMyArea_WithLocationOff_OffersWindowsSettings()
    {
        _location.Fix = new LocationFix(LocationOutcome.Off, null);

        await _card.FindCommand.ExecuteAsync(null);

        Assert.True(_card.LocationIsOff);
        Assert.False(_card.HasSuggestion);
        _card.OpenLocationSettingsCommand.Execute(null);
        Assert.Equal(1, _location.SettingsOpened);
    }

    [Fact]
    public async Task FindMyArea_Abroad_SuggestsNothing()
    {
        _location.Fix = new LocationFix(LocationOutcome.Found, new GeoPoint(51.5, -0.12));

        await _card.FindCommand.ExecuteAsync(null);

        Assert.False(_card.HasSuggestion);
        Assert.Equal("You seem to be outside Malaysia. Pick your area above.", _card.FindStatus);
    }

    [Fact]
    public void Malay_NamesTheStatesInMalay()
    {
        _services.Text.SetLanguage(Core.Localization.Localizer.Malay);

        Assert.Contains(_card.States, s => s.Label == "Pulau Pinang");
    }

    private sealed class FakeLocation : ILocationSource
    {
        public LocationFix Fix { get; set; } = new(LocationOutcome.Failed, null);

        public int SettingsOpened { get; private set; }

        public Task<LocationFix> FindAsync(CancellationToken cancellationToken = default) => Task.FromResult(Fix);

        public void OpenWindowsSettings() => SettingsOpened++;
    }
}
