using Atap.Core.Clipboard;
using Atap.Core.Productivity.Shelf;

namespace Atap.Core.Tests.Productivity;

public class ShelfAndClipboardTests
{
    [Fact]
    public void History_KeepsTwentyFiveNewestFirst_AndDedupes()
    {
        var history = new ClipboardHistory();
        for (var i = 0; i < 30; i++)
            history.Add($"item {i}");

        Assert.True(history.Add("item 10"));
        Assert.False(history.Add("item 10"));

        Assert.Equal(ClipboardHistory.MaxItems, history.Items.Count);
        Assert.Equal("item 10", history.Items[0]);
        Assert.Equal("item 29", history.Items[1]);
        Assert.Single(history.Items, i => i == "item 10");
    }

    [Fact]
    public void History_Restore_KeepsOrderAndLimits()
    {
        var history = new ClipboardHistory();
        history.Add("old");

        history.Restore(["newest", "  ", "older", "newest", .. Enumerable.Range(0, 30).Select(i => $"item {i}")]);

        Assert.Equal(ClipboardHistory.MaxItems, history.Items.Count);
        Assert.Equal(["newest", "older", "item 0"], history.Items.Take(3));
        Assert.DoesNotContain("old", history.Items);
    }

    [Fact]
    public void History_SkipsBlankAndTooLong()
    {
        var history = new ClipboardHistory();

        Assert.False(history.Add("   "));
        Assert.False(history.Add(new string('a', ClipboardHistory.MaxItemBytes + 1)));
        Assert.True(history.Add(new string('a', ClipboardHistory.MaxItemBytes)));
    }

    [Fact]
    public void ExcludedFormats_AreSkipped()
    {
        static int? Zero(string _) => 0;
        static int? One(string _) => 1;

        Assert.True(ClipboardHistory.ShouldSkip(["UnicodeText", "ExcludeClipboardContentFromMonitorProcessing"], One));
        Assert.True(ClipboardHistory.ShouldSkip(["UnicodeText", "Clipboard Viewer Ignore"], One));
        Assert.True(ClipboardHistory.ShouldSkip(["UnicodeText", "CanIncludeInClipboardHistory"], Zero));
        Assert.True(ClipboardHistory.ShouldSkip(["CanUploadToCloudClipboard", "UnicodeText"], Zero));
        Assert.False(ClipboardHistory.ShouldSkip(["UnicodeText", "CanIncludeInClipboardHistory"], One));
        Assert.False(ClipboardHistory.ShouldSkip(["UnicodeText", "Text"], Zero));
    }

    [Fact]
    public void Preview_IsTheFirstLine()
    {
        Assert.Equal("first…", ClipboardHistory.Preview("  first\r\nsecond"));
        Assert.Equal("abc…", ClipboardHistory.Preview("abcdef", 3));
        Assert.Equal("one", ClipboardHistory.Preview("one"));
    }

    [Fact]
    public void Shelf_AddsNewestFirst_MovesRepeats_IgnoresRelative()
    {
        var shelf = new ShelfDocument();

        shelf.Add([@"C:\a.txt", @"C:\b.txt"]);
        shelf.Add([@"c:\A.TXT", "relative.txt", " "]);

        Assert.Equal([@"c:\A.TXT", @"C:\b.txt"], shelf.Paths);
        Assert.True(shelf.Remove(@"C:\B.txt"));
        Assert.Equal([@"c:\A.TXT"], shelf.Paths);
    }
}
