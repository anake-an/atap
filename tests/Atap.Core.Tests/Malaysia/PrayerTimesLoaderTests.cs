using System.Text;
using Atap.Core.Malaysia;
using Atap.Core.Net;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Core.Tests.Malaysia;

public class PrayerTimesLoaderTests
{
    // October's days relabelled as December, which also has 31 days. Only the dates matter here.
    private static readonly string December = EsolatFixtures.Sgr01October2026.Replace("-Okt-2026", "-Dis-2026", StringComparison.Ordinal);

    private readonly FakeTimeProvider _time = new(Myt.At(2026, 10, 10, 9, 0));
    private readonly FakeSource _source;
    private readonly PrayerTimesLoader _loader;

    public PrayerTimesLoaderTests()
    {
        _source = new FakeSource(_time);
        _loader = new PrayerTimesLoader(_source);
    }

    [Fact]
    public async Task Load_FetchesThisMonthFromEsolat()
    {
        _source.Esolat[10] = EsolatFixtures.Sgr01October2026;

        var schedule = await _loader.LoadAsync("SGR01", _time.GetUtcNow());

        Assert.NotNull(schedule?.DayOf(new DateOnly(2026, 10, 10)));
        Assert.Equal(PrayerSource.Esolat, schedule!.Source);
        Assert.Equal(new DateOnly(2026, 10, 9), schedule.FirstDate);
        Assert.Equal(1, _source.EsolatRequests);
        Assert.Equal(0, _source.MirrorRequests);
    }

