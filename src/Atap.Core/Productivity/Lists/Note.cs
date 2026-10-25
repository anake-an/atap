using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Lists;

/// <summary>One quick note.</summary>
public sealed record Note
{
    public const int MaxLength = 20_000;
    public const int MaxTitleLength = 80;

    public string Id { get; set; } = "";

    public string Text { get; set; } = "";

    public bool Pinned { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>The first line that is not blank, trimmed and cut to 80 characters. Empty for an empty note.</summary>
    [JsonIgnore]
    public string Title
    {
        get
        {
            foreach (var line in (Text ?? "").Split('\n'))
            {
                var clean = ProductivityText.CleanLine(line, MaxTitleLength);
                if (clean.Length > 0)
                    return clean;
            }
            return string.Empty;
        }
    }

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    public static Note Create(string id, DateTimeOffset now, string? text = null) => new()
    {
        Id = id,
        Text = ProductivityText.CleanText(text, MaxLength),
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>
    /// Sets the text. Returns this same note when the cleaned text has not changed, so an edit
    /// that changes nothing never causes a save.
    /// </summary>
    public Note WithText(string? text, DateTimeOffset now)
    {
        var clean = ProductivityText.CleanText(text, MaxLength);
        return string.Equals(clean, Text, StringComparison.Ordinal) ? this : this with { Text = clean, UpdatedAt = now };
    }

    public Note WithPinned(bool pinned) => Pinned == pinned ? this : this with { Pinned = pinned };
}

/// <param name="Changed">False when the edit changed nothing, so no save is needed.</param>
public readonly record struct NoteEdit(NotesDocument Document, bool Changed);

/// <summary>The notes of one notes widget, stored as user data.</summary>
public sealed record NotesDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxNotes = 100;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public IReadOnlyList<Note> Notes { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool IsFull => ItemLists.Present(Notes).Count() >= MaxNotes;

    /// <summary>
    /// Repairs the values of a document that was read from a file: drops nulls, fixes ids and caps
    /// text length and the list. A value the reader cannot parse at all, such as words where a date
    /// belongs, fails the whole read before this runs.
    /// </summary>
    public NotesDocument Normalize() => this with
    {
        Notes = ItemLists.Normalize(Notes, MaxNotes, n => n.Id, (note, id) => note with
        {
            Id = id,
            Text = ProductivityText.CleanText(note.Text, Note.MaxLength),
        }),
    };

    public Note? Find(string id) =>
        ItemLists.Present(Notes).FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));

    /// <summary>Replaces the note with the same id, or adds it while there are fewer than 100.</summary>
    public NotesDocument Upsert(Note note) => this with { Notes = ItemLists.Upsert(Notes, note, n => n.Id, MaxNotes) };

    public NotesDocument Remove(string id) => this with { Notes = ItemLists.Remove(Notes, id, n => n.Id) };

    /// <summary>Sets the text of a note. Changed is false when the note is missing or the text is the same.</summary>
    public NoteEdit Edit(string id, string? text, DateTimeOffset now)
    {
        if (Find(id) is not { } note)
            return new NoteEdit(this, false);
        var edited = note.WithText(text, now);
        return ReferenceEquals(edited, note) ? new NoteEdit(this, false) : new NoteEdit(Upsert(edited), true);
    }

    /// <summary>Drops notes with no text, for example when the panel closes.</summary>
    public NotesDocument RemoveEmpty() => this with { Notes = ItemLists.Present(Notes).Where(n => !n.IsEmpty).ToList() };

    /// <summary>Pinned notes first, then the most recently changed.</summary>
    public IReadOnlyList<Note> Ordered() =>
        ItemLists.Present(Notes)
            .OrderByDescending(n => n.Pinned)
            .ThenByDescending(n => n.UpdatedAt)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();
}
