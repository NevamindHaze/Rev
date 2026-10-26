using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rev.Core.Sync;

// The shared file format, the same one Rev on iPhone (rev.json) and the Rev Sync add-on (anki.json) use;
// Rev for Windows writes windows.json. Keep in step with Rev/Sync/SyncModels.swift and
// anki-addon/rev_sync/core.py. Times are seconds since 1970 (UTC); review times are milliseconds.

public sealed class SyncSnapshot
{
    [JsonPropertyName("format")] public int Format { get; set; } = 1;
    [JsonPropertyName("writer")] public string Writer { get; set; } = "";
    [JsonPropertyName("writtenAt")] public double WrittenAt { get; set; }
    [JsonPropertyName("cards")] public List<SyncCard> Cards { get; set; } = [];
    [JsonPropertyName("deleted")] public List<SyncTombstone> Deleted { get; set; } = [];
    [JsonPropertyName("reviews")] public List<SyncReview> Reviews { get; set; } = [];
}

public sealed class SyncCard
{
    /// <summary>"&lt;note id&gt;:&lt;ord&gt;", the same in every app.</summary>
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("noteId")] public string NoteId { get; set; } = "";
    [JsonPropertyName("ord")] public int Ord { get; set; }
    /// <summary>Anki style, sub-decks separated by "::".</summary>
    [JsonPropertyName("deck")] public string Deck { get; set; } = "";
    /// <summary>"basic", "reversed", "cloze", "typeIn" or "other".</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "basic";
    /// <summary>From Anki: rendered HTML. From Rev: plain text.</summary>
    [JsonPropertyName("front")] public string Front { get; set; } = "";
    [JsonPropertyName("back")] public string Back { get; set; } = "";
    /// <summary>HTML for each Anki field (sent by Rev; null when Anki's note type can't be edited from Rev).</summary>
    [JsonPropertyName("fields")] public Dictionary<string, string>? Fields { get; set; }
    [JsonPropertyName("clozeSource")] public string? ClozeSource { get; set; }
    [JsonPropertyName("clozeExtra")] public string? ClozeExtra { get; set; }
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
    [JsonPropertyName("leech")] public bool Leech { get; set; }
    [JsonPropertyName("editedAt")] public double EditedAt { get; set; }
    [JsonPropertyName("sched")] public SyncSchedule Sched { get; set; } = new();
    [JsonPropertyName("schedAt")] public double SchedAt { get; set; }
    [JsonPropertyName("lastReview")] public double? LastReview { get; set; }
}

public sealed class SyncSchedule
{
    [JsonPropertyName("phase")] public string Phase { get; set; } = "new";
    [JsonPropertyName("due")] public double Due { get; set; }
    [JsonPropertyName("interval")] public double Interval { get; set; }
    [JsonPropertyName("ease")] public double Ease { get; set; } = 2.5;
    [JsonPropertyName("reps")] public int Reps { get; set; }
    [JsonPropertyName("lapses")] public int Lapses { get; set; }
    [JsonPropertyName("stability")] public double? Stability { get; set; }
    [JsonPropertyName("difficulty")] public double? Difficulty { get; set; }
    [JsonPropertyName("suspended")] public bool Suspended { get; set; }
}

public sealed class SyncTombstone
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("at")] public double At { get; set; }
}

public sealed class SyncReview
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    /// <summary>Milliseconds since 1970 (Anki's review id).</summary>
    [JsonPropertyName("at")] public long At { get; set; }
    [JsonPropertyName("rating")] public int Rating { get; set; }
    [JsonPropertyName("interval")] public double Interval { get; set; }
}

/// <summary>What Rev for Windows remembers between syncs (kept in Rev's own data folder, never in the shared folder).</summary>
public sealed class SyncState
{
    /// <summary>Every card key sent in the last windows.json, to notice cards deleted in Rev since.</summary>
    [JsonPropertyName("exportedKeys")] public List<string> ExportedKeys { get; set; } = [];
    /// <summary>Cards deleted in Rev, and when (kept for 60 days).</summary>
    [JsonPropertyName("tombstones")] public Dictionary<string, double> Tombstones { get; set; } = [];
    [JsonPropertyName("lastAnkiWrittenAt")] public double? LastAnkiWrittenAt { get; set; }
    [JsonPropertyName("lastExportDigest")] public string? LastExportDigest { get; set; }

    public static SyncState Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<SyncState>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save(string path) => SyncFiles.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(this));

    public SyncState Copy() => JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(this))!;
}

/// <summary>What a sync did, for the message afterwards.</summary>
public sealed record SyncReport(int Added = 0, int Updated = 0, int Rescheduled = 0, int Deleted = 0, int Reviews = 0)
{
    public bool Any => Added + Updated + Rescheduled + Deleted + Reviews > 0;

    /// <summary>"3 new cards, 2 cards updated, …" (the same wording as Rev on iPhone).</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Added > 0) parts.Add(Text.Plural.Counted(Added, "new card"));
            if (Updated > 0) parts.Add($"{Text.Plural.Counted(Updated, "card")} updated");
            if (Rescheduled > 0) parts.Add($"{Text.Plural.Counted(Rescheduled, "schedule")} from Anki");
            if (Deleted > 0) parts.Add($"{Text.Plural.Counted(Deleted, "card")} deleted");
            if (Reviews > 0) parts.Add($"{Text.Plural.Counted(Reviews, "review")} from Anki");
            return parts.Count == 0 ? "Everything is up to date." : string.Join(", ", parts) + ".";
        }
    }
}
