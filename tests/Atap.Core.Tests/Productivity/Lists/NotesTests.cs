using System.Text.Json;
using Atap.Core.Productivity.Lists;
using static Atap.Core.Tests.Productivity.TestZones;

namespace Atap.Core.Tests.Productivity.Lists;

public class NotesTests
{
    private const string SavedNotes = """
        {
          "schemaVersion": 1,
          "notes": [
            {
              "id": "mesyuarat",
              "text": "\n  Mesyuarat kelab Khamis\r\n- bawa laptop\r\n- slaid bab 2",
              "createdAt": "2026-10-20T09:00:00+08:00",
              "updatedAt": "2026-10-24T22:10:00+08:00"
            },
            {
              "id": "wifi",
              "text": "Kata laluan wifi rumah ada di belakang router",
              "pinned": true,
              "createdAt": "2026-09-01T09:00:00+08:00",
              "updatedAt": "2026-09-01T09:00:00+08:00"
            },
            {
              "id": "resepi",
              "text": "Nasi lemak: santan, daun pandan, halia",
              "createdAt": "2026-10-10T09:00:00+08:00",
              "updatedAt": "2026-10-25T07:45:00+08:00",
              "font": "mono"
            },
            { "id": "kosong", "text": "  \r\n ", "createdAt": "2026-10-25T08:00:00+08:00", "updatedAt": "2026-10-25T08:00:00+08:00" }
          ]
        }
        """;

    private static NotesDocument Load() =>
        JsonSerializer.Deserialize(SavedNotes, ListsJsonContext.Default.NotesDocument)!.Normalize();

    [Fact]
    public void Title_IsTheFirstLineWithText()
    {
        var notes = Load();

        Assert.Equal("Mesyuarat kelab Khamis", notes.Find("mesyuarat")!.Title);
        Assert.Equal(string.Empty, notes.Find("kosong")!.Title);
    }

    [Fact]
    public void Title_IsCutTo80Characters()
    {
        var note = Note.Create("n", My(2026, 10, 25, 9, 0), new string('a', 200));

        Assert.Equal(Note.MaxTitleLength, note.Title.Length);
    }

    [Fact]
    public void Edit_WithTheSameText_ChangesNothing()
    {
        var notes = Load();
        var text = notes.Find("resepi")!.Text;

        var edit = notes.Edit("resepi", text, My(2026, 10, 25, 9, 0));

        Assert.False(edit.Changed);
        Assert.Same(notes, edit.Document);
    }

    [Fact]
    public void Edit_WithNewText_UpdatesTheNoteAndItsTime()
    {
        var notes = Load();
        var now = My(2026, 10, 25, 9, 0);

        var edit = notes.Edit("resepi", "Nasi lemak: santan, daun pandan, halia, serai", now);

        Assert.True(edit.Changed);
        Assert.Equal(now, edit.Document.Find("resepi")!.UpdatedAt);
        Assert.EndsWith("serai", edit.Document.Find("resepi")!.Text);
    }

    [Fact]
    public void Edit_MissingNote_ChangesNothing()
    {
        var notes = Load();

        Assert.False(notes.Edit("tiada", "teks", My(2026, 10, 25, 9, 0)).Changed);
    }

    [Fact]
    public void WithText_RemovesStrayControlsAndCapsTheLength()
    {
        var note = Note.Create("n", My(2026, 10, 25, 9, 0));

        var edited = note.WithText("Baris\u0000 satu\r\nBaris dua" + new string('x', Note.MaxLength), My(2026, 10, 25, 9, 1));

        Assert.StartsWith("Baris satu\r\nBaris dua", edited.Text);
        Assert.Equal(Note.MaxLength, edited.Text.Length);
    }

    [Fact]
    public void Ordered_PinnedFirstThenMostRecentlyChanged()
    {
        var notes = Load();

        Assert.Equal(["wifi", "kosong", "resepi", "mesyuarat"], notes.Ordered().Select(n => n.Id));
    }

    [Fact]
    public void RemoveEmpty_DropsBlankNotes()
    {
        var notes = Load().RemoveEmpty();

        Assert.Null(notes.Find("kosong"));
        Assert.Equal(3, notes.Notes.Count);
    }

    [Fact]
    public void SavedFile_RoundTripsAndKeepsUnknownFields()
    {
        var notes = Load();

        var json = JsonSerializer.Serialize(notes, ListsJsonContext.Default.NotesDocument);
        var again = JsonSerializer.Deserialize(json, ListsJsonContext.Default.NotesDocument)!;

        Assert.Contains("\"font\": \"mono\"", json);
        Assert.DoesNotContain("\"title\"", json);
        Assert.Equal(notes.Notes.Select(n => (n.Id, n.Text, n.Pinned)), again.Notes.Select(n => (n.Id, n.Text, n.Pinned)));
    }

    [Fact]
    public void Upsert_StopsAtTheCap()
    {
        var now = My(2026, 10, 25, 9, 0);
        var notes = new NotesDocument();
        for (var i = 0; i < NotesDocument.MaxNotes + 5; i++)
            notes = notes.Upsert(Note.Create($"n{i}", now, "teks"));

        Assert.Equal(NotesDocument.MaxNotes, notes.Notes.Count);
        Assert.True(notes.IsFull);
    }
}
