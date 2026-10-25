using Atap.Core.LocalApi;

namespace Atap.Core.Tests.LocalApi;

public class PushActivityListTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void New_IsEmptyWithNoExpiry()
    {
        var list = new PushActivityList();

        Assert.True(list.IsEmpty);
        Assert.Empty(list.Items);
        Assert.Null(list.Latest);
        Assert.Null(list.NextExpiry);
    }

    [Fact]
    public void Push_NewId_AddsActivityFromRequest()
    {
        var list = new PushActivityList();
        var request = new PushRequest("backup", "Backup NAS")
        {
            Subtitle = "Disk 2",
            Progress = 64,
            State = PushState.Running,
            Icon = PushIcon.Backup,
            Accent = PushAccent.Blue,
            Ttl = TimeSpan.FromMinutes(10),
        };

        var outcome = list.Push(request, Start, "powershell");

        Assert.Equal(PushChange.Added, outcome.Change);
        Assert.Null(outcome.EvictedId);
        Assert.Empty(outcome.ExpiredIds);
        var activity = Assert.Single(list.Items);
        Assert.Same(activity, outcome.Activity);
        Assert.Equal("backup", activity.Id);
        Assert.Equal("Backup NAS", activity.Title);
        Assert.Equal("Disk 2", activity.Subtitle);
        Assert.Equal(64, activity.Progress);
        Assert.Equal(PushIcon.Backup, activity.Icon);
        Assert.Equal(PushAccent.Blue, activity.Accent);
        Assert.Equal("powershell", activity.Source);
        Assert.Equal(Start, activity.CreatedAt);
        Assert.Equal(Start, activity.UpdatedAt);
        Assert.Equal(Start.AddMinutes(10), activity.ExpiresAt);
        Assert.False(list.IsEmpty);
        Assert.Same(activity, list.Find("backup"));
    }

    [Fact]
    public void Push_WithoutTtl_UsesFifteenMinutes()
    {
        var list = new PushActivityList();

        var outcome = list.Push(new PushRequest("a", "x"), Start);

        Assert.Equal(Start.AddMinutes(15), outcome.Activity.ExpiresAt);
        Assert.Equal(Start.AddMinutes(15), list.NextExpiry);
    }

    [Fact]
    public void Push_SameId_UpdatesInPlace()
    {
        var list = new PushActivityList();
        list.Push(new PushRequest("first", "First"), Start);
        list.Push(new PushRequest("backup", "Backup NAS") { Progress = 10, Ttl = TimeSpan.FromMinutes(5) }, Start);
        list.Push(new PushRequest("last", "Last"), Start);
        var later = Start.AddMinutes(2);

        var outcome = list.Push(new PushRequest("backup", "Backup NAS") { Progress = 70, Ttl = TimeSpan.FromMinutes(5) }, later);

        Assert.Equal(PushChange.Updated, outcome.Change);
        Assert.Equal(3, list.Count);
        Assert.Equal(new[] { "first", "backup", "last" }, list.Items.Select(item => item.Id));
        var activity = list.Items[1];
        Assert.Equal(70, activity.Progress);
        Assert.Equal(Start, activity.CreatedAt);
        Assert.Equal(later, activity.UpdatedAt);
        Assert.Equal(later.AddMinutes(5), activity.ExpiresAt);
    }

    [Fact]
    public void Push_SameId_ReplacesEveryField()
    {
        var list = new PushActivityList();
        list.Push(new PushRequest("a", "Copying") { Subtitle = "photos", Progress = 50, Icon = PushIcon.Upload, Accent = PushAccent.Teal }, Start, "python");

        var outcome = list.Push(new PushRequest("a", "Copied") { State = PushState.Done }, Start.AddSeconds(30));

        var activity = outcome.Activity;
        Assert.Equal("Copied", activity.Title);
        Assert.Equal(PushState.Done, activity.State);
        Assert.Null(activity.Subtitle);
        Assert.Null(activity.Progress);
        Assert.Null(activity.Icon);
        Assert.Null(activity.Accent);
        Assert.Null(activity.Source);
    }

    [Fact]
    public void Push_SixthId_EvictsTheLeastRecentlyPushed()
    {
        var list = new PushActivityList();
        for (var i = 0; i < PushActivityList.Capacity; i++)
            list.Push(new PushRequest($"job-{i}", $"Job {i}"), Start.AddSeconds(i));

        var outcome = list.Push(new PushRequest("job-5", "Job 5"), Start.AddSeconds(10));

        Assert.Equal(PushChange.Added, outcome.Change);
        Assert.Equal("job-0", outcome.EvictedId);
        Assert.Equal(PushActivityList.Capacity, list.Count);
        Assert.Equal(new[] { "job-1", "job-2", "job-3", "job-4", "job-5" }, list.Items.Select(item => item.Id));
    }

    [Fact]
    public void Push_SixthId_KeepsAnActivityThatWasJustUpdated()
    {
        var list = new PushActivityList();
        for (var i = 0; i < PushActivityList.Capacity; i++)
            list.Push(new PushRequest($"job-{i}", $"Job {i}"), Start.AddSeconds(i));
        list.Push(new PushRequest("job-0", "Job 0") { Progress = 90 }, Start.AddSeconds(5));

        var outcome = list.Push(new PushRequest("job-5", "Job 5"), Start.AddSeconds(6));

        Assert.Equal("job-1", outcome.EvictedId);
        Assert.NotNull(list.Find("job-0"));
        Assert.Null(list.Find("job-1"));
    }

    [Fact]
    public void Push_ManyIds_NeverHoldsMoreThanCapacity()
    {
        var list = new PushActivityList();

        for (var i = 0; i < 100; i++)
        {
            list.Push(new PushRequest($"id-{i}", "x"), Start.AddSeconds(i));
            Assert.True(list.Count <= PushActivityList.Capacity);
        }

        Assert.Equal(PushActivityList.Capacity, list.Count);
        Assert.Equal(new[] { "id-95", "id-96", "id-97", "id-98", "id-99" }, list.Items.Select(item => item.Id));
    }

    [Fact]
    public void Push_SameTimeOnFullList_EvictsTheEarliestInList()
    {
        var list = new PushActivityList();
        for (var i = 0; i < PushActivityList.Capacity; i++)
            list.Push(new PushRequest($"job-{i}", "x"), Start);

        Assert.Equal("job-0", list.Push(new PushRequest("job-5", "x"), Start).EvictedId);
    }

    [Fact]
    public void Push_OnFullListWithExpiredItem_RemovesExpiredInsteadOfEvicting()
    {
        var list = new PushActivityList();
        list.Push(new PushRequest("short", "x") { Ttl = TimeSpan.FromSeconds(10) }, Start);
        for (var i = 1; i < PushActivityList.Capacity; i++)
            list.Push(new PushRequest($"job-{i}", "x"), Start);

        var outcome = list.Push(new PushRequest("new", "x"), Start.AddSeconds(10));

        Assert.Null(outcome.EvictedId);
        Assert.Equal(new[] { "short" }, outcome.ExpiredIds);
        Assert.Equal(PushActivityList.Capacity, list.Count);
        Assert.Null(list.Find("short"));
    }

    [Fact]
    public void Push_IdThatExpiredButWasNotRemoved_IsAddedFresh()
    {
        var list = new PushActivityList();
        list.Push(new PushRequest("a", "Old") { Ttl = TimeSpan.FromSeconds(10) }, Start);
        var later = Start.AddMinutes(1);

        var outcome = list.Push(new PushRequest("a", "New"), later);

        Assert.Equal(PushChange.Added, outcome.Change);
        Assert.Equal(new[] { "a" }, outcome.ExpiredIds);
        Assert.Equal(later, outcome.Activity.CreatedAt);
    }

    [Fact]
    public void RemoveExpired_RemovesAtTheExpiryInstantNotBefore()
    {
        var list = new PushActivityList();
        list.Push(new PushRequest("a", "x") { Ttl = TimeSpan.FromSeconds(30) }, Start);
        list.Push(new PushRequest("b", "x") { Ttl = TimeSpan.FromMinutes(5) }, Start);

        Assert.Empty(list.RemoveExpired(Start.AddSeconds(30).AddTicks(-1)));
        Assert.Equal(2, list.Count);

        Assert.Equal(new[] { "a" }, list.RemoveExpired(Start.AddSeconds(30)));
        Assert.Equal("b", Assert.Single(list.Items).Id);

        Assert.Equal(new[] { "b" }, list.RemoveExpired(Start.AddHours(1)));
        Assert.True(list.IsEmpty);
    }

    [Fact]
    public void RemoveExpired_ReturnsIdsInListOrder()
    {
        var list = new PushActivityList();
        list.Push(new PushRequest("a", "x") { Ttl = TimeSpan.FromSeconds(10) }, Start);
        list.Push(new PushRequest("b", "x") { Ttl = TimeSpan.FromHours(1) }, Start);
        list.Push(new PushRequest("c", "x") { Ttl = TimeSpan.FromSeconds(20) }, Start);

        Assert.Equal(new[] { "a", "c" }, list.RemoveExpired(Start.AddMinutes(1)));
    }

    [Fact]
    public void NextExpiry_IsTheEarliestAndFollowsUpdates()
    {
        var list = new PushActivityList();
        list.Push(new PushRequest("a", "x") { Ttl = TimeSpan.FromMinutes(10) }, Start);
        list.Push(new PushRequest("b", "x") { Ttl = TimeSpan.FromMinutes(2) }, Start);

        Assert.Equal(Start.AddMinutes(2), list.NextExpiry);

        list.Push(new PushRequest("b", "x") { Ttl = TimeSpan.FromHours(1) }, Start.AddMinutes(1));
        Assert.Equal(Start.AddMinutes(10), list.NextExpiry);

        list.End("a");
        Assert.Equal(Start.AddMinutes(61), list.NextExpiry);
    }

    [Fact]
    public void TtlLimits_GiveExpiryBetweenTenSecondsAndOneDay()
    {
        var list = new PushActivityList();

        Assert.Equal(Start.AddSeconds(10), list.Push(new PushRequest("min", "x") { Ttl = PushProtocol.MinTtl }, Start).Activity.ExpiresAt);
        Assert.Equal(Start.AddDays(1), list.Push(new PushRequest("max", "x") { Ttl = PushProtocol.MaxTtl }, Start).Activity.ExpiresAt);
    }

    [Fact]
    public void Latest_IsTheMostRecentlyPushed()
    {
        var list = new PushActivityList();
        list.Push(new PushRequest("a", "x"), Start);
        list.Push(new PushRequest("b", "x"), Start.AddSeconds(1));
        Assert.Equal("b", list.Latest?.Id);

        list.Push(new PushRequest("a", "y"), Start.AddSeconds(2));
        Assert.Equal("a", list.Latest?.Id);
    }

    [Fact]
    public void End_RemovesById()
    {
        var list = new PushActivityList();
        list.Push(new PushRequest("a", "x"), Start);
        list.Push(new PushRequest("b", "x"), Start);

        Assert.True(list.End("a"));
        Assert.Equal("b", Assert.Single(list.Items).Id);
        Assert.False(list.End("a"));
        Assert.False(list.End("missing"));
    }

    [Fact]
    public void Clear_EmptiesTheList()
    {
        var list = new PushActivityList();
        list.Push(new PushRequest("a", "x"), Start);

        list.Clear();

        Assert.True(list.IsEmpty);
        Assert.Null(list.NextExpiry);
    }

    [Fact]
    public void Changed_IsRaisedOncePerChangeAndNeverForNoOps()
    {
        var list = new PushActivityList();
        var raised = 0;
        list.Changed += (_, _) => raised++;

        list.Push(new PushRequest("a", "x") { Ttl = TimeSpan.FromSeconds(10) }, Start);
        Assert.Equal(1, raised);

        list.Push(new PushRequest("b", "x"), Start.AddMinutes(1));   // also drops expired "a"
        Assert.Equal(2, raised);

        list.End("missing");
        list.RemoveExpired(Start.AddMinutes(2));
        Assert.Equal(2, raised);

        list.End("b");
        Assert.Equal(3, raised);

        list.Clear();
        Assert.Equal(3, raised);

        list.Push(new PushRequest("c", "x") { Ttl = TimeSpan.FromSeconds(10) }, Start);
        list.RemoveExpired(Start.AddMinutes(1));
        Assert.Equal(5, raised);
    }

    [Fact]
    public void Push_CleansAndShortensSource()
    {
        var list = new PushActivityList();
        var rlo = (char)0x202E;

        Assert.Equal("powershell", list.Push(new PushRequest("a", "x"), Start, $"power{rlo}shell\n").Activity.Source);
        Assert.Null(list.Push(new PushRequest("b", "x"), Start, " \t").Activity.Source);
        Assert.Null(list.Push(new PushRequest("c", "x"), Start, null).Activity.Source);

        var longSource = list.Push(new PushRequest("d", "x"), Start, new string('s', 100)).Activity.Source;
        Assert.Equal(new string('s', PushActivityList.MaxSourceLength), longSource);
    }

    [Fact]
    public void Push_InvalidRequest_ThrowsAndChangesNothing()
    {
        var list = new PushActivityList();

        Assert.Throws<ArgumentException>(() => list.Push(new PushRequest("Not Valid", "x"), Start));
        Assert.Throws<ArgumentException>(() => list.Push(new PushRequest("a", new string('x', 49)), Start));
        Assert.Throws<ArgumentException>(() => list.Push(new PushRequest("a", "x") { Ttl = TimeSpan.FromSeconds(5) }, Start));
        Assert.Throws<ArgumentException>(() => list.Push(new PushRequest("a", "x") { Progress = 101 }, Start));
        Assert.Throws<ArgumentNullException>(() => list.Push(null!, Start));
        Assert.True(list.IsEmpty);
    }

    [Fact]
    public void Items_CannotBeChangedFromOutside()
    {
        var list = new PushActivityList();
        list.Push(new PushRequest("a", "x"), Start);

        var items = (ICollection<PushActivity>)list.Items;

        Assert.True(items.IsReadOnly);
        Assert.Throws<NotSupportedException>(items.Clear);
    }

    [Fact]
    public void ParsedPush_FlowsIntoTheList()
    {
        var list = new PushActivityList();
        var parsed = PushProtocol.Parse("""{"op":"push","id":"backup","title":"Backup NAS","progress":64,"ttl":"10m"}""");
        Assert.True(parsed.IsSuccess);

        list.Push((PushRequest)parsed.Request, Start);
        list.Push((PushRequest)PushProtocol.Parse("""{"op":"push","id":"backup","title":"Backup NAS","progress":100,"state":"done","ttl":30}""").Request!, Start.AddMinutes(4));
        var end = (EndRequest)PushProtocol.Parse("""{"op":"end","id":"backup"}""").Request!;

        var activity = Assert.Single(list.Items);
        Assert.Equal(100, activity.Progress);
        Assert.Equal(PushState.Done, activity.State);
        Assert.Equal(Start.AddMinutes(4).AddSeconds(30), activity.ExpiresAt);
        Assert.True(list.End(end.Id));
        Assert.True(list.IsEmpty);
    }
}
