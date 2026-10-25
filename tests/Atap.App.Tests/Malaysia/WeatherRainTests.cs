using System.Text;
using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Malaysia;
using Atap.Core.Net;
using Atap.Core.Tests.Malaysia;
using Atap.Core.Widgets;
using Atap.Features.Malaysia;
using Atap.Tests.Infrastructure;
using Atap.Widgets.RainAlerts;
using Atap.Widgets.Weather;

namespace Atap.Tests.Malaysia;

/// <summary>Weather and rain alerts on fake time (17:00 in Malaysia on 25 October 2026) with recorded data.gov.my answers.</summary>
public sealed class WeatherRainTests : IDisposable
{
    // The recorded forecast is for Gombak. The widget asks for the state, so the sample is relabelled.
    private static readonly string SelangorForecast = DataGovFixtures.ForecastGombak.Replace("Ds055", "St008", StringComparison.Ordinal);

    // The recorded thunderstorm warning ended at 06:00. Kept running here so it is current at 17:00.
    private static readonly string CurrentWarnings = DataGovFixtures.Warnings.Replace(
        "\"valid_from\":\"2026-10-25T01:00:00\",\"valid_to\":\"2026-10-25T06:00:00\"",
        "\"valid_from\":\"2026-10-25T01:00:00\",\"valid_to\":\"2026-10-25T23:00:00\"",
        StringComparison.Ordinal);

    private readonly TestServices _services = new();
    private readonly List<OwnerLifetime> _lifetimes = [];
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);
    private readonly FakeDataGov _dataGov = new();

    public WeatherRainTests() => _services.Settings.Register(new MalaysiaDescriptor().Settings);

    public void Dispose()
    {
        foreach (var lifetime in _lifetimes)
            lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task Weather_WithoutAnArea_AsksForOne_AndFetchesNothing()
    {
        var weather = await StartWeatherAsync();

        Assert.False(weather.HasForecast);
        Assert.Equal("Pick your area on the Prayer times page in Settings.", weather.Message);
        Assert.Equal(0, _dataGov.Requests);
    }

    [Fact]
    public async Task Weather_ShowsTodayAndTheNextThreeDays_ForTheState()
    {
        _services.Settings.Set(MalaysiaKeys.State, "SGR");
        _dataGov.Forecast = SelangorForecast;

        var weather = await StartWeatherAsync();

        Assert.True(weather.HasForecast);
        Assert.Equal("Today", weather.Today?.Day);
        Assert.Equal("Selangor", weather.Place);
        Assert.Equal(WeatherViewModel.MoreDays, weather.Next.Count);
        Assert.NotEmpty(weather.TodayDetail);
        Assert.Contains("°", weather.Today!.Temperatures, StringComparison.Ordinal);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task Rain_ANewWarningForTheState_AlertsOnce_EvenAcrossARestart()
    {
        _services.Settings.Set(MalaysiaKeys.State, "SGR");
        _dataGov.Warnings = CurrentWarnings;

        var (first, rain) = await StartRainAsync();

        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal("Thunderstorms Warning", alert.Alert.Title);
        Assert.Equal(AlertPriority.High, alert.Alert.Priority);
        var row = Assert.Single(rain.Warnings);
        Assert.Equal("Thunderstorms Warning", row.Title);
        _services.Alerts.DismissCurrent();

        _visibility.Value = WidgetVisibility.Expanded;
        Assert.Null(_services.Alerts.Current);

        await first.StopAsync();
        await first.DisposeAsync();
        await StartRainAsync();
        Assert.Null(_services.Alerts.Current);
    }

    [Fact]
    public async Task Rain_OnlyWarningsThatAreCurrent()
    {
        _services.Settings.Set(MalaysiaKeys.State, "SGR");
        _dataGov.Warnings = DataGovFixtures.Warnings;

        var (_, rain) = await StartRainAsync();

        Assert.False(rain.HasWarnings);
        Assert.Equal("No warnings for Selangor.", rain.Message);
        Assert.Null(_services.Alerts.Current);
    }

    [Fact]
    public async Task Rain_AStatePickedAfterStart_FetchesTheWarningsAtOnce()
    {
        _dataGov.Warnings = CurrentWarnings;
        var (_, rain) = await StartRainAsync();
        Assert.Equal(0, _dataGov.Requests);

        _services.Settings.Set(MalaysiaKeys.State, "SGR");

        Assert.Equal(1, _dataGov.Requests);
        Assert.Equal("Thunderstorms Warning", Assert.Single(rain.Warnings).Title);
    }

    [Fact]
    public void Rain_WorksWithoutAPlacement() => Assert.True(new RainAlertsDescriptor().WorksWithoutPlacement);

    private async Task<WeatherViewModel> StartWeatherAsync()
    {
        var descriptor = new WeatherDescriptor();
        var widget = descriptor.Create(_services.CreateContext(descriptor.TypeId, descriptor, Lifetime(descriptor), _visibility, _dataGov));
        await widget.StartAsync(SettingValues.Empty);
        return Assert.IsType<WeatherViewModel>(widget.ViewModel);
    }

    private async Task<(IWidget Widget, RainAlertsViewModel ViewModel)> StartRainAsync()
    {
        var descriptor = new RainAlertsDescriptor();
        var widget = descriptor.Create(_services.CreateContext(descriptor.TypeId, descriptor, Lifetime(descriptor), _visibility, _dataGov));
        await widget.StartAsync(SettingValues.Empty);
        return (widget, Assert.IsType<RainAlertsViewModel>(widget.ViewModel));
    }

    private OwnerLifetime Lifetime(IWidgetDescriptor descriptor)
    {
        var lifetime = new OwnerLifetime(descriptor.TypeId);
        _lifetimes.Add(lifetime);
        return lifetime;
    }

    /// <summary>data.gov.my answering from the recorded samples.</summary>
    private sealed class FakeDataGov : IHttpDataService
    {
        public string? Forecast { get; set; }

        public string? Warnings { get; set; }

        public int Requests { get; private set; }

        public Task<FetchResult> GetAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Requests++;
            var body = request.Url.AbsolutePath.Contains("forecast", StringComparison.Ordinal) ? Forecast : Warnings;
            return Task.FromResult(body is null
                ? FetchResult.Fail(FetchStatus.Failed, FetchError.Network)
                : new FetchResult(FetchStatus.Fresh, Encoding.UTF8.GetBytes(body), DateTimeOffset.UtcNow, FetchError.None));
        }
    }
}
