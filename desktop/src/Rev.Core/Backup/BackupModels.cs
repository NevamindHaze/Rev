using System.Text.Json.Serialization;

namespace Rev.Core.Backup;

// The shape of a backup file: the same as Rev for iPhone's (Rev/Backup/BackupModels.swift), so a backup
// made on one can be restored on the other. Times are seconds since 1970 (UTC), as Rev writes them.
// Fields only Rev for Windows writes are marked "Windows"; the iPhone simply ignores them.

public sealed class BackupFile
{
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = 1;
    [JsonPropertyName("exportedAt")] public double ExportedAt { get; set; }
    [JsonPropertyName("newCardsPerDay")] public int? NewCardsPerDay { get; set; }
    [JsonPropertyName("userName")] public string? UserName { get; set; }
    /// <summary>Windows: "fsrs" or "sm2".</summary>
    [JsonPropertyName("scheduler")] public string? Scheduler { get; set; }
    /// <summary>Windows: FSRS target memory, 0.80–0.97.</summary>
    [JsonPropertyName("desiredRetention")] public double? DesiredRetention { get; set; }
    [JsonPropertyName("decks")] public List<DeckDto> Decks { get; set; } = [];
    [JsonPropertyName("reviewLogs")] public List<ReviewLogDto> ReviewLogs { get; set; } = [];
}

public sealed class DeckDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("createdAt")] public double CreatedAt { get; set; }
    [JsonPropertyName("cards")] public List<CardDto> Cards { get; set; } = [];
}

public sealed class CardDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("front")] public string Front { get; set; } = "";
    [JsonPropertyName("back")] public string Back { get; set; } = "";
    [JsonPropertyName("createdAt")] public double CreatedAt { get; set; }
    [JsonPropertyName("phase")] public string Phase { get; set; } = "new";
    [JsonPropertyName("due")] public double Due { get; set; }
    [JsonPropertyName("intervalDays")] public double IntervalDays { get; set; }
    [JsonPropertyName("ease")] public double Ease { get; set; } = 2.5;
    [JsonPropertyName("reps")] public int Reps { get; set; }
    [JsonPropertyName("lapses")] public int Lapses { get; set; }
    [JsonPropertyName("step")] public int Step { get; set; }
    [JsonPropertyName("lastReviewed")] public double? LastReviewed { get; set; }
    [JsonPropertyName("stability")] public double? Stability { get; set; }
    [JsonPropertyName("difficulty")] public double? Difficulty { get; set; }
    [JsonPropertyName("noteID")] public string? NoteId { get; set; }
    [JsonPropertyName("isSuspended")] public bool? IsSuspended { get; set; }
    [JsonPropertyName("isLeech")] public bool? IsLeech { get; set; }
    [JsonPropertyName("tags")] public string? Tags { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("clozeSource")] public string? ClozeSource { get; set; }
    [JsonPropertyName("clozeNumber")] public int? ClozeNumber { get; set; }
    [JsonPropertyName("clozeExtra")] public string? ClozeExtra { get; set; }
    [JsonPropertyName("noteOrd")] public int? NoteOrd { get; set; }
    [JsonPropertyName("editedAt")] public double? EditedAt { get; set; }
    [JsonPropertyName("schedChangedAt")] public double? SchedChangedAt { get; set; }
    [JsonPropertyName("fieldsReadOnly")] public bool? FieldsReadOnly { get; set; }
}

public sealed class ReviewLogDto
{
    /// <summary>Windows: the review's own id (the iPhone doesn't store one).</summary>
    [JsonPropertyName("id")] public Guid? Id { get; set; }
    [JsonPropertyName("cardID")] public Guid CardId { get; set; }
    [JsonPropertyName("date")] public double Date { get; set; }
    [JsonPropertyName("rating")] public int Rating { get; set; } = 3;
    [JsonPropertyName("wasNew")] public bool WasNew { get; set; }
    [JsonPropertyName("intervalDays")] public double IntervalDays { get; set; }
}
