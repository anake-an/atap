using Atap.Composition;
using Atap.Core.Malaysia;
using Atap.Core.Widgets;
using Atap.Features.Malaysia;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Holidays;

namespace Atap.Tests.Malaysia;

/// <summary>The bundled gazette tables and the widget on fake time (25 October 2026, Malaysia).</summary>
public sealed class HolidaysWidgetTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new(MalaysiaKeys.HolidaysTypeId);

    public HolidaysWidgetTests() => _services.Settings.Register(new MalaysiaDescriptor().Settings);

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void EveryBundledFile_Parses_AndIsTheYearItsNameSays()
    {
        var files = BundledHolidays.Files(typeof(App).Assembly).ToList();

        Assert.True(files.Count >= 2);
        Assert.All(files, f =>
        {
            Assert.True(f.Result.Succeeded, $"{f.Name}: {f.Result.Error} {f.Result.Detail}");
            Assert.EndsWith($"holidays-{f.Result.Value!.Year}.json", f.Name, StringComparison.Ordinal);
            Assert.All(f.Result.Value.Holidays, h => Assert.Equal(f.Result.Value.Year, h.Date.Year));
        });
    }

    [Fact]
    public void TheGazette_SpotChecks()
    {
        var calendar = BundledHolidays.Calendar;

        // Federal: Hari Malaysia everywhere. Deepavali everywhere but Sarawak, and marked as subject to change.
        Assert.Contains(calendar.On(new DateOnly(2026, 9, 16), "SWK"), h => h.NameMs == "Hari Malaysia");
        Assert.Empty(calendar.On(new DateOnly(2026, 11, 8), "SWK"));
        Assert.True(Assert.Single(calendar.On(new DateOnly(2026, 11, 8), "SGR")).Tentative);
        // State: Sultan of Selangor's birthday only in Selangor.
        Assert.Single(calendar.On(new DateOnly(2026, 12, 11), "SGR"));
        Assert.Empty(calendar.On(new DateOnly(2026, 12, 11), "KUL"));
        // The second day of Hari Raya Haji is only for Kedah, Kelantan, Perlis and Terengganu.
        Assert.Single(calendar.On(new DateOnly(2027, 5, 18), "KTN"));
        Assert.Empty(calendar.On(new DateOnly(2027, 5, 18), "JHR"));
        // The second day of Chinese New Year comes from both tables, so it is nationwide.
        Assert.True(Assert.Single(calendar.On(new DateOnly(2027, 2, 7), "TRG")).IsNationwide);
    }

    [Fact]
    public async Task Widget_ShowsTheNextHolidayForTheState()
    {
        _services.Settings.Set(MalaysiaKeys.State, "PRK");

        var holidays = await StartAsync();

        // 25 October 2026 in Perak: the Sultan of Perak's birthday on 6 November comes before Deepavali.
        Assert.Equal("Sultan of Perak's Birthday", holidays.Next?.Name);
        Assert.Equal("12 days left", holidays.Next?.Until);
        Assert.False(holidays.Next!.Tentative);
        Assert.Equal(HolidaysViewModel.Shown, holidays.Upcoming.Count);
        Assert.True(holidays.Upcoming[1].Tentative);
        Assert.Equal("For Perak.", holidays.Scope);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task Widget_WithoutAState_ShowsOnlyNationwideHolidays()
    {
        var holidays = await StartAsync();

        // Deepavali is not a Sarawak holiday, so it is not nationwide: Christmas is the next one for everyone.
        Assert.Equal("Christmas Day", holidays.Next?.Name);
        Assert.Equal("Nationwide holidays only. Pick your area in Settings to add your state's.", holidays.Scope);
    }

    [Fact]
    public void PastTheLastYear_AsksToUpdateAtap()
    {
        var view = new HolidaysViewModel(_services.Text);
        var calendar = BundledHolidays.Calendar;
        var after = new DateOnly(calendar.LastYear!.Value, 12, 31);

        view.Show(after, null, calendar.Next(after, null), []);

        Assert.True(view.HasMessage);
        Assert.Equal("Update Atap to see next year's holidays.", view.Message);
    }

    private async Task<HolidaysViewModel> StartAsync()
    {
        var descriptor = new HolidaysDescriptor();
        var widget = descriptor.Create(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, new ObservableValue<WidgetVisibility>(WidgetVisibility.Hidden)));
        await widget.StartAsync(SettingValues.Empty);
        return Assert.IsType<HolidaysViewModel>(widget.ViewModel);
    }
}
