namespace Atap.Core.Tests;

public class CompactArbiterTests
{
    [Fact]
    public void SelectOwner_PicksHighestPriorityLiveWidget()
    {
        var media = FakeWidget.Live("media");
        var timer = FakeWidget.Live("timer");

        var owner = CompactArbiter.SelectOwner([media, timer], ["timer", "media"]);

        Assert.Same(timer, owner);
    }

    [Fact]
    public void SelectOwner_SkipsWidgetsThatAreNotLive()
    {
        var media = FakeWidget.Live("media");
        var timer = FakeWidget.Create("timer", live: false);

        var owner = CompactArbiter.SelectOwner([media, timer], ["timer", "media"]);

        Assert.Same(media, owner);
    }

    [Fact]
    public void SelectOwner_ReturnsNullWhenNothingIsLive()
    {
        var media = FakeWidget.Create("media");
        var timer = FakeWidget.Create("timer");

        Assert.Null(CompactArbiter.SelectOwner([media, timer], ["media", "timer"]));
    }

    [Fact]
    public void SelectOwner_IgnoresLiveWidgetsMissingFromPriority()
    {
        var media = FakeWidget.Live("media");

        Assert.Null(CompactArbiter.SelectOwner([media], ["timer"]));
    }

    [Fact]
    public void SelectOwner_IgnoresWidgetsWithoutCompactSupport()
    {
        var media = FakeWidget.Create("media", live: true, supportsCompact: false);

        Assert.Null(CompactArbiter.SelectOwner([media], ["media"]));
    }

    [Fact]
    public void SelectOwner_MatchesInstanceIdsNotTypeIds()
    {
        var nas = FakeWidget.Live("json-url#nas");
        var weather = FakeWidget.Live("json-url#weather");

        Assert.Same(weather, CompactArbiter.SelectOwner([nas, weather], ["json-url#weather", "json-url#nas"]));
        Assert.Null(CompactArbiter.SelectOwner([nas, weather], ["json-url"]));
    }
}