    [Fact]
    public async Task Load_WhenEsolatFails_UsesTheMirror()
    {
        _source.Mirror[(2026, 10)] = MirrorFixtures.Sgr01October2026;

        var schedule = await _loader.LoadAsync("SGR01", _time.GetUtcNow());

        Assert.Equal(PrayerSource.Mirror, schedule?.Source);
        Assert.NotNull(schedule?.DayOf(new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public async Task Load_WhenEsolatHasNoRecord_UsesTheMirror()
    {
        _source.Esolat[10] = EsolatFixtures.NoRecord;
        _source.Mirror[(2026, 10)] = MirrorFixtures.Sgr01October2026;

        var schedule = await _loader.LoadAsync("SGR01", _time.GetUtcNow());

        Assert.Equal(PrayerSource.Mirror, schedule?.Source);
    }

    [Fact]
    public async Task Load_WhenEsolatAnswersForAnotherMonth_UsesTheMirror()
    {
        _time.SetUtcNow(Myt.At(2026, 11, 3, 9, 0));
        _source.Esolat[11] = EsolatFixtures.Sgr01October2026;

        var schedule = await _loader.LoadAsync("SGR01", _time.GetUtcNow());

        Assert.Null(schedule);
        Assert.Equal(1, _source.MirrorRequests);
    }

    [Fact]
    public async Task Load_WhenBothFail_IsNull()
    {
        Assert.Null(await _loader.LoadAsync("SGR01", _time.GetUtcNow()));
    }

    [Fact]
    public async Task Load_FromThe25th_AddsNextMonth()
    {
        _time.SetUtcNow(Myt.At(2026, 10, 25, 9, 0));
        _source.Esolat[10] = EsolatFixtures.Sgr01October2026;
        _source.Esolat[11] = EsolatFixtures.Sgr01November2026;

        var schedule = await _loader.LoadAsync("SGR01", _time.GetUtcNow());

        Assert.NotNull(schedule?.DayOf(new DateOnly(2026, 10, 25)));
        Assert.NotNull(schedule?.DayOf(new DateOnly(2026, 11, 30)));
    }

    [Fact]
    public async Task Load_WhenNextMonthFails_KeepsThisMonth()
    {
        _time.SetUtcNow(Myt.At(2026, 10, 25, 9, 0));
        _source.Esolat[10] = EsolatFixtures.Sgr01October2026;

        var schedule = await _loader.LoadAsync("SGR01", _time.GetUtcNow());

        Assert.NotNull(schedule?.DayOf(new DateOnly(2026, 10, 25)));
        Assert.Null(schedule?.DayOf(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public async Task Load_JanuaryFromDecember_AsksOnlyTheMirror()
    {
        _time.SetUtcNow(Myt.At(2026, 12, 26, 9, 0));
        _source.Esolat[12] = December;

        var schedule = await _loader.LoadAsync("SGR01", _time.GetUtcNow());

        Assert.NotNull(schedule?.DayOf(new DateOnly(2026, 12, 26)));
        Assert.Equal(1, _source.EsolatRequests);
        Assert.Equal([(2027, 1)], _source.MirrorMonths);
    }

    [Fact]
    public async Task TwoSimulatedMonths_AtMostTwoEsolatRequestsAMonth()
    {
        var time = new FakeTimeProvider(Myt.At(2026, 10, 1, 0, 0));
        var source = new FakeSource(time);
        var loader = new PrayerTimesLoader(source);
        source.Esolat[10] = EsolatFixtures.Sgr01October2026;
        source.Esolat[11] = EsolatFixtures.Sgr01November2026;
        source.Esolat[12] = December;

        // A load at every midnight and every 6 hours, as restarts and zone changes would cause.
        while (time.GetUtcNow() < Myt.At(2026, 12, 1, 0, 0))
        {
            var schedule = await loader.LoadAsync("SGR01", time.GetUtcNow());
            Assert.False(schedule?.IsStale(time.GetUtcNow()) ?? true);
            time.Advance(TimeSpan.FromHours(6));
        }

        Assert.Equal(2, source.EsolatRequestsIn(10));
        Assert.Equal(1, source.EsolatRequestsIn(11));
        Assert.Equal(0, source.MirrorRequests);
    }

    /// <summary>
    /// Answers from the fixtures it is given, and fails for anything else. Caches by URL for the request's
    /// MaxAge the way <see cref="HttpDataService"/> does, so the request counts are what the network would see.
    /// </summary>
    private sealed class FakeSource(FakeTimeProvider time) : IHttpDataService
    {
        private readonly Dictionary<Uri, (byte[] Body, DateTimeOffset At)> _cache = [];
        private readonly List<(Uri Url, DateTimeOffset At)> _sent = [];

        /// <summary>e-Solat answers by month. The year is always the current one.</summary>
        public Dictionary<int, string> Esolat { get; } = [];

        public Dictionary<(int Year, int Month), string> Mirror { get; } = [];

        public int EsolatRequests => _sent.Count(s => s.Url.Host == MalaysiaKeys.EsolatHost);

        public int MirrorRequests => _sent.Count(s => s.Url.Host == MalaysiaKeys.MirrorHost);

        public IReadOnlyList<(int Year, int Month)> MirrorMonths =>
            _sent.Where(s => s.Url.Host == MalaysiaKeys.MirrorHost).Select(s => (Query(s.Url, "year"), Query(s.Url, "month"))).ToList();

        public int EsolatRequestsIn(int month) =>
            _sent.Count(s => s.Url.Host == MalaysiaKeys.EsolatHost && MalaysiaTime.DateOf(s.At).Month == month);

        public Task<FetchResult> GetAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            var now = time.GetUtcNow();
            if (_cache.TryGetValue(request.Url, out var cached) && now - cached.At < request.MaxAge)
                return Task.FromResult(new FetchResult(FetchStatus.Cached, cached.Body, cached.At, FetchError.None));

            _sent.Add((request.Url, now));
            var body = request.Url.Host == MalaysiaKeys.EsolatHost
                ? Esolat.GetValueOrDefault(Query(request.Url, "month"))
                : Mirror.GetValueOrDefault((Query(request.Url, "year"), Query(request.Url, "month")));
            if (body is null)
                return Task.FromResult(FetchResult.Fail(FetchStatus.Failed, FetchError.HttpStatus));

            var bytes = Encoding.UTF8.GetBytes(body);
            _cache[request.Url] = (bytes, now);
            return Task.FromResult(new FetchResult(FetchStatus.Fresh, bytes, now, FetchError.None));
        }

        private static int Query(Uri url, string name) =>
            url.Query.TrimStart('?').Split('&')
                .Select(part => part.Split('=', 2))
                .Where(pair => pair[0] == name)
                .Select(pair => int.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture))
                .FirstOrDefault();
    }
}
